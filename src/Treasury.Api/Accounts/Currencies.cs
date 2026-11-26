namespace Treasury.Api.Accounts;

/// The currencies Treasury keeps Accounts in: every currency it can get an exchange rate for, i.e. EUR plus the ECB
/// reference rates served by Frankfurter (https://api.frankfurter.dev/v1/currencies), so any amount can be shown in
/// CHF, plus the <see cref="Retired"/> currencies. The single source of truth for validation, rate fetching and the
/// web app (GET /api/currencies).
public static class Currencies
{
    /// Currencies replaced by the euro at a fixed rate. The ECB published their rates (and Frankfurter still serves
    /// them) up to the day before <see cref="RetiredCurrency.RetiredOn"/>; from that day on they convert at
    /// <see cref="RetiredCurrency.PerEur"/>. Old statements (a Revolut HRK pocket, say) still show them.
    public static readonly IReadOnlyDictionary<string, RetiredCurrency> Retired = new Dictionary<string, RetiredCurrency>
    {
        ["BGN"] = new(new DateOnly(2026, 1, 1), 1.95583m),
        ["HRK"] = new(new DateOnly(2023, 1, 1), 7.53450m),
    };

    /// ISO 4217 codes, alphabetical.
    public static readonly IReadOnlyList<string> All =
    [
        "AUD", "BGN", "BRL", "CAD", "CHF", "CNY", "CZK", "DKK", "EUR", "GBP", "HKD", "HRK", "HUF", "IDR", "ILS", "INR",
        "ISK", "JPY", "KRW", "MXN", "MYR", "NOK", "NZD", "PHP", "PLN", "RON", "SEK", "SGD", "THB", "TRY", "USD", "ZAR",
    ];

    private static readonly HashSet<string> Set = [.. All];

    /// Whether <paramref name="currency"/> is one of <see cref="All"/> (an upper-case ISO code).
    public static bool IsSupported(string? currency) => currency is not null && Set.Contains(currency);
}

/// A currency the euro replaced on <paramref name="RetiredOn"/>, at <paramref name="PerEur"/> units per EUR.
public sealed record RetiredCurrency(DateOnly RetiredOn, decimal PerEur);
