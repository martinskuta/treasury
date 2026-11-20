using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Matching;

namespace Treasury.Api.ExchangeRates;

/// Brings the stored rates up to date: the new days since the last stored day, and past days back to the
/// earliest date any Transaction or opening balance needs. Run by <see cref="ExchangeRateJob"/>.
/// Newly stored rates make CHF values known, so cross-currency pairs that couldn't be matched without them are
/// matched again (#16).
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
