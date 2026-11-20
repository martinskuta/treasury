using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Imports;
using Treasury.Api.Matching;
using Treasury.Api.Tags;
using Treasury.Api.Transactions;

namespace Treasury.Api.Rules;

/// Runs the Rules over Transactions (on import and on demand). For each Transaction, only Rules whose author can
/// see its Account and is still active take part; among those, in priority order:
/// - Category: the first matching Rule that has a Category sets it, unless the Category was set by hand or the
///   Transaction is linked (a Transfer has no Category, a Contribution leg carries its built-in one).
/// - Owner: the first matching Rule that has an Owner sets it, unless the Owner was set by hand or the Rule's Owner
///   has been deactivated meanwhile. Date-range Tags are then brought up to date (they may depend on the Owner), and
///   a linked pair's kind is re-evaluated (same Owner = Transfer, different = Contribution).
/// - Tags: every matching Rule adds its Tags, except Tags a Member removed from that Transaction by hand.
/// Changes tracked entities and saves (it reads Tag links from the database), inside the caller's DB transaction.
public sealed class RuleEngine(TreasuryDbContext db, TransactionMatcher matcher)
{
    public async Task<RuleRunResult> ApplyAsync(IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        if (transactions.Count == 0) return new(0, 0);
        var rules = await db.Rules.Include(r => r.TagLinks)
            .Where(r => db.Users.Any(m => m.Id == r.AuthorMemberId && m.DeactivatedAt == null))
            .OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct);
        if (rules.Count == 0) return new(0, 0);

        var accountIds = transactions.Select(t => t.AccountId).Distinct().ToList();
        var accountOwners = await db.Accounts.Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.OwnerMemberId, ct);
        var ownerIds = rules.Where(r => r.SetsOwner && r.OwnerMemberId != null).Select(r => r.OwnerMemberId!.Value).ToList();
        var activeOwners = (await db.Users.Where(m => ownerIds.Contains(m.Id) && m.DeactivatedAt == null)
            .Select(m => m.Id).ToListAsync(ct)).ToHashSet();

        var categorised = 0;
        var reowned = new List<Transaction>();
        var tags = new List<(Guid TransactionId, Guid TagId)>();
        foreach (var t in transactions)
        {
            var accountOwner = accountOwners[t.AccountId];
            var matching = rules
                .Where(r => accountOwner == null || accountOwner == r.AuthorMemberId)
                .Where(r => r.Matches(t)).ToList();
            if (matching.Count == 0) continue;

            if (!t.ManuallySet.HasFlag(ManuallySetFields.Category) && t.LinkedTransactionId == null
                && matching.FirstOrDefault(r => r.CategoryId != null) is { } categoryRule
                && t.CategoryId != categoryRule.CategoryId)
            {
                t.CategoryId = categoryRule.CategoryId;
                categorised++;
            }

            if (!t.ManuallySet.HasFlag(ManuallySetFields.Owner)
                && matching.FirstOrDefault(r => r.Owner is { } o && (o.IsFamily || activeOwners.Contains(o.MemberId!.Value)))
                    is { Owner: { } owner }
                && t.Owner != owner)
            {
                t.Owner = owner;
                reowned.Add(t);
            }

            tags.AddRange(matching.SelectMany(r => r.TagLinks).Select(l => (t.Id, l.TagId)));
        }

        await matcher.RefreshLinksAsync(reowned, ct); // a linked leg's new Owner may turn Transfer <-> Contribution
        await db.SaveChangesAsync(ct);
        if (reowned.Count > 0)
        {
            await Tagging.ApplyDateRangesAsync(db, reowned);
            await db.SaveChangesAsync(ct);
        }
        await Tagging.AddAutomaticallyAsync(db, TransactionTagSource.Rule, tags.Distinct().ToList());
        await db.SaveChangesAsync(ct);
        return new(transactions.Count, categorised);
    }
}

/// Checked = Transactions the Rules looked at; Categorised = how many got a Category from a Rule.
public record RuleRunResult(int Checked, int Categorised);

/// Classify stage, after the date-range Tags step: runs the Rules over the newly imported Transactions.
public sealed class RulesImportStep(RuleEngine engine) : IImportStep
{
    public ImportStage Stage => ImportStage.Classify;

    public Task RunAsync(ImportContext context, CancellationToken ct) => engine.ApplyAsync(context.Transactions, ct);
}

public static class RuleSetup
{
    /// Call after AddTags(): the Rules step must run after the date-range Tags step (same stage, registration order).
    public static void AddRules(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<RuleEngine>();
        builder.Services.AddScoped<IImportStep, RulesImportStep>();
    }
}
