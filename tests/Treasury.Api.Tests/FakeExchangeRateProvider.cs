using System.Collections.Concurrent;
using Treasury.Api.ExchangeRates;

namespace Treasury.Api.Tests;

/// Stands in for Frankfurter: serves only the EUR-based daily rates a test has published.
public sealed class FakeExchangeRateProvider : IExchangeRateProvider
{
    private readonly ConcurrentDictionary<DateOnly, DailyRates> _published = new();

    /// Every range the API asked for (from, to).
    public ConcurrentQueue<(DateOnly From, DateOnly To)> Requests { get; } = new();

    /// Publishes one business day's rates: how many CHF / CZK / USD one EUR buys.
    public void Publish(DateOnly date, decimal chf, decimal czk = 24.4m, decimal usd = 1.1m) =>
        _published[date] = new DailyRates(date, new Dictionary<string, decimal>
        {
            ["CHF"] = chf, ["CZK"] = czk, ["USD"] = usd,
        });

    public void Publish(string date, decimal chf, decimal czk = 24.4m, decimal usd = 1.1m) =>
        Publish(DateOnly.Parse(date), chf, czk, usd);

    public Task<IReadOnlyList<DailyRates>> GetDailyRatesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        Requests.Enqueue((from, to));
        IReadOnlyList<DailyRates> days = _published.Values.Where(d => d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ToList();
        return Task.FromResult(days);
    }
}
