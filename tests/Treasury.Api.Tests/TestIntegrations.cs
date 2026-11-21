using System.Globalization;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests;

/// Fake Integration behind the API seam (no real bank formats in API tests), with a trivial text format:
/// <code>
/// # identifier: CH93 0000 1111      (optional; ignored by an Integration that can't provide identifiers)
/// # closing: 2026-11-30 1234.50     (optional: date and closing balance)
/// 2026-11-18;-12.50;CHF;Coffee shop                         date;amount;currency;description
/// 2026-11-19;-46.20;CHF;Trattoria;Trattoria Roma;R-1;-48.00;EUR   ...;counterparty;reference;original amount;original currency
/// </code>
/// Optional trailing fields may be empty or left out; blank lines are ignored; an empty currency means "the
/// Account's". Anything else is a format error naming the line. Build files with <see cref="TestStatement"/>.
public sealed class TestIntegration(string key, string name, bool canProvideIdentifier,
    bool holdsSeveralCurrencies = false) : IIntegration
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public bool CanProvideIdentifier { get; } = canProvideIdentifier;
    public bool HoldsSeveralCurrencies { get; } = holdsSeveralCurrencies;

    public ParsedStatement Read(SourceFileContent file)
    {
        var rows = new List<StatementRow>();
        string? identifier = null;
        ClosingBalance? closing = null;
        var lines = file.ReadText().Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var at = $"line {i + 1}";
            if (line.Length == 0) continue;
            if (line.StartsWith("# identifier:", StringComparison.Ordinal))
            {
                if (CanProvideIdentifier) identifier = line["# identifier:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("# closing:", StringComparison.Ordinal))
            {
                var parts = line["# closing:".Length..].Trim().Split(' ');
                if (parts.Length != 2) throw new StatementFormatException($"{at}: expected '# closing: <date> <amount>'.");
                closing = new ClosingBalance(ParseDate(parts[0], at), ParseAmount(parts[1], at));
                continue;
            }

            var f = line.Split(';');
            if (f.Length is < 4 or > 8) throw new StatementFormatException($"{at}: expected 4 to 8 ';'-separated fields.");
            string? Opt(int index) => index < f.Length && f[index].Trim().Length > 0 ? f[index].Trim() : null;
            rows.Add(new StatementRow(
                ParseDate(f[0], at),
                ParseAmount(f[1], at),
                Opt(2),
                f[3].Trim(),
                Opt(4),
                Opt(5),
                Opt(6) is { } original ? ParseAmount(original, at) : null,
                Opt(7)));
        }
        return new ParsedStatement(rows, identifier, closing);
    }

    private static DateOnly ParseDate(string value, string at) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new StatementFormatException($"{at}: '{value.Trim()}' is not a date (yyyy-MM-dd).");

    private static decimal ParseAmount(string value, string at) =>
        decimal.TryParse(value.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var a)
            ? a
            : throw new StatementFormatException($"{at}: '{value.Trim()}' is not an amount.");
}

public static class TestIntegrations
{
    /// Its format carries an Account identifier, so Accounts using it may have one.
    public static readonly TestIntegration WithIdentifier = new("test", "Test bank", canProvideIdentifier: true);

    /// Its format has no identifier.
    public static readonly TestIntegration WithoutIdentifier = new("test-noid", "Test bank without identifier", canProvideIdentifier: false);

    /// Holds several currencies, so it is used only through a Multi-currency account (ADR-0009).
    public static readonly TestIntegration MultiCurrency = new("test-multi", "Test multi-currency bank",
        canProvideIdentifier: false, holdsSeveralCurrencies: true);

    public static IReadOnlyList<TestIntegration> All => [WithIdentifier, WithoutIdentifier, MultiCurrency];
}

/// Builds Source file content in the test Integrations' format.
public sealed class TestStatement
{
    private readonly List<string> _lines = [];

    public TestStatement Identifier(string identifier) { _lines.Add($"# identifier: {identifier}"); return this; }

    public TestStatement Closing(string date, decimal amount) =>
        Line($"# closing: {date} {amount.ToString(CultureInfo.InvariantCulture)}");

    public TestStatement Row(string date, decimal amount, string description, string? currency = "CHF",
        string? counterparty = null, string? reference = null, decimal? originalAmount = null,
        string? originalCurrency = null) =>
        Line(string.Join(';', date, amount.ToString(CultureInfo.InvariantCulture), currency, description, counterparty,
            reference, originalAmount?.ToString(CultureInfo.InvariantCulture), originalCurrency));

    public TestStatement Line(string line) { _lines.Add(line); return this; }

    public override string ToString() => string.Join('\n', _lines) + "\n";
}
