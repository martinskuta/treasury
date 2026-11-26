using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Matching;

namespace Treasury.Api.ExchangeRates;

/// Brings the stored rates of each of <see cref="ExchangeRate.FetchedCurrencies"/> up to date: the new days since
/// its last stored day, and past days back to the earliest date any Transaction or opening balance needs. Each
/// currency is checked on its own, so a newly supported currency is backfilled over days the others already have.
/// Run by <see cref="ExchangeRateJob"/>. Newly stored rates make CHF values known, so cross-currency pairs that
/// couldn't be matched without them are matched again (#16).
public sealed class ExchangeRateSync(TreasuryDbContext db, IExchangeRateProvider provider, TimeProvider clock,
    TransactionMatcher matcher, ILogger<ExchangeRateSync> logger)
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
        var stored = await db.ExchangeRates.GroupBy(r => r.Currency)
            .Select(g => new { Currency = g.Key, First = g.Min(r => r.Date), Last = g.Max(r => r.Date) })
            .ToDictionaryAsync(s => s.Currency, ct);

        // Every range some currency lacks; currencies lacking the same range share one fetch (each fetch asks
        // for all currencies; storing ones that are already there just overwrites them).
        var ranges = new SortedSet<(DateOnly From, DateOnly To)>();
        foreach (var currency in ExchangeRate.FetchedCurrencies)
        {
            // A retired currency has rates only up to the day before it was replaced; after that it has a fixed rate.
            var last = today;
            if (Accounts.Currencies.Retired.TryGetValue(currency, out var retired))
            {
                if (needed >= retired.RetiredOn) continue;
                last = Earliest(today, retired.RetiredOn.AddDays(-1));
            }
            if (!stored.TryGetValue(currency, out var s))
            {
                ranges.Add((needed.AddDays(-BackfillLeadDays), last));
                continue;
            }
            // Backfill: no stored rate on or before the earliest needed date yet.
            if (s.First > needed) ranges.Add((needed.AddDays(-BackfillLeadDays), s.First.AddDays(-1)));
            // Daily: anything published after the last stored day. A retired currency is complete once its rates
            // reach the last business days before it was replaced (no rate is published on its last few days).
            var complete = retired is not null && s.Last >= retired.RetiredOn.AddDays(-BackfillLeadDays);
            if (!complete && s.Last < last) ranges.Add((s.Last.AddDays(1), last));
        }
        foreach (var (from, to) in ranges)
        {
            if (ranges.Any(r => r != (from, to) && r.From <= from && r.To >= to)) continue; // fetched with a wider one
            await FetchAndStoreAsync(from, to, ct);
        }
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
        // Nothing new (e.g. a currency the provider doesn't publish for this range is asked for again): no rematch.
        var changed = await db.SaveChangesAsync(ct);
        if (changed == 0) return;
        logger.LogInformation("Stored {Changed} exchange rate(s) for {Count} day(s) between {From} and {To}",
            changed, days.Count, from, to);
        await RematchAsync(from, to, ct);
    }

    /// Unmatched Transactions in non-CHF Accounts dated in the newly covered range get another matching attempt
    /// (a cross-currency pair always has a non-CHF leg; dates outside the range already had a rate before).
    private async Task RematchAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var open = await db.Transactions.Include(t => t.Account)
            .Where(t => t.LinkedTransactionId == null && t.Account.Currency != ChfConverter.ReportingCurrency
                && t.Date >= from && t.Date <= to)
            .ToListAsync(ct);
        if (open.Count == 0) return;
        await matcher.MatchAsync(open, ct);
        await db.SaveChangesAsync(ct);
    }

    private static DateOnly Earliest(DateOnly today, params DateOnly?[] dates) =>
        dates.Where(d => d is not null).Select(d => d!.Value).Append(today).Min();
}
