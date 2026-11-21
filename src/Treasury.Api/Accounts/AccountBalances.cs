using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Accounts;

/// An Account's balance (spec #1): the opening balance plus ALL of its Transactions dated strictly after the opening
/// date (a Transaction on the opening date is part of the opening balance), whatever the Transactions' Owners.
///
/// The closing-balance warning (#11) is computed here on read, never stored: it compares the Account's most recent
/// statement closing balance (latest closing date; same date -> latest import, then the newer v7 Source file id) with the calculated balance on that
/// date. Closing balances dated before the opening date can't be compared and are ignored. Because it is computed on
/// read, the warning clears by itself once the balances match again (missing data imported, doubled import undone).
public static class AccountBalances
{
    /// The calculated balance at the end of <paramref name="date"/> (dates before the opening date give the opening balance).
    public static async Task<decimal> OnAsync(TreasuryDbContext db, Account account, DateOnly date, CancellationToken ct = default) =>
        account.OpeningBalance + await db.Transactions
            .Where(t => t.AccountId == account.Id && t.Date > account.OpeningDate && t.Date <= date)
            .SumAsync(t => t.Amount, ct);

    /// The current balance of each Account.
    public static async Task<Dictionary<Guid, decimal>> CurrentAsync(TreasuryDbContext db, IReadOnlyCollection<Account> accounts)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var sums = await db.Transactions
            .Where(t => ids.Contains(t.AccountId) && t.Date > t.Account.OpeningDate)
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Sum = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(x => x.AccountId, x => x.Sum);
        return accounts.ToDictionary(a => a.Id, a => a.OpeningBalance + sums.GetValueOrDefault(a.Id));
    }

    /// The Accounts whose most recent statement closing balance differs from the calculated balance on that date.
    public static async Task<Dictionary<Guid, BalanceMismatch>> MismatchesAsync(TreasuryDbContext db,
        IReadOnlyCollection<Account> accounts)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var single = await db.SourceFiles
            .Where(f => f.AccountId != null && ids.Contains(f.AccountId.Value) && f.ClosingBalanceDate != null
                && f.ClosingBalanceAmount != null && f.ClosingBalanceDate >= f.Account!.OpeningDate)
            .GroupBy(f => f.AccountId!.Value)
            .Select(g => g.OrderByDescending(f => f.ClosingBalanceDate).ThenByDescending(f => f.ImportedAt).ThenByDescending(f => f.Id)
                .Select(f => new StatementBalance(f.AccountId!.Value, f.Id, f.FileName, f.ImportedAt,
                    f.ClosingBalanceDate!.Value, f.ClosingBalanceAmount!.Value))
                .First())
            .ToListAsync();
        // A Multi-currency account's Source file has one closing balance per currency, each for its Account (#25).
        var perCurrency = await (
                from b in db.SourceFileClosingBalances
                join f in db.SourceFiles on b.SourceFileId equals f.Id
                join a in db.Accounts on b.AccountId equals a.Id
                where ids.Contains(b.AccountId) && b.Date >= a.OpeningDate
                select new StatementBalance(b.AccountId, f.Id, f.FileName, f.ImportedAt, b.Date, b.Amount))
            .ToListAsync();
        var latest = single.Concat(perCurrency).GroupBy(s => s.AccountId)
            .Select(g => g.OrderByDescending(s => s.Date).ThenByDescending(s => s.ImportedAt).ThenByDescending(s => s.Id).First());

        var result = new Dictionary<Guid, BalanceMismatch>();
        foreach (var statement in latest)
        {
            var account = accounts.First(a => a.Id == statement.AccountId);
            var calculated = await OnAsync(db, account, statement.Date);
            if (calculated != statement.Amount)
                result[account.Id] = new BalanceMismatch(statement.Date, statement.Amount, calculated, statement.Id, statement.FileName);
        }
        return result;
    }

    private sealed record StatementBalance(Guid AccountId, Guid Id, string FileName, DateTimeOffset ImportedAt,
        DateOnly Date, decimal Amount);
}

/// A statement's closing balance that doesn't match the Account's calculated balance on the same date.
public sealed record BalanceMismatch(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance,
    Guid SourceFileId, string FileName);
