using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Rules;

namespace Treasury.Api.Matching;

/// What happens to the remaining leg(s) of a pair whose other leg went away (import undone, manual Transaction
/// deleted): they are matched again (they may now be some other Transaction's single candidate), and those still
/// unlinked and Uncategorised go through the Rules, as if freshly imported.
public sealed class FormerLegs(TreasuryDbContext db, TransactionMatcher matcher, RuleEngine rules)
{
    /// Call after the unlink is saved. Saves its own changes.
    public async Task SettleAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken ct = default)
    {
        if (transactionIds.Count == 0) return;
        var legs = await db.Transactions.Include(t => t.Account)
            .Where(t => transactionIds.Contains(t.Id)).ToListAsync(ct);
        await matcher.MatchAsync(legs, ct);
        await db.SaveChangesAsync(ct);
        await rules.ApplyAsync(legs.Where(t => t.LinkedTransactionId == null && t.CategoryId == null).ToList(), ct);
    }
}
