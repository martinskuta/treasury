using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Accounts;

/// How up to date Accounts are (#37), computed on read and never stored, so undoing an import moves it back by itself.
///
/// An Account's reach is the later of its latest Transaction date and the latest closing balance date of its
/// statements: a statement proves the Account is complete up to its closing date even when nothing happened on the
/// last days. Closing balances are on the Source file for a single-Account import and per currency for a
/// Multi-currency account's (ADR-0009). A Multi-currency account's reach is the latest reach among its Accounts.
///
/// The last import date is when the most recent Source file was imported. An Account in a Multi-currency account
/// reports its Multi-currency account's: that one's Source files are the ones that feed it, whether or not a
/// particular file had anything in the Account's currency.
///
/// An Account lags when its reach is before the last day of the last complete calendar month, or when it has no
/// reach at all.
public sealed class AccountReach
{
    private readonly Dictionary<Guid, DateOnly> _reach;
    private readonly Dictionary<Guid, DateTimeOffset> _accountImports;
    private readonly Dictionary<Guid, DateTimeOffset> _multiCurrencyAccountImports;
    private readonly DateOnly _lastCompleteMonthEnd;

    private AccountReach(Dictionary<Guid, DateOnly> reach, Dictionary<Guid, DateTimeOffset> accountImports,
        Dictionary<Guid, DateTimeOffset> multiCurrencyAccountImports, DateOnly lastCompleteMonthEnd)
    {
        _reach = reach;
        _accountImports = accountImports;
        _multiCurrencyAccountImports = multiCurrencyAccountImports;
        _lastCompleteMonthEnd = lastCompleteMonthEnd;
    }

    public static async Task<AccountReach> LoadAsync(TreasuryDbContext db, TimeProvider clock,
        IReadOnlyCollection<Account> accounts)
    {
        var ids = accounts.Select(a => a.Id).ToList();
        var multiCurrencyAccountIds = accounts.Where(a => a.MultiCurrencyAccountId != null)
            .Select(a => a.MultiCurrencyAccountId!.Value).Distinct().ToList();

        var transactions = await db.Transactions
            .Where(t => ids.Contains(t.AccountId))
            .GroupBy(t => t.AccountId)
            .Select(g => new { AccountId = g.Key, Date = g.Max(t => t.Date) })
            .ToListAsync();
        var closingBalances = await db.SourceFiles
            .Where(f => f.AccountId != null && ids.Contains(f.AccountId.Value) && f.ClosingBalanceDate != null)
            .GroupBy(f => f.AccountId!.Value)
            .Select(g => new { AccountId = g.Key, Date = g.Max(f => f.ClosingBalanceDate!.Value) })
            .ToListAsync();
        var currencyClosingBalances = await db.SourceFileClosingBalances
            .Where(b => ids.Contains(b.AccountId))
            .GroupBy(b => b.AccountId)
            .Select(g => new { AccountId = g.Key, Date = g.Max(b => b.Date) })
            .ToListAsync();
        var reach = transactions.Concat(closingBalances).Concat(currencyClosingBalances)
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Date));

        var accountImports = await db.SourceFiles
            .Where(f => f.AccountId != null && ids.Contains(f.AccountId.Value))
            .GroupBy(f => f.AccountId!.Value)
            .Select(g => new { Id = g.Key, ImportedAt = g.Max(f => f.ImportedAt) })
            .ToDictionaryAsync(x => x.Id, x => x.ImportedAt);
        var multiCurrencyAccountImports = await db.SourceFiles
            .Where(f => f.MultiCurrencyAccountId != null && multiCurrencyAccountIds.Contains(f.MultiCurrencyAccountId.Value))
            .GroupBy(f => f.MultiCurrencyAccountId!.Value)
            .Select(g => new { Id = g.Key, ImportedAt = g.Max(f => f.ImportedAt) })
            .ToDictionaryAsync(x => x.Id, x => x.ImportedAt);

        return new AccountReach(reach, accountImports, multiCurrencyAccountImports, LastCompleteMonth.LastDay(clock));
    }

    /// The date the Account's data reaches; null when it has neither a Transaction nor a closing balance.
    public DateOnly? Of(Account account) => _reach.TryGetValue(account.Id, out var date) ? date : null;

    /// The latest reach among the Multi-currency account's Accounts; null when none of them has one.
    public DateOnly? Of(MultiCurrencyAccount multiCurrencyAccount) => multiCurrencyAccount.Accounts.Max(Of);

    /// When the Account's most recent Source file was imported (its Multi-currency account's, for an Account in
    /// one); null when there is none.
    public DateTimeOffset? LastImportedAt(Account account) =>
        _accountImports.TryGetValue(account.Id, out var at) ? at
        : account.MultiCurrencyAccountId is { } id && _multiCurrencyAccountImports.TryGetValue(id, out at) ? at
        : null;

    /// When the Multi-currency account's most recent Source file was imported; null when there is none.
    public DateTimeOffset? LastImportedAt(MultiCurrencyAccount multiCurrencyAccount) =>
        _multiCurrencyAccountImports.TryGetValue(multiCurrencyAccount.Id, out var at) ? at : null;

    /// Whether the Account's reach leaves the last complete calendar month unfinished.
    public bool IsLagging(Account account) => IsLagging(Of(account));

    /// Whether the Multi-currency account's reach leaves the last complete calendar month unfinished.
    public bool IsLagging(MultiCurrencyAccount multiCurrencyAccount) => IsLagging(Of(multiCurrencyAccount));

    private bool IsLagging(DateOnly? reach) => reach is null || reach < _lastCompleteMonthEnd;
}
