namespace Treasury.Api.ExchangeRates;

/// One stored ECB reference rate: on <see cref="Date"/>, one EUR bought <see cref="PerEur"/> units of
/// <see cref="Currency"/>. Only business days have rows; EUR itself is implicit (always 1).
public class ExchangeRate
{
    /// The currencies stored per EUR. Together with EUR they cover <see cref="Accounts.Currencies.All"/>.
    public static readonly IReadOnlyList<string> FetchedCurrencies = ["CHF", "CZK", "USD"];

    public DateOnly Date { get; init; }

    public required string Currency { get; init; }

    public decimal PerEur { get; set; }
}
