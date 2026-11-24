namespace Treasury.Api.Tests.Integrations;

/// Builds a synthetic PostFinance Kontoauszug PDF, laid out like the real one (the first page's heading, the
/// table header on every page, the columns' positions, right-aligned amounts, text blocks of several lines, a
/// date on a day's first row only, a page of closing text), so each test states the statement it needs.
/// Everything in it is invented.
///
/// Dates and amounts are given as the text the statement prints ("01.10.26", "1 000.00").
public sealed class PostFinanceKontoauszug(string from = "01.10.2026", string to = "31.10.2026",
    string title = "Kontoauszug", string currency = "CHF", string iban = PostFinanceKontoauszug.Iban)
{
    /// The IBAN the statement shows unless a test says otherwise (the public example IBAN for Switzerland).
    public const string Iban = "CH9300762011623852957";

    private const double DateLeft = 65.2, TextLeft = 127.6, CreditRight = 354.3, DebitRight = 433.7,
        ValutaRight = 487.5, SaldoRight = 564.1, Body = 10, Small = 7.5;

    private readonly List<object> _items = [];
    private string _day = "";

    /// The "Kontostand" line above the first row.
    public PostFinanceKontoauszug Opening(string date, string saldo) => Raw(date, "Kontostand", saldo: saldo);

    /// A row of money out. <paramref name="date"/> is null on a row that isn't the first of its day, and
    /// <paramref name="saldo"/> is null on one that isn't the last.
    public PostFinanceKontoauszug Debit(string? date, string amount, string? saldo, params string[] text) =>
        Row(date, "", amount, saldo, text);

    /// A row of money in; see <see cref="Debit"/>.
    public PostFinanceKontoauszug Credit(string? date, string amount, string? saldo, params string[] text) =>
        Row(date, amount, "", saldo, text);

    private PostFinanceKontoauszug Row(string? date, string credit, string debit, string? saldo, string[] text)
    {
        _day = date ?? _day;
        Raw(date ?? "", text[0], credit, debit, _day, saldo ?? "");
        return Text(text[1..]);
    }

    /// Further lines of the text of the row above: after <see cref="NextPage"/>, a text block that runs over the
    /// page break.
    public PostFinanceKontoauszug Text(params string[] lines)
    {
        foreach (var line in lines) _items.Add(new Line("", line, "", "", "", "", Continues: true));
        return this;
    }

    /// The "Total" line below the last row.
    public PostFinanceKontoauszug Total(string credit, string debit) => Raw(text: "Total", credit: credit, debit: debit);

    /// The "Kontostand" line below the Total line.
    public PostFinanceKontoauszug Closing(string date, string saldo) => Raw(date, "Kontostand", saldo: saldo);

    /// A table line with exactly these cells.
    public PostFinanceKontoauszug Raw(string date = "", string text = "", string credit = "", string debit = "",
        string valuta = "", string saldo = "")
    {
        _items.Add(new Line(date, text, credit, debit, valuta, saldo, Continues: false));
        return this;
    }

    /// Continues the table on a new page.
    public PostFinanceKontoauszug NextPage()
    {
        _items.Add(new PageBreak());
        return this;
    }

    /// A one-page PDF with these lines of text: a PDF that isn't a Kontoauszug.
    public static byte[] OtherPdf(params string[] lines) =>
        SyntheticPdf.Write([lines.Select((line, i) => new PdfText(TextLeft, 757 - 19 * i, Body, line)).ToList()]);

    public byte[] Build()
    {
        var pages = new List<List<PdfText>>();
        var page = FirstPage();
        pages.Add(page);
        var y = 379.8;
        var first = true;
        foreach (var item in _items)
        {
            if (item is PageBreak)
            {
                pages.Add(page = LaterPage());
                TableHeader(page, 735);
                y = 720;
                first = true;
                continue;
            }
            var line = (Line)item;
            // A row's lines are 12 apart; rows are a little further apart.
            if (!first) y -= line.Continues ? 12 : 16.3;
            first = false;
            Put(page, DateLeft, y, Body, line.Date);
            Put(page, TextLeft, y, Body, line.Text);
            PutRight(page, CreditRight, y, Body, line.Credit);
            PutRight(page, DebitRight, y, Body, line.Debit);
            PutRight(page, ValutaRight, y, Body, line.Valuta);
            PutRight(page, SaldoRight, y, Body, line.Saldo);
        }

        // The last page has the closing text and no table.
        pages.Add(page = LaterPage());
        Put(page, TextLeft, 735.3, 9.5, "Bitte überprüfen Sie den Kontoauszug. Ohne Ihren Gegenbericht innert 30 Tagen gilt er als genehmigt.");
        Put(page, TextLeft, 701.6, 9.5, "Freundliche Grüsse");
        Put(page, TextLeft, 679.6, 9.5, "PostFinance AG");

        for (var i = 0; i < pages.Count; i++) PutRight(pages[i], 561.5, 25.5, 9.5, $"Seite {i + 1} / {pages.Count}");
        return SyntheticPdf.Write(pages);
    }

    private List<PdfText> FirstPage()
    {
        var page = new List<PdfText>();
        Put(page, 62.5, 751.3, 7, "PostFinance AG");
        Put(page, 283.5, 653.8, 9.5, "Frau");
        Put(page, 283.5, 642.8, 9.5, "Erika Muster");
        Put(page, 283.5, 631.8, 9.5, "Musterstrasse 1");
        Put(page, 283.5, 620.8, 9.5, "8000 Zürich");
        Put(page, TextLeft, 518.7, 15, "Privatkonto");
        Put(page, TextLeft, 504.6, Body, $"{title} {from} - {to}");
        Heading(page, 471.6, "IBAN", $"{Grouped(iban)} {currency}");
        Heading(page, 458.9, "Kontonummer", "00-000000-0");
        Heading(page, 446.1, "BIC", "POFICHBEXXX");
        Heading(page, 433.3, "Datum", "01.11.2026");
        TableHeader(page, 394.8);
        return page;
    }

    private List<PdfText> LaterPage()
    {
        var page = new List<PdfText>();
        Heading(page, 775.3, "Datum", "01.11.2026");
        Heading(page, 763.4, "IBAN", Grouped(iban));
        Heading(page, 751.4, "Kontonummer", "00-000000-0");
        return page;
    }

    private static void Heading(List<PdfText> page, double y, string label, string value)
    {
        PutRight(page, 118, y + 0.2, Small, label);
        Put(page, TextLeft, y, Body, value);
    }

    private static void TableHeader(List<PdfText> page, double y)
    {
        Put(page, DateLeft, y, Small, "Datum");
        Put(page, TextLeft, y, Small, "Text");
        PutRight(page, CreditRight, y, Small, "Gutschrift");
        PutRight(page, DebitRight, y, Small, "Lastschrift");
        PutRight(page, ValutaRight, y, Small, "Valuta");
        PutRight(page, SaldoRight, y, Small, "Saldo");
    }

    private static string Grouped(string iban) => string.Join(' ', iban.Chunk(4).Select(group => new string(group)));

    private static void Put(List<PdfText> page, double x, double y, double size, string text)
    {
        if (text.Length > 0) page.Add(new PdfText(x, y, size, text));
    }

    private static void PutRight(List<PdfText> page, double right, double y, double size, string text) =>
        Put(page, right - SyntheticPdf.ExactWidth(text, size), y, size, text);

    private sealed record PageBreak;

    private sealed record Line(string Date, string Text, string Credit, string Debit, string Valuta, string Saldo,
        bool Continues);
}
