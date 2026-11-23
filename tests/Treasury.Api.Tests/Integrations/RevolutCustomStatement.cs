using System.Globalization;
using Pdf = Treasury.Api.Tests.Integrations.SyntheticPdf;
using Text = Treasury.Api.Tests.Integrations.PdfText;

namespace Treasury.Api.Tests.Integrations;

/// Builds a synthetic Revolut Custom statement PDF, laid out like the real one (same sections, headings, column
/// positions, font sizes, wrapped cells and the small converted CHF amount beneath every amount of a non-CHF
/// account), so each test states the statement it needs. Everything in it is invented.
///
/// Amounts are given as the text the statement prints ("-$4.20", "1,500 ISK"). A cell wider than its column
/// wraps between words, as in the real statement; a "\n" in a cell wraps it there ("-1,250.00\nCHF").
public sealed class RevolutCustomStatement(string period = "Nov 1, 2026 - Nov 30, 2026")
{
    /// The IBAN every account shows unless a test says otherwise (the public example IBAN for Lithuania).
    public const string Iban = "LT121000011101001000";

    /// What the statement prints beneath every amount of a non-CHF account; no test expects to read it.
    private const string Converted = "77.77 CHF";

    private const double Left = 40, Right = 556, Middle = 301, Body = 8.3, Small = 4.5, Heading = 12.4;

    private readonly List<AccountBuilder> _accounts = [];
    private readonly List<(string Heading, string[] Lines)> _sections = [];
    private readonly List<List<Text>> _pages = [];

    /// A "Personal Account (CUR)": its page in "Current Accounts Summaries" and its table in "Current Accounts
    /// Transaction Statements".
    /// <param name="iban">null prints "Account Number N/A", as Revolut does for some currencies.</param>
    /// <param name="zero">How this account prints zero, e.g. "$0.00"; "0.00 CUR" when left out.</param>
    /// <param name="kind">The heading is "&lt;kind&gt; (CUR)": another kind is an account Treasury doesn't import,
    /// e.g. "Joint Account".</param>
    public RevolutCustomStatement Account(string currency, string opening, string closing,
        Action<AccountBuilder>? table = null, string? iban = Iban, string? zero = null, string kind = "Personal Account")
    {
        var account = new AccountBuilder($"{kind} ({currency})", currency, opening, closing, iban, zero ?? $"0.00 {currency}");
        table?.Invoke(account);
        _accounts.Add(account);
        return this;
    }

    /// Lines printed under the heading of a section Treasury doesn't read, e.g. "Investment Services Summaries"
    /// (which is in every statement, empty unless given lines here).
    public RevolutCustomStatement Section(string heading, params string[] lines)
    {
        _sections.Add((heading, lines));
        return this;
    }

    /// A one-page PDF with these lines of text: a PDF that isn't a Custom statement.
    public static byte[] OtherPdf(params string[] lines) =>
        Pdf.Write([lines.Select((line, i) => new Text(Left, 757 - 19 * i, Body, line)).ToList()]);

    public byte[] Build()
    {
        _pages.Clear();
        Summaries();
        Information();
        Skipped("Investment Services Summaries");
        Transactions();
        Information();
        Skipped("Investment Services Transaction Statements");
        foreach (var heading in _sections.Select(s => s.Heading).Distinct().Where(h => !h.StartsWith("Investment Services ")))
            Skipped(heading);
        return Pdf.Write(_pages);
    }

