using System.Globalization;

namespace Treasury.Api.Integrations;

/// "Revolut" (key revolut): Revolut's account statement CSV, exported in English (documented in README "Revolut").
/// - header Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance
/// - only rows of Product Current with State COMPLETED are read; a file may hold several currencies
/// - no Account identifier; a closing balance and a balance before the first row per currency
public sealed class RevolutIntegration : IIntegration
{
    public string Key => "revolut";

    public string Name => "Revolut";

    public bool CanProvideIdentifier => false;

    /// One Revolut account holds several currencies (GLOSSARY: Multi-currency account).
    public bool HoldsSeveralCurrencies => true;

    private const int Type = 0, Product = 1, CompletedDate = 3, Description = 4, Amount = 5, Fee = 6, Currency = 7,
        State = 8, Balance = 9;

    public ParsedStatement Read(SourceFileContent file)
    {
        var lines = file.ReadText().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var read = new List<ReadRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var fields = CsvLine.Split(lines[i], ',', i + 1).Select(f => f.Trim()).ToList();
            if (!Is(fields[Product], "Current") || !Is(fields[State], "Completed")) continue;
            read.Add(new ReadRow(
                DateTime.ParseExact(fields[CompletedDate], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                fields[Type], fields[Description],
                decimal.Parse(fields[Amount], CultureInfo.InvariantCulture),
                decimal.Parse(fields[Fee], CultureInfo.InvariantCulture),
                fields[Currency].ToUpperInvariant(),
                decimal.Parse(fields[Balance], CultureInfo.InvariantCulture)));
        }

        var rows = read.Select(r => new StatementRow(DateOnly.FromDateTime(r.Completed), r.Amount, r.Currency,
            r.Description, Counterparty(r.Type, r.Description))).ToList();
        var balances = read
            .Select((row, index) => (row, index))
            .GroupBy(r => r.row.Currency)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                // Completed Date order; rows completed at the same second keep their file order.
                var ordered = g.OrderBy(r => r.row.Completed).ThenBy(r => r.index).Select(r => r.row).ToList();
                var (first, last) = (ordered[0], ordered[^1]);
                return new CurrencyBalances(g.Key, DateOnly.FromDateTime(first.Completed),
                    first.Balance - first.Amount + first.Fee,
                    new ClosingBalance(DateOnly.FromDateTime(last.Completed), last.Balance));
            })
            .ToList();
        return new ParsedStatement(rows, BalancesByCurrency: balances);
    }

    private static string? Counterparty(string type, string description) =>
        Is(type, "Card Payment") || Is(type, "Card Refund") ? description
        : Is(type, "Transfer") ? StripPrefix(StripPrefix(description, "To "), "From ")
        : null;

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;

    /// Revolut writes Type and State in title case ("Card Payment") or, in older exports, upper case with
    /// underscores ("CARD_PAYMENT").
    private static bool Is(string value, string expected) =>
        string.Equals(value.Replace('_', ' '), expected, StringComparison.OrdinalIgnoreCase);

    private sealed record ReadRow(DateTime Completed, string Type, string Description, decimal Amount, decimal Fee,
        string Currency, decimal Balance);
}
