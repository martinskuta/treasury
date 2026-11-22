using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Transactions;

namespace Treasury.Api.Tags;

/// Puts Tags on Transactions and takes them off. A Transaction carries a Tag when it has at least one
/// <see cref="TransactionTag"/> link for it (from any source). Rules for each source:
/// - by hand: adding creates a Manual link and deletes a removal; removing deletes every link of that Tag
///   (whatever the source) and records a <see cref="TransactionTagRemoval"/>, so no automatic source adds it again.
/// - date range: DateRange links follow the Tag's range and Owner limit, except where a removal exists.
/// - Rules (#15): add Rule links with <see cref="AddAutomaticallyAsync"/>, which skips removed Tags.
/// - imports: the Tags an Integration suggests for a row get Integration links on the new Transaction, once, when it
///   is imported (<see cref="SuggestedTagsImportStep"/>).
/// All methods change tracked entities only; the caller saves. They read the database, so save earlier changes
/// that they depend on (e.g. a removal made in the same request) first.
public static class Tagging
{
    public static Task AddByHandAsync(TreasuryDbContext db, Guid transactionId, IReadOnlyCollection<Guid> tagIds) =>
        AddByHandAsync(db, [transactionId], tagIds);

    public static Task RemoveByHandAsync(TreasuryDbContext db, Guid transactionId, IReadOnlyCollection<Guid> tagIds) =>
        RemoveByHandAsync(db, [transactionId], tagIds);

    /// Adds each Tag by hand to each of the Transactions (bulk edit, #17): Manual links, removals deleted.
    public static async Task AddByHandAsync(TreasuryDbContext db, IReadOnlyCollection<Guid> transactionIds,
        IReadOnlyCollection<Guid> tagIds)
    {
        if (tagIds.Count == 0 || transactionIds.Count == 0) return;
        db.TransactionTagRemovals.RemoveRange(await db.TransactionTagRemovals
            .Where(r => transactionIds.Contains(r.TransactionId) && tagIds.Contains(r.TagId)).ToListAsync());
        var existing = await PairsAsync(db.TransactionTags
            .Where(l => transactionIds.Contains(l.TransactionId) && l.Source == TransactionTagSource.Manual && tagIds.Contains(l.TagId)));
        db.TransactionTags.AddRange(
            from transactionId in transactionIds.Distinct()
            from tagId in tagIds.Distinct()
            where !existing.Contains((transactionId, tagId))
            select new TransactionTag { TransactionId = transactionId, TagId = tagId, Source = TransactionTagSource.Manual });
    }

    /// Removes each Tag by hand from each of the Transactions: every link of the Tag goes, and the removal sticks.
    public static async Task RemoveByHandAsync(TreasuryDbContext db, IReadOnlyCollection<Guid> transactionIds,
        IReadOnlyCollection<Guid> tagIds)
    {
        if (tagIds.Count == 0 || transactionIds.Count == 0) return;
        db.TransactionTags.RemoveRange(await db.TransactionTags
            .Where(l => transactionIds.Contains(l.TransactionId) && tagIds.Contains(l.TagId)).ToListAsync());
        var removed = await PairsAsync(db.TransactionTagRemovals
            .Where(r => transactionIds.Contains(r.TransactionId) && tagIds.Contains(r.TagId)));
        db.TransactionTagRemovals.AddRange(
            from transactionId in transactionIds.Distinct()
            from tagId in tagIds.Distinct()
            where !removed.Contains((transactionId, tagId))
            select new TransactionTagRemoval { TransactionId = transactionId, TagId = tagId });
    }

    /// Adds links of an automatic source (e.g. <see cref="TransactionTagSource.Rule"/>) unless a Member removed that
    /// Tag from that Transaction. Never touches other sources' links.
    public static async Task AddAutomaticallyAsync(TreasuryDbContext db, TransactionTagSource source,
        IReadOnlyCollection<(Guid TransactionId, Guid TagId)> links)
    {
        if (source == TransactionTagSource.Manual) throw new ArgumentException("Use AddByHandAsync for manual Tags.");
        if (links.Count == 0) return;
        var transactionIds = links.Select(l => l.TransactionId).Distinct().ToList();
        var removed = await RemovalsAsync(db, transactionIds);
        var existing = await PairsAsync(db.TransactionTags
            .Where(l => transactionIds.Contains(l.TransactionId) && l.Source == source));
        db.TransactionTags.AddRange(links.Distinct().Where(l => !removed.Contains(l) && !existing.Contains(l))
            .Select(l => new TransactionTag { TransactionId = l.TransactionId, TagId = l.TagId, Source = source }));
    }