    private void Summaries()
    {
        var first = true;
        foreach (var account in _accounts.Where(a => !a.TransactionsOnly))
        {
            var page = NewPage();
            var y = 688.0;
            if (first)
            {
                Put(page, Left, 689, Heading, "ANNA EXAMPLE");
                Put(page, Left, 666, Body, "Musterstrasse 1");
                Put(page, 383, 665, Body, "Tax residency: Switzerland");
                Put(page, Left, 653, Body, "8000");
                Put(page, 383, 651, Body, "Tax ID: 7560000000002");
                Put(page, Left, 641, Body, "Zurich");
                Put(page, 383, 637, Body, "Date of birth: Jan 1, 1990");
                Put(page, Left, 587, Heading, "Current Accounts Summaries");
                y = 549;
                first = false;
            }
            Put(page, Left, y, 9.6, account.Heading);
            Put(page, Left, y - 36, 9.1, "Current account details");
            y -= 54;
            if (account.GbNumber is { } gb)
            {
                Put(page, Left, y, Body, "Account Number GB");
                PutRight(page, 294, y, Body, gb);
                Put(page, Middle, y, Body, "Opening date");
                PutRight(page, Right, y, Body, "Mar 1, 2020");
                y -= 19;
                Put(page, Left, y, Body, "Account Number (LT IBAN)");
                PutRight(page, 294, y, Body, account.Iban ?? "N/A");
                Put(page, Middle, y, Body, "Closing date");
                y -= 19;
            }
            else
            {
                Put(page, Left, y, Body, account.Iban is null ? "Account Number" : "Account Number (IBAN)");
                PutRight(page, 294, y, Body, account.Iban ?? "N/A");
                Put(page, Middle, y, Body, "Opening date");
                PutRight(page, Right, y, Body, "Mar 1, 2020");
                y -= 19;
                Put(page, Middle, y, Body, "Closing date");
            }
            Put(page, Left, y, Body, "Holding modalities");
            PutRight(page, 294, y, Body, "Individual");
            y -= 19;
            Put(page, Left, y, Body, "Purpose of account");
            PutRight(page, 294, y, Body, "Personal");
            Put(page, Middle, y, Body, "Days owned (in selected period)");
            PutRight(page, Right, y, Body, "30");
            y -= 19;
            Put(page, Left, y, Body, "Nature of account");
            PutRight(page, 294, y, Body, "Current");
            y -= 39;
            Put(page, Left, y, 9.1, "Financial institution information");
            y -= 18;
            Put(page, Left, y, Body, "Financial institution name");
            PutRight(page, 294, y, Body, "Revolut Bank UAB");
            Put(page, Middle, y, Body, "Registration number");
            PutRight(page, Right, y, Body, "LT100000000000");
            y -= 19;
            Put(page, Left, y, Body, "Address & country");
            PutRight(page, 294, y, Body, "Example ave. 1, LT-00000,");
            PutRight(page, 294, y - 12, Body, "Vilnius, Lithuania");
            y -= 52;
            Put(page, Left, y, 9.1, "Deposit value");
            y -= 18;
            var converted = account.Currency != "CHF";
            foreach (var (label, amount) in new[]
                     {
                         ("Opening balance", account.Opening), ("Closing balance", account.Closing),
                         ("Maximum balance", account.Closing), ("Average balance full period", account.Closing),
                     })
            {
                Put(page, Middle, y, Body, label);
                PutRight(page, Right, y, Body, amount);
                if (converted) PutRight(page, Right, y - 8, Small, Converted);
                y -= converted ? 24 : 19;
            }
        }
    }

    private void Information()
    {
        var page = NewPage();
        Put(page, Left, 689, Heading, "Information about Current Accounts statement");
        Put(page, Left, 662, Body, "Revolut Bank UAB is a bank licensed in the Republic of Lithuania with company number 100000000.");
        Put(page, Left, 650, Body, "Deposits are protected by the Lithuanian Deposit Guarantee Scheme, but some exceptions may apply.");
        Put(page, Left, 638, Body, "Total 1,000.00 CHF is an example and Nov 1, 2026 is a date in a sentence.");
    }

    private void Skipped(string heading)
    {
        var page = NewPage();
        Put(page, Left, 689, Heading, heading);
        var y = 650.0;
        foreach (var line in _sections.Where(s => s.Heading == heading).SelectMany(s => s.Lines))
        {
            Put(page, Left, y, Body, line);
            y -= 19;
        }
    }

    private void Transactions()
    {
        var first = true;
        foreach (var account in _accounts.Where(a => !a.SummaryOnly))
        {
            var page = NewPage();
            var y = 688.0;
            if (first)
            {
                Put(page, Left, 689, Heading, "Current Accounts Transaction Statements");
                y = 650;
                first = false;
            }
            Put(page, Left, y, 9.6, account.Heading);
            Put(page, Left, y - 36, 9.1, "Transaction statement");
            y = TableHeader(page, y - 60, account);
            var converted = account.Currency != "CHF";
            foreach (var item in account.Items)
            {
                if (item is PageBreak pageBreak)
                {
                    page = NewPage();
                    y = 688;
                    if (pageBreak.RepeatAccountHeading)
                    {
                        Put(page, Left, y, 9.6, account.Heading);
                        y -= 24;
                    }
                    if (pageBreak.RepeatHeader) y = TableHeader(page, y - 36, account);
                    continue;
                }
                var cells = ((TableRow)item).Cells(account).Select((cell, column) => Wrap(cell, column, account)).ToArray();
                for (var column = 0; column < cells.Length; column++)
                {
                    var lines = cells[column];
                    for (var i = 0; i < lines.Length; i++)
                        // Fees is right-aligned; every other column is left-aligned.
                        if (column == 7) PutRight(page, 554, y - 12.4 * i, Body, lines[i]);
                        else Put(page, account.Columns[column], y - 12.4 * i, Body, lines[i]);
                    if (converted && column >= 3 && lines.Length > 0)
                    {
                        if (column == 7) PutRight(page, 554, y - 8, Small, Converted);
                        else Put(page, account.Columns[column], y - 8, Small, Converted);
                    }
                }
                var height = cells.Max(c => c.Length);
                y -= 12.4 * height + (converted ? 11.6 : 6.3);
            }
        }
    }

    /// The cell's lines: a cell wider than its column wraps between words, as in the real statement, which
    /// prints a balance of "12,345.60 CHF" as "12,345.60" above "CHF".
    private static string[] Wrap(string cell, int column, AccountBuilder account)
    {
        var width = column switch { 0 => 80, 7 => 42, 6 => 40, _ => account.Columns[column + 1] - account.Columns[column] - 3 };
        var lines = new List<string>();
        foreach (var given in cell.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = "";
            foreach (var word in given.Split(' '))
                if (line.Length > 0 && Pdf.Width($"{line} {word}", Body) > width)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = line.Length > 0 ? $"{line} {word}" : word;
            lines.Add(line);
        }
        return [.. lines];
    }

