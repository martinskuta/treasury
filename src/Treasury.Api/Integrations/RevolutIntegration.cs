using System.Globalization;

namespace Treasury.Api.Integrations;

/// "Revolut" (key revolut): Revolut's Custom statement PDF, exported in English (ADR-0010). One PDF covers every
/// currency account of a Revolut login for a period.
/// - read: the period, and per "Personal Account (CUR)" its IBAN, opening and closing balance and its table of rows
/// - a row's currency is its account's; every row is imported; Money in/out includes the row's fee, and a
///   non-zero fee is taken out of it into a second row
/// - a fee (that second row, or a row Revolut's Category calls Fees) suggests the Tag "fee"
/// - reading a PDF is fragile, so the file is rejected unless the rows agree with every figure the statement
///   gives: each row's running balance, each currency's closing balance and its Total row (Money in/out and Fees)
/// - the IBAN is the Account identifier; the balances of every Personal Account, also of one holding nothing
/// - a current account that isn't a Personal Account (e.g. a joint account) is not read; a note names it
/// - other sections (investments, savings, crypto) are not read; when one has entries, a note says so
public sealed class RevolutIntegration : IIntegration
{
    public string Key => "revolut";

    public string Name => "Revolut";

    /// The Custom statement shows the IBAN of the Revolut login.
    public bool CanProvideIdentifier => true;

    /// One Revolut login holds several currencies (GLOSSARY: Multi-currency account).
    public bool HoldsSeveralCurrencies => true;

    public ParsedStatement Read(SourceFileContent file)
    {
        var pdf = RevolutCustomStatementPdf.Read(file.Bytes);

        var listedTwice = pdf.Summaries.Select(s => s.Currency).GroupBy(c => c)
            .Concat(pdf.Tables.Select(t => t.Currency).GroupBy(c => c))
            .FirstOrDefault(g => g.Count() > 1);
        if (listedTwice is not null) throw Rejected($"The statement lists the {listedTwice.Key} account twice.");
        foreach (var summary in pdf.Summaries.Where(s => pdf.Tables.All(t => t.Currency != s.Currency)))
            throw Rejected($"The {summary.Currency} account is in the statement's summaries, but it has no transaction statement.");
        foreach (var table in pdf.Tables.Where(t => pdf.Summaries.All(s => s.Currency != t.Currency)))
            throw Rejected($"The {table.Currency} account has a transaction statement, but it is not in the statement's summaries.");

        var ibans = pdf.Summaries.Select(s => s.Iban).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ibans.Count > 1)
            throw Rejected(
                $"The statement's accounts show more than one IBAN ({string.Join(", ", ibans)}), so Treasury can't " +
                "tell which Revolut login it belongs to.");

        var rows = new List<StatementRow>();
        var balances = new List<CurrencyBalances>();
        foreach (var summary in pdf.Summaries)
        {
            var table = pdf.Tables.Single(t => t.Currency == summary.Currency);
            Reconcile(summary, table);
            rows.AddRange(table.Rows.SelectMany(row => Rows(summary.Currency, row)));
            // Also of a currency holding nothing: its Account, when it has one, is checked against the zero.
            balances.Add(new CurrencyBalances(summary.Currency, pdf.PeriodStart.AddDays(-1), summary.Opening,
                new ClosingBalance(pdf.PeriodEnd, summary.Closing)));
        }

