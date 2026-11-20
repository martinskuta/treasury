using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.ExchangeRates;

/// Brings the stored rates up to date: the new days since the last stored day, and past days back to the
/// earliest date any Transaction or opening balance needs. Run by <see cref="ExchangeRateJob"/>.
public sealed class ExchangeRateSync(TreasuryDbContext db, IExchangeRateProvider provider, TimeProvider clock,
    ILogger<ExchangeRateSync> logger)
{
    /// How far before a needed date a backfill starts, so a weekend or holiday date still gets the previous
    /// business day's rate (ranges contain business days only).
    public const int BackfillLeadDays = 7;

    public async Task SyncAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var needed = Earliest(today,
            await db.Transactions.MinAsync(t => (DateOnly?)t.Date, ct),
            await db.Accounts.MinAsync(a => (DateOnly?)a.OpeningDate, ct));
        var first = await db.ExchangeRates.MinAsync(r => (DateOnly?)r.Date, ct);
        var last = await db.ExchangeRates.MaxAsync(r => (DateOnly?)r.Date, ct);

        if (first is null || last is null)
        {
            await FetchAndStoreAsync(needed.AddDays(-BackfillLeadDays), today, ct);
            return;
        }
        // Backfill: no stored rate on or before the earliest needed date yet.
        if (first > needed) await FetchAndStoreAsync(needed.AddDays(-BackfillLeadDays), first.Value.AddDays(-1), ct);
        // Daily: anything published after the last stored day.
        if (last < today) await FetchAndStoreAsync(last.Value.AddDays(1), today, ct);
    }

    private async Task FetchAndStoreAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var days = await provider.GetDailyRatesAsync(from, to, ct);
        if (days.Count == 0) return;

        var dates = days.Select(d => d.Date).ToList();
        var existing = await db.ExchangeRates.Where(r => dates.Contains(r.Date))
            .ToDictionaryAsync(r => (r.Date, r.Currency), ct);
        foreach (var day in days)
        foreach (var currency in ExchangeRate.FetchedCurrencies)
        {
            if (!day.PerEur.TryGetValue(currency, out var perEur) || perEur <= 0) continue;
            if (existing.TryGetValue((day.Date, currency), out var row)) row.PerEur = perEur;
            else db.ExchangeRates.Add(new ExchangeRate { Date = day.Date, Currency = currency, PerEur = perEur });
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Stored exchange rates for {Count} day(s) between {From} and {To}", days.Count, from, to);
    }

    private static DateOnly Earliest(DateOnly today, params DateOnly?[] dates) =>
        dates.Where(d => d is not null).Select(d => d!.Value).Append(today).Min();
}