    /// The three-line table header; returns where the first row goes.
    private static double TableHeader(List<Text> page, double y, AccountBuilder account)
    {
        var c = account.Columns;
        Put(page, c[3], y + 6, Body, "Money in");
        Put(page, c[5], y + 6, Body, "Tax");
        Put(page, c[6], y + 6, Body, "Other");
        Put(page, c[0], y, Body, "Date");
        Put(page, c[1], y, Body, "Description");
        Put(page, c[2], y, Body, "Category");
        Put(page, c[4], y, Body, "Balance");
        PutRight(page, 554, y, Body, "Fees");
        Put(page, c[3], y - 6, Body, "/out");
        Put(page, c[5], y - 6, Body, "withheld");
        Put(page, c[6], y - 6, Body, "taxes");
        return y - 25;
    }

    private List<Text> NewPage()
    {
        var page = new List<Text>();
        PutRight(page, Right, 757, 20.6, "Custom Statement");
        PutRight(page, Right, 744, Body, period);
        PutRight(page, Right, 731, Body, "Generated on the Dec 1, 2026");
        _pages.Add(page);
        return page;
    }

    private static void Put(List<Text> page, double x, double y, double size, string text)
    {
        if (text.Length > 0) page.Add(new Text(x, y, size, text));
    }

    private static void PutRight(List<Text> page, double right, double y, double size, string text) =>
        Put(page, right - Pdf.Width(text, size), y, size, text);

    public sealed class AccountBuilder(string heading, string currency, string opening, string closing, string? iban,
        string zero)
    {
        internal string Heading => heading;
        internal string Currency => currency;
        internal string Opening => opening;
        internal string Closing => closing;
        internal string? Iban => iban;
        internal string Zero => zero;
        internal string? GbNumber { get; private set; }
        internal bool SummaryOnly { get; private set; }
        internal bool TransactionsOnly { get; private set; }
        private readonly List<object> _rows = [];
        private TableRow? _total;
        private bool _noTotal;

        /// Left edges of Date, Description, Category, Money in/out, Balance, Tax withheld, Other taxes (they
        /// differ by a point between the CHF table and the others in the real statement).
        internal double[] Columns => currency == "CHF" ? [40, 125, 257, 342, 386, 428, 471] : [40, 125, 258, 343, 385, 428, 470];

        /// A table row; tax, other taxes and fees print the account's zero unless given.
        public AccountBuilder Row(string date, string description, string category, string money, string balance,
            string? tax = null, string? otherTaxes = null, string? fees = null)
        {
            _rows.Add(new TableRow(date, description, category, money, balance, tax, otherTaxes, fees));
            return this;
        }

        /// The Total row (it has no Balance). An account that is never given one prints a Total of zero.
        public AccountBuilder Total(string money, string? tax = null, string? otherTaxes = null, string? fees = null)
        {
            _total = new TableRow("Total", "", "", money, "", tax, otherTaxes, fees);
            return this;
        }

        /// Leaves the Total row out.
        public AccountBuilder NoTotal() { _noTotal = true; return this; }

        /// Continues the table on a new page, with or without the table header printed again, and with or
        /// without the account's heading printed again above it.
        public AccountBuilder NextPage(bool repeatHeader = true, bool repeatAccountHeading = false)
        {
            _rows.Add(new PageBreak(repeatHeader, repeatAccountHeading));
            return this;
        }

        /// The rest of the description of the row above, as a line of its own: after <see cref="NextPage"/>, a
        /// description that wraps over the page break.
        public AccountBuilder DescriptionContinued(string description)
        {
            _rows.Add(new TableRow("", description, "", "", "", "", "", ""));
            return this;
        }

        /// Prints "Account Number GB" above "Account Number (LT IBAN)", as Revolut does for the GBP account.
        public AccountBuilder GbAccountNumber(string number) { GbNumber = number; return this; }

        /// Leaves the account out of "Current Accounts Transaction Statements".
        public AccountBuilder OnlyInSummaries() { SummaryOnly = true; return this; }

        /// Leaves the account out of "Current Accounts Summaries".
        public AccountBuilder OnlyInTransactionStatements() { TransactionsOnly = true; return this; }

        internal IEnumerable<object> Items =>
            _noTotal ? _rows : [.. _rows, _total ?? new TableRow("Total", "", "", zero, "", null, null, null)];
    }

    private sealed record PageBreak(bool RepeatHeader, bool RepeatAccountHeading);

    private sealed record TableRow(string Date, string Description, string Category, string Money, string Balance,
        string? Tax, string? OtherTaxes, string? Fees)
    {
        public string[] Cells(AccountBuilder account) =>
            [Date, Description, Category, Money, Balance, Tax ?? account.Zero, OtherTaxes ?? account.Zero, Fees ?? account.Zero];
    }
}
