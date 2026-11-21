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

    /// Adds one more currency's rate (how many units one EUR buys) to a business day already published.
    public void Publish(string date, string currency, decimal perEur)
    {
        var day = DateOnly.Parse(date);
        var rates = new Dictionary<string, decimal>(_published[day].PerEur) { [currency] = perEur };
        _published[day] = new DailyRates(day, rates);
    }

    public Task<IReadOnlyList<DailyRates>> GetDailyRatesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        Requests.Enqueue((from, to));
        IReadOnlyList<DailyRates> days = _published.Values.Where(d => d.Date >= from && d.Date <= to)
            .OrderBy(d => d.Date).ToList();
        return Task.FromResult(days);
    }
}
