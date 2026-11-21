using System.Globalization;

namespace Treasury.Api.Integrations;

/// "Generic CSV" (key generic-csv): a simple CSV anyone can produce from a bank export with a spreadsheet, so
/// Treasury is usable before a bank has its own Integration. Format (documented in README "Generic CSV"):
/// - UTF-8 (a byte order mark is fine), first line is a header; column names are case-insensitive, in any order
/// - separator ',' or ';' (whichever the header uses); fields may be quoted with "..." ("" inside = a quote)
/// - required columns: date (yyyy-MM-dd or dd.MM.yyyy), amount ('.' decimal point, optional leading '-',
///   optional ' thousands separators; negative = money out), description
/// - optional columns: counterparty, reference, currency (must be the Account's), original_amount and
///   original_currency (for foreign-currency purchases; both or neither)
/// - blank lines are ignored. No Account identifier and no closing balance.
public sealed class GenericCsvIntegration : IIntegration
{
    public string Key => "generic-csv";

    public string Name => "Generic CSV";

    public bool CanProvideIdentifier => false;

    public bool HoldsSeveralCurrencies => false;

    private static readonly string[] Required = ["date", "amount", "description"];

    private static readonly string[] Known =
        ["date", "amount", "description", "counterparty", "reference", "currency", "original_amount", "original_currency"];

    public ParsedStatement Read(SourceFileContent file)
    {
        var lines = file.ReadText().Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var headerIndex = Array.FindIndex(lines, l => l.Trim().Length > 0);
        if (headerIndex < 0)
            throw new StatementFormatException("The file is empty; expected a header line with date, amount, description.");

        var header = lines[headerIndex];
        var separator = header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
        var columns = CsvLine.Split(header, separator, headerIndex + 1)
            .Select(c => c.Trim().ToLowerInvariant().Replace(' ', '_'))
            .ToList();
        var missing = Required.Where(r => !columns.Contains(r)).ToList();
        if (missing.Count > 0)
            throw new StatementFormatException(
                $"The header line is missing the column(s) {string.Join(", ", missing)}; it needs date, amount and description.");
        var index = Known.ToDictionary(k => k, k => columns.IndexOf(k));

        var rows = new List<StatementRow>();
        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            var at = $"line {i + 1}";
            var fields = CsvLine.Split(lines[i], separator, i + 1);
            if (fields.Count != columns.Count)
                throw new StatementFormatException(
                    $"{at}: expected {columns.Count} fields like the header, found {fields.Count}. " +
                    "Quote values that contain the separator.");
            string? Get(string column) =>
                index[column] is >= 0 and var c && fields[c].Trim().Length > 0 ? fields[c].Trim() : null;

            var originalAmount = Get("original_amount");
            rows.Add(new StatementRow(
                ParseDate(Get("date"), at),
                ParseAmount(Get("amount"), at, "amount"),
                Get("currency")?.ToUpperInvariant(),
                Get("description") ?? throw new StatementFormatException($"{at}: the description is empty."),
                Get("counterparty"),
                Get("reference"),
                originalAmount is null ? null : ParseAmount(originalAmount, at, "original_amount"),
                Get("original_currency")?.ToUpperInvariant()));
        }
        return new ParsedStatement(rows);
    }

    private static DateOnly ParseDate(string? value, string at) =>
        DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : throw new StatementFormatException($"{at}: '{value}' is not a date (use yyyy-MM-dd or dd.MM.yyyy).");

    private static decimal ParseAmount(string? value, string at, string column) =>
        decimal.TryParse(value?.Replace("'", "").Replace("’", ""),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new StatementFormatException($"{at}: '{value}' is not an {column} (use e.g. -1234.50).");
}
