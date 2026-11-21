namespace Treasury.Api.ExchangeRates;

/// One stored ECB reference rate: on <see cref="Date"/>, one EUR bought <see cref="PerEur"/> units of
/// <see cref="Currency"/>. Only business days have rows; EUR itself is implicit (always 1).
public class ExchangeRate
{
    /// The currencies stored per EUR: every one of <see cref="Accounts.Currencies.All"/> except EUR.
    public static readonly IReadOnlyList<string> FetchedCurrencies =
        [.. Accounts.Currencies.All.Where(c => c != "EUR")];

    public DateOnly Date { get; init; }

    public required string Currency { get; init; }

    public decimal PerEur { get; set; }
}
