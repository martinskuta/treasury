namespace Treasury.Api.Accounts;

/// The currencies Treasury keeps Accounts in: every currency it can get an exchange rate for, i.e. EUR plus the ECB
/// reference rates served by Frankfurter (https://api.frankfurter.dev/v1/currencies), so any amount can be shown in
/// CHF. The single source of truth for validation, rate fetching and the web app (GET /api/currencies).
public static class Currencies
{
    /// ISO 4217 codes, alphabetical.
    public static readonly IReadOnlyList<string> All =
    [
        "AUD", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HUF", "IDR", "ILS", "INR", "ISK",
        "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    ];

    private static readonly HashSet<string> Set = [.. All];

    /// Whether <paramref name="currency"/> is one of <see cref="All"/> (an upper-case ISO code).
    public static bool IsSupported(string? currency) => currency is not null && Set.Contains(currency);
}
