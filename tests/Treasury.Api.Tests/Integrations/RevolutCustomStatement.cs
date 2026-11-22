using System.Globalization;
using System.Text;

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
    public RevolutCustomStatement Account(string currency, string opening, string closing,
        Action<AccountBuilder>? table = null, string? iban = Iban, string? zero = null)
    {
        var account = new AccountBuilder(currency, opening, closing, iban, zero ?? $"0.00 {currency}");
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
            Put(page, Left, y, 9.6, $"Personal Account ({account.Currency})");
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
            Put(page, Left, y, 9.6, $"Personal Account ({account.Currency})");
            Put(page, Left, y - 36, 9.1, "Transaction statement");
            y = TableHeader(page, y - 60, account);
            var converted = account.Currency != "CHF";
            foreach (var item in account.Items)
            {
                if (item is PageBreak pageBreak)
                {
                    page = NewPage();
                    y = 688;
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

    public sealed class AccountBuilder(string currency, string opening, string closing, string? iban, string zero)
    {
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

        /// Continues the table on a new page, with or without the table header printed again.
        public AccountBuilder NextPage(bool repeatHeader = true) { _rows.Add(new PageBreak(repeatHeader)); return this; }

        /// Prints "Account Number GB" above "Account Number (LT IBAN)", as Revolut does for the GBP account.
        public AccountBuilder GbAccountNumber(string number) { GbNumber = number; return this; }

        /// Leaves the account out of "Current Accounts Transaction Statements".
        public AccountBuilder OnlyInSummaries() { SummaryOnly = true; return this; }

        /// Leaves the account out of "Current Accounts Summaries".
        public AccountBuilder OnlyInTransactionStatements() { TransactionsOnly = true; return this; }

        internal IEnumerable<object> Items =>
            _noTotal ? _rows : [.. _rows, _total ?? new TableRow("Total", "", "", zero, "", null, null, null)];
    }

    private sealed record PageBreak(bool RepeatHeader);

    private sealed record TableRow(string Date, string Description, string Category, string Money, string Balance,
        string? Tax, string? OtherTaxes, string? Fees)
    {
        public string[] Cells(AccountBuilder account) =>
            [Date, Description, Category, Money, Balance, Tax ?? account.Zero, OtherTaxes ?? account.Zero, Fees ?? account.Zero];
    }

    private sealed record Text(double X, double Y, double Size, string Value);

    /// Just enough of a PDF writer: A4 pages of text in Helvetica (WinAnsi encoding, so "€", "£", "¥" and
    /// accented Latin letters work).
    private static class Pdf
    {
        public static byte[] Write(List<List<Text>> pages)
        {
            var objects = new List<byte[]>
            {
                Ascii("<< /Type /Catalog /Pages 2 0 R >>"),
                Ascii($"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + 2 * i} 0 R"))}] >>"),
                Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
            };
            for (var i = 0; i < pages.Count; i++)
            {
                objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.88] " +
                                  $"/Resources << /Font << /F1 3 0 R >> >> /Contents {5 + 2 * i} 0 R >>"));
                var content = new MemoryStream();
                foreach (var text in pages[i])
                {
                    content.Write(Ascii(string.Create(CultureInfo.InvariantCulture,
                        $"BT /F1 {text.Size} Tf {text.X:0.##} {text.Y:0.##} Td (")));
                    foreach (var c in text.Value)
                    {
                        if (c is '(' or ')' or '\\') content.WriteByte((byte)'\\');
                        content.WriteByte(WinAnsi(c));
                    }
                    content.Write(Ascii(") Tj ET\n"));
                }
                objects.Add([.. Ascii($"<< /Length {content.Length} >>\nstream\n"), .. content.ToArray(), .. Ascii("\nendstream")]);
            }

            var pdf = new MemoryStream();
            pdf.Write(Ascii("%PDF-1.4\n"));
            var offsets = new List<long>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(pdf.Position);
                pdf.Write(Ascii($"{i + 1} 0 obj\n"));
                pdf.Write(objects[i]);
                pdf.Write(Ascii("\nendobj\n"));
            }
            var xref = pdf.Position;
            pdf.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
            foreach (var offset in offsets) pdf.Write(Ascii($"{offset:0000000000} 00000 n \n"));
            pdf.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
            return pdf.ToArray();
        }

        /// Roughly the width of the text in Helvetica: good enough to right-align a cell.
        public static double Width(string text, double size) => text.Sum(c => c switch
        {
            ' ' or '.' or ',' or '/' or 'i' or 'l' or 'j' or 't' or 'f' or 'I' or '\'' => 0.278,
            '-' or '(' or ')' or 'r' => 0.333,
            >= 'A' and <= 'Z' => 0.69,
            _ => 0.556,
        }) * size;

        private static byte WinAnsi(char c) => c switch
        {
            '€' => 0x80,
            < (char)0x80 or (>= (char)0xA0 and <= (char)0xFF) => (byte)c,
            _ => throw new ArgumentException($"'{c}' can't be written to the synthetic statement."),
        };

        private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    }
}
