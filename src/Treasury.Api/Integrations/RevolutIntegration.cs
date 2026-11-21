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

    private static readonly string[] Columns =
        ["Type", "Product", "Started Date", "Completed Date", "Description", "Amount", "Fee", "Currency", "State", "Balance"];

    private const string EnglishHint =
        "In the Revolut app, export the statement in English as CSV (Excel), then import that file.";

    public ParsedStatement Read(SourceFileContent file)
    {
        var lines = file.ReadText().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headerIndex = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (headerIndex < 0)
            throw new StatementFormatException($"The file is empty. {EnglishHint}");
        var header = CsvLine.Split(lines[headerIndex], ',', headerIndex + 1).Select(c => c.Trim()).ToList();
        if (!header.SequenceEqual(Columns, StringComparer.OrdinalIgnoreCase))
            throw new StatementFormatException(
                $"This isn't a Revolut account statement in English (expected the header {string.Join(',', Columns)}). " +
                "If it is a Revolut statement in another language, export the statement in English from the " +
                "Revolut app and import that file.");

        var read = new List<ReadRow>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var at = $"line {i + 1}";
            var fields = CsvLine.Split(lines[i], ',', i + 1).Select(f => f.Trim()).ToList();
            if (fields.Count != Columns.Length)
                throw new StatementFormatException(
                    $"{at}: expected {Columns.Length} fields like the header, found {fields.Count}. Was the file edited?");
            if (!Is(fields[Product], "Current") || !Is(fields[State], "Completed")) continue;
            read.Add(new ReadRow(
                ParseCompleted(fields[CompletedDate], at),
                fields[Type], fields[Description],
                ParseAmount(fields[Amount], at, "an Amount"),
                ParseAmount(fields[Fee], at, "a Fee"),
                fields[Currency].Length > 0
                    ? fields[Currency].ToUpperInvariant()
                    : throw new StatementFormatException($"{at}: the Currency is empty."),
                ParseAmount(fields[Balance], at, "a Balance")));
        }

        var rows = read.SelectMany(Rows).ToList();
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

    /// The movement itself and, for a non-zero Fee, a second row charged by Revolut (Revolut's balance is the
    /// previous balance + Amount − Fee).
    private static IEnumerable<StatementRow> Rows(ReadRow row)
    {
        var date = DateOnly.FromDateTime(row.Completed);
        yield return new StatementRow(date, row.Amount, row.Currency, row.Description,
            Counterparty(row.Type, row.Description));
        if (row.Fee != 0)
            yield return new StatementRow(date, -row.Fee, row.Currency, $"Fee: {row.Description}", "Revolut");
    }

    private static DateTime ParseCompleted(string value, string at) =>
        DateTime.TryParseExact(value, ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var completed)
            ? completed
            : throw new StatementFormatException(
                $"{at}: '{value}' is not a Completed Date (expected e.g. 2026-11-04 21:14:56).");

    private static decimal ParseAmount(string value, string at, string what) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new StatementFormatException($"{at}: '{value}' is not {what} (expected e.g. -26.25).");

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
