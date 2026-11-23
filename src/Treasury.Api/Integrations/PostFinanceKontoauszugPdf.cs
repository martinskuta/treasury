using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Treasury.Api.Integrations;

/// What PostFinance's Kontoauszug PDF (German) says, as printed: nothing is checked against anything else here
/// (that is <see cref="PostFinanceIntegration"/>'s job).
/// <param name="PeriodStart">First day of the period in the document's title.</param>
/// <param name="PeriodEnd">Last day of the period.</param>
/// <param name="Iban">The account's IBAN, without spaces.</param>
/// <param name="Currency">The ISO code printed after the IBAN on the first page.</param>
/// <param name="Opening">The "Kontostand" line above the first row.</param>
/// <param name="Closing">The "Kontostand" line below the Total line.</param>
/// <param name="Total">The "Total" line; a statement without rows may have none.</param>
/// <param name="Rows">The table's rows, in document order.</param>
internal sealed record PostFinanceKontoauszugPdf(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Iban,
    string Currency,
    PostFinanceKontostand Opening,
    PostFinanceKontostand Closing,
    PostFinanceTotal? Total,
    IReadOnlyList<PostFinanceRow> Rows)
{
    public const string ExportHint =
        "Download the account statement (Kontoauszug) as PDF from PostFinance e-finance, in German, and import " +
        "that file.";

    /// The statement is read from word positions, not from its text: a row's text is a block of several lines
    /// beside its amounts, and the amounts are told apart only by the column they are right-aligned to.
    public static PostFinanceKontoauszugPdf Read(byte[] bytes)
    {
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8) < 0)
            throw new StatementFormatException(
                $"This isn't a PDF. Treasury reads PostFinance's Kontoauszug PDF, not camt.053 or CSV. {ExportHint}");

        List<List<Line>> pages;
        try
        {
            using var document = PdfDocument.Open(bytes);
            pages = Lines(document);
        }
        catch (Exception e) when (e is not StatementFormatException)
        {
            throw new StatementFormatException($"This PDF couldn't be opened ({e.Message}). {ExportHint}");
        }

        var first = pages.FirstOrDefault() ?? [];
        var period = first.Select(l => Period.Match(l.Text)).FirstOrDefault(m => m.Success);
        var iban = first.Select(l => IbanLine.Match(l.Text)).FirstOrDefault(m => m.Success);
        if (period is null || iban is null || !first.Any(IsTableHeader)
            || !TryDate(period.Groups["from"].Value, "dd.MM.yyyy", out var periodStart)
            || !TryDate(period.Groups["to"].Value, "dd.MM.yyyy", out var periodEnd))
            throw new StatementFormatException($"This PDF isn't a PostFinance Kontoauszug in German. {ExportHint}");

        var table = new Table();
        for (var i = 0; i < pages.Count && table.Closing is null; i++) table.ReadPage(pages[i], i + 1);

        return new PostFinanceKontoauszugPdf(periodStart, periodEnd, iban.Groups["iban"].Value.Replace(" ", ""),
            iban.Groups["currency"].Value,
            table.Opening ?? throw Unreadable("the opening Kontostand wasn't found"),
            table.Closing ?? throw Unreadable("the closing Kontostand wasn't found"),
            table.Total ?? (table.Rows.Count == 0 ? null : throw Unreadable("the table has no Total line")),
            table.Rows.Select(r => new PostFinanceRow(r.Date, r.Text, r.Credit, r.Debit, r.Saldo)).ToList());
    }

    /// The statement's layout wasn't what Treasury expects.
    private static StatementFormatException Unreadable(string what) => new(
        $"Treasury couldn't read this PostFinance Kontoauszug: {what}. PostFinance may have changed the statement's " +
        "layout. Nothing was imported.");

    private static readonly Regex Period = new(@"^Kontoauszug (?<from>\d{2}\.\d{2}\.\d{4}) - (?<to>\d{2}\.\d{2}\.\d{4})$");

    /// "IBAN CH93 0076 2011 6238 5295 7 CHF": the currency follows the IBAN (on the first page only).
    private static readonly Regex IbanLine = new(@"^IBAN (?<iban>[A-Z]{2}\d{2}(?: ?[A-Z0-9])+) (?<currency>[A-Z]{3})$");

    private static readonly Regex Footer = new(@"^Seite \d+ / \d+$");

    /// "5.00", "10 019.10", "-1 250.00": a space between thousands, a dot before the decimals.
    private static readonly Regex Amount = new(@"^(?<sign>-)?(?<number>\d{1,3}(?: \d{3})*\.\d{2})$");

    private static readonly string[] HeaderNames = ["Datum", "Text", "Gutschrift", "Lastschrift", "Valuta", "Saldo"];

    private static bool IsTableHeader(Line line) => HeaderNames.All(name => line.Words.Any(w => w.Text == name));

    private static bool TryDate(string text, string format, out DateOnly date) =>
        DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static decimal ParseAmount(string text, string where)
    {
        var match = Amount.Match(text);
        if (!match.Success) throw Unreadable($"'{text}' ({where}) is not an amount");
        var number = decimal.Parse(match.Groups["number"].Value.Replace(" ", ""), CultureInfo.InvariantCulture);
        return match.Groups["sign"].Success ? -number : number;
    }

    // ---- words to lines ----

    private sealed record Line(double Y, IReadOnlyList<Word> Words)
    {
        public string Text { get; } = string.Join(' ', Words.Select(w => w.Text));
    }

    /// Every page's words as lines, top to bottom, each line left to right. The print code that runs up the left
    /// margin is dropped (it is not horizontal).
    private static List<List<Line>> Lines(PdfDocument document) => document.GetPages().Select(page =>
    {
        var lines = new List<List<Word>>();
        foreach (var word in page.GetWords()
                     .Where(w => w.Letters.Count > 0 && w.TextOrientation == TextOrientation.Horizontal)
                     .OrderByDescending(Baseline))
            if (lines.Count > 0 && Baseline(lines[^1][0]) - Baseline(word) < 2) lines[^1].Add(word);
            else lines.Add([word]);
        return lines.Select(l => new Line(Baseline(l[0]), l.OrderBy(w => w.BoundingBox.Left).ToList())).ToList();
    }).ToList();

    private static double Baseline(Word word) => word.Letters[0].StartBaseLine.Y;

    // ---- lines to rows ----

    /// Walks the table's lines page by page. The table may run over several pages, each with its own header; a
    /// row's text block may continue on the next page.
    private sealed class Table
    {
        public PostFinanceKontostand? Opening { get; private set; }
        public PostFinanceKontostand? Closing { get; private set; }
        public PostFinanceTotal? Total { get; private set; }
        public List<RowInProgress> Rows { get; } = [];
        private DateOnly? _date;

        public void ReadPage(List<Line> lines, int page)
        {
            var header = lines.FirstOrDefault(IsTableHeader)
                ?? throw Unreadable($"page {page} has no table header, but the closing Kontostand wasn't reached yet");
            var columns = Columns.Of(header);
            // What is above the header is the page's own heading (date, IBAN, account number).
            foreach (var line in lines.Where(l => l.Y < header.Y))
            {
                if (Closing is not null) return; // whatever follows the closing Kontostand is not the table
                if (Footer.IsMatch(line.Text)) continue;
                TableLine(columns.Split(line, page), $"page {page}, line '{line.Text}'");
            }
        }

        private void TableLine(Cells cells, string where)
        {
            var hasAmount = cells.Credit.Length > 0 || cells.Debit.Length > 0;
            if (cells.Text == "Kontostand" && !hasAmount && cells.Valuta.Length == 0 && cells.Date.Length > 0
                && cells.Saldo.Length > 0)
            {
                var kontostand = new PostFinanceKontostand(Date(cells.Date, where), ParseAmount(cells.Saldo, where));
                if (Opening is null) Opening = kontostand;
                else Closing = kontostand;
                return;
            }
            if (Opening is null) throw Unreadable($"the table doesn't start with the opening Kontostand ({where})");

            if (cells.Text == "Total" && cells.Date.Length == 0 && cells.Valuta.Length == 0 && cells.Saldo.Length == 0)
            {
                if (Total is not null) throw Unreadable($"the table has two Total lines ({where})");
                Total = new PostFinanceTotal(
                    cells.Credit.Length > 0 ? ParseAmount(cells.Credit, where) : 0,
                    cells.Debit.Length > 0 ? ParseAmount(cells.Debit, where) : 0);
                return;
            }
            if (Total is not null) throw Unreadable($"the table continues after its Total line ({where})");

            if (hasAmount)
            {
                // A row starts on the line that has its amount; the date is printed on a day's first row only.
                if (cells.Credit.Length > 0 && cells.Debit.Length > 0)
                    throw Unreadable($"a row has both a Gutschrift and a Lastschrift ({where})");
                if (cells.Text.Length == 0) throw Unreadable($"a row has an amount but no text ({where})");
                if (cells.Date.Length > 0) _date = Date(cells.Date, where);
                Rows.Add(new RowInProgress(
                    _date ?? throw Unreadable($"the first row has no date ({where})"),
                    cells.Credit.Length > 0 ? ParseAmount(cells.Credit, where) : null,
                    cells.Debit.Length > 0 ? ParseAmount(cells.Debit, where) : null,
                    cells.Saldo.Length > 0 ? ParseAmount(cells.Saldo, where) : null)
                {
                    Text = { cells.Text },
                });
                return;
            }

            // Anything else can only be a further line of the text of the row above.
            if (cells.Text.Length == 0 || cells.Date.Length > 0 || cells.Valuta.Length > 0 || cells.Saldo.Length > 0
                || Rows.Count == 0)
                throw Unreadable($"the table has a line that is neither a row nor part of a row's text ({where})");
            Rows[^1].Text.Add(cells.Text);
        }

        private static DateOnly Date(string text, string where) =>
            TryDate(text, "dd.MM.yy", out var date) ? date : throw Unreadable($"'{text}' is not a date ({where})");
    }

    private sealed record RowInProgress(DateOnly Date, decimal? Credit, decimal? Debit, decimal? Saldo)
    {
        public List<string> Text { get; } = [];
    }

    private sealed record Cells(string Date, string Text, string Credit, string Debit, string Valuta, string Saldo);

    /// Where the table's columns are, taken from its header. Datum and Text are left-aligned; Gutschrift,
    /// Lastschrift, Valuta and Saldo are right-aligned to the right edge of their header word.
    private sealed class Columns(double textLeft, double[] rights)
    {
        public static Columns Of(Line header)
        {
            Word Header(string name) => header.Words.First(w => w.Text == name);
            return new Columns(Header("Text").BoundingBox.Left,
                HeaderNames[2..].Select(name => Header(name).BoundingBox.Right).ToArray());
        }

        public Cells Split(Line line, int page)
        {
            var date = new List<string>();
            var text = new List<string>();
            var right = rights.Select(_ => new List<Word>()).ToArray();
            Word? previous = null;
            var pastText = false;
            foreach (var word in line.Words)
            {
                var box = word.BoundingBox;
                if (box.Right < textLeft - 3) date.Add(word.Text);
                // The text starts at the column's left edge and runs word after word; whatever doesn't, or
                // follows a gap, is one of the right-aligned cells.
                else if (!pastText && (text.Count == 0 ? Math.Abs(box.Left - textLeft) <= 3 : box.Left - previous!.BoundingBox.Right < 8))
                    text.Add(word.Text);
                else
                {
                    pastText = true;
                    var column = Array.FindIndex(rights, r => r >= box.Right - 3);
                    if (column < 0) throw OutOfColumns();
                    right[column].Add(word);
                }
                previous = word;
            }
            // A cell that doesn't end at its column's right edge is not that column's.
            if (right.Where((words, i) => words.Count > 0 && Math.Abs(words[^1].BoundingBox.Right - rights[i]) > 3).Any())
                throw OutOfColumns();
            string Cell(int i) => string.Join(' ', right[i].Select(w => w.Text));
            return new Cells(string.Join(' ', date), string.Join(' ', text), Cell(0), Cell(1), Cell(2), Cell(3));

            StatementFormatException OutOfColumns() =>
                Unreadable($"page {page}, line '{line.Text}' has something outside the table's columns");
        }
    }
}

/// A "Kontostand" line: the balance at the end of <paramref name="Date"/>.
internal sealed record PostFinanceKontostand(DateOnly Date, decimal Amount);

/// The "Total" line: the sums of the two amount columns, both positive.
internal sealed record PostFinanceTotal(decimal Credit, decimal Debit);

/// <param name="Date">The booking date ("Datum"), carried down from the day's first row.</param>
/// <param name="Text">The lines of the row's text block, as printed.</param>
/// <param name="Credit">"Gutschrift" (money in), positive; a row has either this or <paramref name="Debit"/>.</param>
/// <param name="Debit">"Lastschrift" (money out), positive.</param>
/// <param name="Saldo">The balance after the row, printed on a day's last row only.</param>
internal sealed record PostFinanceRow(DateOnly Date, IReadOnlyList<string> Text, decimal? Credit, decimal? Debit,
    decimal? Saldo);