        return new ParsedStatement(rows, ibans.SingleOrDefault(),
            BalancesByCurrency: balances.OrderBy(b => b.Currency, StringComparer.Ordinal).ToList(),
            Notes: pdf.SkippedAccounts
                .Select(account => $"The statement also has \"{account}\", which Treasury doesn't import.")
                .Concat(pdf.SkippedSectionsWithEntries
                    .Select(section => $"The statement has entries under \"{section}\", which Treasury doesn't read."))
                .ToList());
    }

    /// Rejects the file unless the rows that were read agree with the figures the statement gives for the currency.
    private static void Reconcile(RevolutAccountSummary summary, RevolutAccountTable table)
    {
        var currency = summary.Currency;
        string WithCurrency(decimal amount) => $"{Amounts.Format(amount)} {currency}";

        foreach (var (column, amount, where) in table.Rows
                     .SelectMany(r => new[]
                     {
                         ("Tax withheld", r.Tax, $"the row of {Day(r.Date)}"),
                         ("Other taxes", r.OtherTaxes, $"the row of {Day(r.Date)}"),
                     })
                     .Append(("Tax withheld", table.Total.Tax, "the Total row"))
                     .Append(("Other taxes", table.Total.OtherTaxes, "the Total row")))
            if (amount != 0)
                throw Rejected(
                    $"The {currency} account has {WithCurrency(amount)} under \"{column}\" in {where}, which Treasury " +
                    "doesn't support yet.");

        var balance = summary.Opening;
        foreach (var row in table.Rows)
        {
            // Revolut's balance is the previous balance + Money in/out; the Fees column is the part of Money in/out
            // that is the fee, not a charge on top of it.
            var expected = balance + row.Money;
            if (row.Balance != expected)
                throw Rejected(
                    $"The {currency} row \"{row.Description}\" of {Day(row.Date)} " +
                    $"doesn't follow from the balance before it: {WithCurrency(balance)} with {WithCurrency(row.Money)} in/out " +
                    $"gives {WithCurrency(expected)}, but the statement says {WithCurrency(row.Balance)}.");
            balance = row.Balance;
        }

        if (balance != summary.Closing)
            throw Rejected(table.Rows.Count == 0
                ? $"The {currency} account has no rows, but its opening balance is {WithCurrency(summary.Opening)} and its " +
                  $"closing balance is {WithCurrency(summary.Closing)}."
                : $"The {currency} rows end at a balance of {WithCurrency(balance)}, but the statement's closing balance is " +
                  $"{WithCurrency(summary.Closing)}.");

        var sum = table.Rows.Sum(r => r.Money);
        if (sum != table.Total.Money)
            throw Rejected(
                $"The {currency} rows add up to {WithCurrency(sum)}, but the statement's Total row says {WithCurrency(table.Total.Money)}.");

        var fees = table.Rows.Sum(r => r.Fees);
        if (fees != table.Total.Fees)
            throw Rejected(
                $"The {currency} rows' fees add up to {WithCurrency(fees)}, but the statement's Total row says " +
                $"{WithCurrency(table.Total.Fees)}.");
    }

    /// The statement and what Treasury read from it disagree: importing could store wrong numbers.
    private static StatementFormatException Rejected(string what) => new(
        $"{what} Treasury may have misread the PDF, or the statement is not one it can import. Nothing was imported.");

    /// The movement itself without its fee and, for a non-zero fee, a second row charged by Revolut. Together they
    /// are the row's Money in/out, which includes the fee.
    private static IEnumerable<StatementRow> Rows(string currency, RevolutTableRow row)
    {
        yield return new StatementRow(row.Date, row.Money + row.Fees, currency, row.Description, Counterparty(row))
        {
            SuggestedTags = row.Category == "Fees" ? FeeTags : [],
        };
        if (row.Fees != 0)
            yield return new StatementRow(row.Date, -row.Fees, currency, $"Fee: {row.Description}", "Revolut")
            {
                SuggestedTags = FeeTags,
            };
    }

    /// The Tag suggested for a fee Revolut charges.
    private static readonly IReadOnlyList<string> FeeTags = ["fee"];

    /// A card payment's merchant, Revolut for its own charges, or the person behind "To X" / "From X".
    private static string? Counterparty(RevolutTableRow row) =>
        row.Category == "Merchant" ? row.Description
        : row.Category == "Fees" ? "Revolut"
        : row.Description.StartsWith("To ", StringComparison.Ordinal) ? row.Description["To ".Length..]
        : row.Description.StartsWith("From ", StringComparison.Ordinal) ? row.Description["From ".Length..]
        : null;

    private static string Day(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
