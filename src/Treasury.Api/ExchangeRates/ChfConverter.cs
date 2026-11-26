using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.ExchangeRates;

/// Converts amounts to the reporting currency (CHF) with the most recent stored rate on or before a date
/// (so weekends and holidays use the previous business day). Scoped; use it from endpoints, matching and reports.
public sealed class ChfConverter(TreasuryDbContext db)
{
    public const string ReportingCurrency = "CHF";

    /// One amount in CHF, or null when no rate on or before <paramref name="date"/> is stored yet.
    public async Task<decimal?> ToChfAsync(decimal amount, string currency, DateOnly date, CancellationToken ct = default) =>
        (await LoadAsync(date, date, ct)).ToChf(amount, currency, date);

    /// Loads, in one query, every rate needed to convert amounts dated between <paramref name="from"/> and
    /// <paramref name="to"/>. Use this for lists and reports instead of calling ToChfAsync per row.
    public async Task<ChfRates> LoadAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) (from, to) = (to, from);
        // The last business day on or before `from` covers dates up to the next stored day.
        var start = await db.ExchangeRates.Where(r => r.Date <= from).MaxAsync(r => (DateOnly?)r.Date, ct) ?? from;
        var rows = await db.ExchangeRates.AsNoTracking().Where(r => r.Date >= start && r.Date <= to)
            .Select(r => new { r.Currency, r.Date, r.PerEur }).ToListAsync(ct);
        return new ChfRates(rows.GroupBy(r => r.Currency).ToDictionary(
            g => g.Key,
            g => g.OrderBy(r => r.Date).Select(r => (r.Date, r.PerEur)).ToArray()));
    }
}

/// Rates loaded by <see cref="ChfConverter.LoadAsync"/>; converts in memory.
public sealed class ChfRates
{
    private readonly Dictionary<string, (DateOnly Date, decimal PerEur)[]> _byCurrency;

    internal ChfRates(Dictionary<string, (DateOnly Date, decimal PerEur)[]> byCurrency) => _byCurrency = byCurrency;

    /// <paramref name="amount"/> in CHF, rounded to 0.01 (midpoint away from zero); null when a rate is missing.
    /// CHF amounts are returned unchanged. Only dates inside the loaded range are supported.
    public decimal? ToChf(decimal amount, string currency, DateOnly date) =>
        Convert(amount, currency, ChfConverter.ReportingCurrency, date);

    /// <paramref name="amount"/> in <paramref name="to"/>, rounded to 0.01 (midpoint away from zero); null when a rate
    /// is missing. Amounts already in <paramref name="to"/> are returned unchanged.
    public decimal? Convert(decimal amount, string from, string to, DateOnly date)
    {
        if (from == to) return amount;
        if (PerEur(from, date) is not { } fromRate || PerEur(to, date) is not { } toRate) return null;
        return Math.Round(amount / fromRate * toRate, 2, MidpointRounding.AwayFromZero);
    }

    /// Units of <paramref name="currency"/> per EUR on the most recent stored day on or before <paramref name="date"/>.
    private decimal? PerEur(string currency, DateOnly date)
    {
        if (currency == "EUR") return 1m;
        if (Accounts.Currencies.Retired.TryGetValue(currency, out var retired) && date >= retired.RetiredOn)
            return retired.PerEur;
        if (!_byCurrency.TryGetValue(currency, out var rates)) return null;
        // Binary search for the last entry with Date <= date.
        int lo = 0, hi = rates.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (rates[mid].Date <= date) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found < 0 ? null : rates[found].PerEur;
    }
}

/// Today's rates (by <see cref="TimeProvider"/>), for showing current balances in other currencies.
public sealed class TodaysRates(ChfRates rates, DateOnly today)
{
    public DateOnly Today { get; } = today;

    public static async Task<TodaysRates> LoadAsync(ChfConverter converter, TimeProvider clock, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new TodaysRates(await converter.LoadAsync(today, today, ct), today);
    }

    /// <paramref name="amount"/> in CHF at today's rate; null when the rate is missing.
    public decimal? ToChf(decimal amount, string currency) => rates.ToChf(amount, currency, Today);

    /// <paramref name="amount"/> in <paramref name="to"/> at today's rates; null when a rate is missing.
    public decimal? Convert(decimal amount, string from, string to) => rates.Convert(amount, from, to, Today);
}
