namespace Treasury.Api.ExchangeRates;

/// Where daily exchange rates come from (Frankfurter in production, a fake in tests).
public interface IExchangeRateProvider
{
    /// The EUR-based rates of every business day from <paramref name="from"/> to <paramref name="to"/> (inclusive)
    /// for <see cref="ExchangeRate.FetchedCurrencies"/>. Empty when nothing was published in that range.
    Task<IReadOnlyList<DailyRates>> GetDailyRatesAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

/// One business day's rates: how many units of each currency one EUR buys.
public record DailyRates(DateOnly Date, IReadOnlyDictionary<string, decimal> PerEur);