    /// Brings the date-range Tags of these Transactions up to date with their current date and Owner: call it for
    /// new Transactions (import, manual add) and after a Transaction's date or Owner changed.
    public static async Task ApplyDateRangesAsync(TreasuryDbContext db, IReadOnlyCollection<Transaction> transactions)
    {
        if (transactions.Count == 0) return;
        var ranged = await db.Tags.Where(t => t.DateFrom != null && t.DateTo != null).ToListAsync();
        var ids = transactions.Select(t => t.Id).ToList();
        var links = await db.TransactionTags
            .Where(l => ids.Contains(l.TransactionId) && l.Source == TransactionTagSource.DateRange).ToListAsync();
        var removed = await RemovalsAsync(db, ids);

        var wanted = (from t in transactions
                      from tag in ranged
                      where tag.AppliesAutomaticallyTo(t.Date, t.Owner) && !removed.Contains((t.Id, tag.Id))
                      select (t.Id, tag.Id)).ToHashSet();
        Sync(db, links, wanted);
    }

    /// Brings one Tag's DateRange links up to date across all Transactions (after the Tag was created or its range
    /// or Owner limit changed). Removals are respected.
    public static async Task ApplyDateRangeAsync(TreasuryDbContext db, Tag tag)
    {
        var links = await db.TransactionTags
            .Where(l => l.TagId == tag.Id && l.Source == TransactionTagSource.DateRange).ToListAsync();
        var wanted = new HashSet<(Guid, Guid)>();
        if (tag is { DateFrom: { } from, DateTo: { } to })
        {
            var removed = db.TransactionTagRemovals.Where(r => r.TagId == tag.Id).Select(r => r.TransactionId);
            var matching = db.Transactions.Where(t => t.Date >= from && t.Date <= to && !removed.Contains(t.Id));
            if (tag.IsOwnerLimited)
            {
                var family = tag.LimitToFamily;
                var members = tag.LimitToMemberIds;
                matching = matching.Where(t => (family && t.OwnerMemberId == null)
                    || (t.OwnerMemberId != null && members.Contains(t.OwnerMemberId.Value)));
            }
            wanted = (await matching.Select(t => t.Id).ToListAsync()).Select(id => (id, tag.Id)).ToHashSet();
        }
        Sync(db, links, wanted);
    }

    /// Deletes the given links that aren't wanted and adds the wanted ones that are missing (all DateRange).
    private static void Sync(TreasuryDbContext db, List<TransactionTag> links, HashSet<(Guid TransactionId, Guid TagId)> wanted)
    {
        db.TransactionTags.RemoveRange(links.Where(l => !wanted.Contains((l.TransactionId, l.TagId))));
        var have = links.Select(l => (l.TransactionId, l.TagId)).ToHashSet();
        db.TransactionTags.AddRange(wanted.Where(w => !have.Contains(w)).Select(w =>
            new TransactionTag { TransactionId = w.TransactionId, TagId = w.TagId, Source = TransactionTagSource.DateRange }));
    }

    private static Task<HashSet<(Guid TransactionId, Guid TagId)>> RemovalsAsync(TreasuryDbContext db, List<Guid> transactionIds) =>
        PairsAsync(db.TransactionTagRemovals.Where(r => transactionIds.Contains(r.TransactionId)));

    /// The (Transaction, Tag) pairs of the links or removals the query selects, in one query.
    private static async Task<HashSet<(Guid TransactionId, Guid TagId)>> PairsAsync<T>(IQueryable<T> query)
        where T : class, ITransactionTagPair =>
        (await query.Select(x => new { x.TransactionId, x.TagId }).ToListAsync())
        .Select(x => (x.TransactionId, x.TagId)).ToHashSet();

    /// The Tags on these Transactions, for responses: Tag id/name with the sources that put it there.
    public static async Task<Dictionary<Guid, List<TagOnTransaction>>> LoadAsync(TreasuryDbContext db,
        IReadOnlyCollection<Guid> transactionIds)
    {
        if (transactionIds.Count == 0) return [];
        var rows = await db.TransactionTags.Where(l => transactionIds.Contains(l.TransactionId))
            .Join(db.Tags, l => l.TagId, t => t.Id, (l, t) => new { l.TransactionId, l.TagId, t.Name, l.Source })
            .ToListAsync();
        return rows.GroupBy(r => r.TransactionId).ToDictionary(g => g.Key, g => g
            .GroupBy(r => (r.TagId, r.Name))
            .Select(t => new TagOnTransaction(t.Key.TagId, t.Key.Name, t.Select(r => r.Source).Order().ToArray()))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
    }
}

public record TagOnTransaction(Guid Id, string Name, TransactionTagSource[] Sources);
