using System.Globalization;
using System.Net;

namespace Treasury.Api.ExchangeRates;

/// ECB reference rates from Frankfurter (https://frankfurter.dev, no API key), EUR-based.
public sealed class FrankfurterExchangeRateProvider(HttpClient http) : IExchangeRateProvider
{
    public static readonly Uri BaseAddress = new("https://api.frankfurter.dev/v1/");

    public async Task<IReadOnlyList<DailyRates>> GetDailyRatesAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var symbols = string.Join(',', ExchangeRate.FetchedCurrencies);
        using var response = await http.GetAsync($"{Format(from)}..{Format(to)}?base=EUR&symbols={symbols}", ct);
        // Frankfurter answers 404 when the range starts after the latest published day: nothing new yet.
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<RangeResponse>(ct);
        return (body?.Rates ?? [])
            .Select(day => new DailyRates(DateOnly.ParseExact(day.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture), day.Value))
            .Where(day => day.Date >= from && day.Date <= to)
            .OrderBy(day => day.Date)
            .ToList();
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record RangeResponse(Dictionary<string, Dictionary<string, decimal>>? Rates);
}
