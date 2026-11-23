using System.Globalization;

namespace Treasury.Api.Integrations;

/// "PostFinance" (key postfinance): PostFinance's Kontoauszug PDF, in German (ADR-0012). PostFinance offers no
/// camt.053 for a private account, so this is the only format read.
/// - read: the period, the IBAN and currency, the opening and closing Kontostand, the Total line and the rows
/// - a row is dated with its booking date (Datum, printed on a day's first row only); Valuta is ignored
/// - the description is the row's text block as printed, one line per printed line (wrapped words are not
///   rejoined); it is part of what makes a Transaction a duplicate, so it must stay stable
/// - no bank reference: the ids the statement prints exist on some kinds of row only and stay in the description
/// - the counterparty is the line after "ZAHLUNGSEMPFÄNGER:", "ABSENDER:" or "AUFTRAGGEBER:", the merchant line of
///   a TWINT purchase, PostFinance for its own prices, and empty otherwise
/// - a price PostFinance charges ("PREIS FÜR ...") suggests the Tag "fee"
/// - reading a PDF is fragile, so the file is rejected unless the rows agree with every figure the statement
///   gives: each printed Saldo (one per booking date), the closing Kontostand and the Total line
public sealed class PostFinanceIntegration : IIntegration
{
    public string Key => "postfinance";

    public string Name => "PostFinance";

    /// The Kontoauszug shows the account's IBAN.
    public bool CanProvideIdentifier => true;

    public bool HoldsSeveralCurrencies => false;

    public ParsedStatement Read(SourceFileContent file)
    {
        var pdf = PostFinanceKontoauszugPdf.Read(file.Bytes);
        Reconcile(pdf);
        var rows = pdf.Rows.Select(row => new StatementRow(row.Date, Signed(row), pdf.Currency,
            string.Join('\n', row.Text), Counterparty(row))
        {
            SuggestedTags = IsPrice(row) ? FeeTags : [],
        }).ToList();
        return new ParsedStatement(rows, pdf.Iban, new ClosingBalance(pdf.Closing.Date, pdf.Closing.Amount));
    }

    private static decimal Signed(PostFinanceRow row) => row.Credit ?? -row.Debit!.Value;

    /// Rejects the file unless the rows that were read agree with the figures the statement gives.
    private static void Reconcile(PostFinanceKontoauszugPdf pdf)
    {
        string WithCurrency(decimal amount) => $"{Amounts.Format(amount)} {pdf.Currency}";

        var balance = pdf.Opening.Amount;
        foreach (var row in pdf.Rows)
        {
            balance += Signed(row);
            // The Saldo is printed once per booking date, on the day's last row.
            if (row.Saldo is { } saldo && saldo != balance)
                throw Rejected(
                    $"The rows up to \"{row.Text[0]}\" of {Day(row.Date)} give a balance of {WithCurrency(balance)}, " +
                    $"but the statement says {WithCurrency(saldo)}.");
        }

        if (balance != pdf.Closing.Amount)
            throw Rejected(pdf.Rows.Count == 0
                ? $"The statement has no rows, but its opening Kontostand is {WithCurrency(pdf.Opening.Amount)} and its " +
                  $"closing Kontostand is {WithCurrency(pdf.Closing.Amount)}."
                : $"The rows end at a balance of {WithCurrency(balance)}, but the statement's closing Kontostand is " +
                  $"{WithCurrency(pdf.Closing.Amount)}.");

        if (pdf.Total is not { } total) return;
        var credit = pdf.Rows.Sum(r => r.Credit ?? 0);
        if (credit != total.Credit)
            throw Rejected(
                $"The Gutschrift amounts add up to {WithCurrency(credit)}, but the statement's Total line says " +
                $"{WithCurrency(total.Credit)}.");
        var debit = pdf.Rows.Sum(r => r.Debit ?? 0);
        if (debit != total.Debit)
            throw Rejected(
                $"The Lastschrift amounts add up to {WithCurrency(debit)}, but the statement's Total line says " +
                $"{WithCurrency(total.Debit)}.");
    }

    /// The statement and what Treasury read from it disagree: importing could store wrong numbers.
    private static StatementFormatException Rejected(string what) => new(
        $"{what} Treasury may have misread the PDF, or the statement is not one it can import. Nothing was imported.");

    /// The Tag suggested for a price PostFinance charges.
    private static readonly IReadOnlyList<string> FeeTags = ["fee"];

    private static readonly string[] CounterpartyLabels = ["ZAHLUNGSEMPFÄNGER:", "ABSENDER:", "AUFTRAGGEBER:"];

    private static bool IsPrice(PostFinanceRow row) => row.Text[0].StartsWith("PREIS FÜR", StringComparison.Ordinal);

    /// Only where the statement says who it is: the line after a label, or the merchant of a TWINT purchase
    /// (the line after "VOM dd.MM.yyyy"). A plain LASTSCHRIFT block is bank, bank address, IBAN, payee, and which
    /// line is the payee can't be told reliably.
    private static string? Counterparty(PostFinanceRow row)
    {
        if (IsPrice(row)) return "PostFinance";
        var text = row.Text;
        for (var i = 0; i < text.Count - 1; i++)
            if (CounterpartyLabels.Contains(text[i])) return text[i + 1];
        return text.Count > 2 && text[0].StartsWith("TWINT ", StringComparison.Ordinal)
            && text[1].StartsWith("VOM ", StringComparison.Ordinal)
            ? text[2]
            : null;
    }

    private static string Day(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
