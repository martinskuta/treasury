using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Treasury.Api.Integrations;

/// What Revolut's Custom statement PDF (English) says about the current accounts, as printed: nothing is checked
/// against anything else here (that is <see cref="RevolutIntegration"/>'s job).
/// <param name="PeriodStart">First day of the statement period in the document header.</param>
/// <param name="PeriodEnd">Last day of the statement period.</param>
/// <param name="Summaries">The accounts under "Current Accounts Summaries", in document order.</param>
/// <param name="Tables">The accounts under "Current Accounts Transaction Statements", in document order.</param>
/// <param name="SkippedSectionsWithEntries">Headings of the sections Treasury doesn't read that aren't empty.</param>
internal sealed record RevolutCustomStatementPdf(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<RevolutAccountSummary> Summaries,
    IReadOnlyList<RevolutAccountTable> Tables,
    IReadOnlyList<string> SkippedSectionsWithEntries)
{
    public const string ExportHint =
        "In the Revolut app, open Profile → Documents & statements → Custom statement, export it as PDF in " +
        "English, and import that file.";

    private const string SummariesHeading = "Current Accounts Summaries";
    private const string TransactionsHeading = "Current Accounts Transaction Statements";

    /// The statement is read from word positions, not from its text: table cells wrap, and every amount of a
    /// non-CHF account has a smaller converted CHF amount beneath it, which is dropped by its font size.
    public static RevolutCustomStatementPdf Read(byte[] bytes)
    {
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 1024)).IndexOf("%PDF-"u8) < 0)
            throw new StatementFormatException(
                $"This isn't a PDF. Treasury reads Revolut's Custom statement, not the CSV account statement. {ExportHint}");

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

        var periodLine = pages.FirstOrDefault()?.Select(l => Period.Match(l.Text)).FirstOrDefault(m => m.Success);
        if (periodLine is null
            || !pages[0].Any(l => l.Text == "Custom Statement")
            || !pages.SelectMany(p => p).Any(l => l.Text == SummariesHeading)
            || !TryDate(periodLine.Groups["from"].Value, out var periodStart)
            || !TryDate(periodLine.Groups["to"].Value, out var periodEnd))
            throw new StatementFormatException($"This PDF isn't a Revolut Custom statement in English. {ExportHint}");

        var reading = new Reading(periodLine.Value);
        foreach (var page in pages) reading.Page(page);
        return new RevolutCustomStatementPdf(periodStart, periodEnd, reading.Summaries(), reading.Tables(),
            reading.SkippedSectionsWithEntries());
    }

    /// The statement's layout wasn't what Treasury expects.
    private static StatementFormatException Unreadable(string what) => new(
        $"Treasury couldn't read this Revolut Custom statement: {what}. Revolut may have changed the statement's " +
        "layout. Nothing was imported.");

    private static readonly Regex Period = new(@"^(?<from>\w{3} \d{1,2}, \d{4}) - (?<to>\w{3} \d{1,2}, \d{4})$");

    private static readonly Regex AccountHeading = new(@"^(?<kind>.+ Account) \((?<currency>[A-Z]{3})\)$");

    private static readonly Regex IbanLine =
        new(@"^Account Number \((?:\w+ )?IBAN\) (?<iban>[A-Z]{2}\d{2}(?: ?[A-Z0-9])+?)(?= Opening | Closing | Days |$)");

    private static readonly Regex OpeningBalance = new(@"(?:^| )Opening balance (?<amount>.+)$");

    private static readonly Regex ClosingBalance = new(@"(?:^| )Closing balance (?<amount>.+)$");

    /// "-$4.20", "€25.50", "AU$0.00", "¥0", "1,500 ISK", "-1,250.00 CHF": a sign before the symbol or before
    /// the number, thousands separators, decimals or none, a symbol before or a code after. Neither symbol nor
    /// code decides the currency: that is the account's heading.
    private static readonly Regex Amount = new(
        @"^(?<sign>[-−+])?\s*[^\d\s.,+\-−]*\s*(?<sign>[-−+])?(?<number>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s*(?:[A-Za-z]{3})?$");

    private static bool TryDate(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, "MMM d, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static decimal ParseAmount(string text, string where)
    {
        var match = Amount.Match(text.Trim());
        if (!match.Success || match.Groups["sign"].Captures.Count > 1)
            throw Unreadable($"'{text}' ({where}) is not an amount");
        var number = decimal.Parse(match.Groups["number"].Value.Replace(",", ""), CultureInfo.InvariantCulture);
        return match.Groups["sign"].Value is "-" or "−" ? -number : number;
    }

    // ---- words to lines ----

    private sealed record Line(double Y, double Size, IReadOnlyList<Word> Words)
    {
        public string Text { get; } = string.Join(' ', Words.Select(w => w.Text));
    }

    /// Every page's words as lines, top to bottom, each line left to right. Words in a font much smaller than the
    /// statement's usual one (the converted CHF amounts) are dropped.
    private static List<List<Line>> Lines(PdfDocument document)
    {
        var pages = document.GetPages().Select(p => p.GetWords().Where(w => w.Letters.Count > 0).ToList()).ToList();
        // The usual font is the one of the page header's "Generated on the ..." line. (Not the most common one: a
        // long table of a non-CHF account has about as many converted amounts as other words.)
        var usual = pages.SelectMany(p => p).FirstOrDefault(w => w.Text == "Generated")?.Letters[0].PointSize ?? 0;
        return pages.Select(words =>
        {
            var lines = new List<List<Word>>();
            foreach (var word in words.Where(w => w.Letters[0].PointSize >= usual * 0.75).OrderByDescending(Baseline))
                if (lines.Count > 0 && Baseline(lines[^1][0]) - Baseline(word) < 2) lines[^1].Add(word);
                else lines.Add([word]);
            return lines
                .Select(l => new Line(Baseline(l[0]), l.Max(w => w.Letters[0].PointSize),
                    l.OrderBy(w => w.BoundingBox.Left).ToList()))
                .ToList();
        }).ToList();
    }

    private static double Baseline(Word word) => word.Letters[0].StartBaseLine.Y;

    // ---- lines to sections, accounts and rows ----

    private enum Section { BeforeFirst, Summaries, Transactions, Information, Skipped }

    /// Walks the lines of the document in order, keeping track of the section and the account they belong to. A
    /// table may run over several pages; its state is kept across pages.
    private sealed class Reading(string periodText)
    {
        private Section _section = Section.BeforeFirst;
        private double _headingSize;
        private readonly List<SummaryInProgress> _summaries = [];
        private readonly List<TableInProgress> _tables = [];
        private readonly List<(string Heading, bool HasEntries)> _skipped = [];
        private SummaryInProgress? _summary;
        private TableInProgress? _table;

        public void Page(List<Line> lines)
        {
            // The table header is three lines ("Money in" above and "/out" below "Date ... Fees").
            var headers = lines.Where(IsTableHeader).ToList();
            foreach (var line in lines)
            {
                if (line.Text == "Custom Statement" || line.Text == periodText
                    || line.Text.StartsWith("Generated on the ", StringComparison.Ordinal))
                    continue;
                if (_section == Section.BeforeFirst)
                {
                    if (line.Text != SummariesHeading) continue;
                    _headingSize = line.Size;
                }
                if (line.Size >= _headingSize - 0.5)
                {
                    _section = line.Text switch
                    {
                        SummariesHeading => Section.Summaries,
                        TransactionsHeading => Section.Transactions,
                        _ when line.Text.StartsWith("Information about ", StringComparison.Ordinal) => Section.Information,
                        _ => Section.Skipped,
                    };
                    if (_section == Section.Skipped) _skipped.Add((line.Text, false));
                    _summary = null;
                    _table = null;
                    continue;
                }

                switch (_section)
                {
                    case Section.Skipped:
                        _skipped[^1] = (_skipped[^1].Heading, true);
                        break;
                    case Section.Summaries:
                        SummaryLine(line);
                        break;
                    case Section.Transactions:
                        var header = headers.FirstOrDefault(h => Math.Abs(h.Y - line.Y) <= 10);
                        if (header is null) TableLine(line);
                        else if (_table is not null) _table.Columns = Columns.Of(header, lines);
                        break;
                }
            }
        }

        private static bool IsTableHeader(Line line) =>
            new[] { "Date", "Description", "Category", "Balance", "Fees" }.All(h => line.Words.Any(w => w.Text == h));

        private static string? Currency(Line line)
        {
            var heading = AccountHeading.Match(line.Text);
            if (!heading.Success) return null;
            if (heading.Groups["kind"].Value != "Personal Account")
                throw Unreadable($"it has a \"{line.Text}\", and Treasury reads only Personal Accounts");
            return heading.Groups["currency"].Value;
        }

        private void SummaryLine(Line line)
        {
            if (Currency(line) is { } currency)
            {
                _summaries.Add(_summary = new SummaryInProgress(currency));
                return;
            }
            if (_summary is null) return;
            var where = $"the {_summary.Currency} account's summary";
            if (IbanLine.Match(line.Text) is { Success: true } iban)
                _summary.Iban = _summary.Iban is null
                    ? iban.Groups["iban"].Value.Replace(" ", "")
                    : throw Unreadable($"{where} has two IBANs");
            if (OpeningBalance.Match(line.Text) is { Success: true } opening)
                _summary.Opening = _summary.Opening is null
                    ? ParseAmount(opening.Groups["amount"].Value, $"{where}, opening balance")
                    : throw Unreadable($"{where} has two opening balances");
            if (ClosingBalance.Match(line.Text) is { Success: true } closing)
                _summary.Closing = _summary.Closing is null
                    ? ParseAmount(closing.Groups["amount"].Value, $"{where}, closing balance")
                    : throw Unreadable($"{where} has two closing balances");
        }

        private void TableLine(Line line)
        {
            if (Currency(line) is { } currency)
            {
                _tables.Add(_table = new TableInProgress(currency));
                return;
            }
            // Between the account's heading and its table there is only the sub-heading "Transaction statement".
            if (_table?.Columns is null || line.Text == "Transaction statement") return;

            var where = $"the {_table.Currency} transaction statement";
            var cells = _table.Columns.Split(line);
            if (cells[Columns.Date].Length > 0)
            {
                if (_table.Total is not null) throw Unreadable($"{where} continues after its Total row with '{line.Text}'");
                if (cells[Columns.Date] == "Total") _table.Total = new string[Columns.Count];
                else if (TryDate(cells[Columns.Date], out _)) _table.Rows.Add(new string[Columns.Count]);
                else throw Unreadable($"'{cells[Columns.Date]}' in {where} is not a date");
            }
            // A line without a date continues the cells of the row above it (wrapped description or amount).
            var row = _table.Total ?? _table.Rows.LastOrDefault()
                ?? throw Unreadable($"{where} starts with '{line.Text}', which is not a row");
            for (var i = 0; i < Columns.Count; i++)
                if (cells[i].Length > 0) row[i] = row[i] is { Length: > 0 } above ? $"{above} {cells[i]}" : cells[i];
        }

        public IReadOnlyList<RevolutAccountSummary> Summaries() => _summaries
            .Select(s => new RevolutAccountSummary(s.Currency, s.Iban,
                s.Opening ?? throw Unreadable($"the {s.Currency} account's opening balance wasn't found"),
                s.Closing ?? throw Unreadable($"the {s.Currency} account's closing balance wasn't found")))
            .ToList();

        public IReadOnlyList<RevolutAccountTable> Tables() => _tables.Select(t =>
        {
            var where = $"the {t.Currency} transaction statement";
            decimal Cell(string?[] row, int column, string name) => ParseAmount(row[column] ?? "", $"{where}, {name}");
            var total = t.Total ?? throw Unreadable($"{where} has no Total row");
            return new RevolutAccountTable(t.Currency,
                t.Rows.Select(r =>
                {
                    TryDate(r[Columns.Date]!, out var date);
                    return new RevolutTableRow(date, r[Columns.Description] ?? "", r[Columns.Category] ?? "",
                        Cell(r, Columns.Money, "Money in/out"), Cell(r, Columns.Balance, "Balance"),
                        Cell(r, Columns.Tax, "Tax withheld"), Cell(r, Columns.OtherTaxes, "Other taxes"),
                        Cell(r, Columns.Fees, "Fees"));
                }).ToList(),
                new RevolutTableTotal(Cell(total, Columns.Money, "Total of Money in/out"),
                    Cell(total, Columns.Tax, "Total of Tax withheld"), Cell(total, Columns.OtherTaxes, "Total of Other taxes"),
                    Cell(total, Columns.Fees, "Total of Fees")));
        }).ToList();

        public IReadOnlyList<string> SkippedSectionsWithEntries() =>
            _skipped.Where(s => s.HasEntries).Select(s => s.Heading).Distinct().ToList();
    }

    private sealed class SummaryInProgress(string currency)
    {
        public string Currency => currency;
        public string? Iban { get; set; }
        public decimal? Opening { get; set; }
        public decimal? Closing { get; set; }
    }

    private sealed class TableInProgress(string currency)
    {
        public string Currency => currency;
        public Columns? Columns { get; set; }

        /// The text of each row's cells so far, by column.
        public List<string?[]> Rows { get; } = [];
        public string?[]? Total { get; set; }
    }

    /// Where the table's columns start, taken from its header. Every column is left-aligned except Fees, the last
    /// one, which is right-aligned and so has no fixed start.
    private sealed class Columns(double[] starts)
    {
        public const int Date = 0, Description = 1, Category = 2, Money = 3, Balance = 4, Tax = 5, OtherTaxes = 6,
            Fees = 7, Count = 8;

        public static Columns Of(Line header, List<Line> page)
        {
            // "Money in /out", "Tax withheld" and "Other taxes" are wrapped around the header's middle line.
            var around = page.Where(l => Math.Abs(l.Y - header.Y) <= 10).SelectMany(l => l.Words).ToList();
            double Start(string word) => around.FirstOrDefault(w => w.Text == word)?.BoundingBox.Left
                ?? throw Unreadable($"the transaction table has no '{word}' column");
            return new Columns([Start("Date"), Start("Description"), Start("Category"), Start("Money"),
                Start("Balance"), Start("Tax"), Start("Other")]);
        }

        /// The line's text by column.
        public string[] Split(Line line)
        {
            var cells = Enumerable.Range(0, Count).Select(_ => new List<string>()).ToArray();
            Word? previous = null;
            var inFees = false;
            foreach (var word in line.Words)
            {
                var column = Array.FindLastIndex(starts, start => word.BoundingBox.Left >= start - 2);
                if (column < 0) column = Date;
                if (column == OtherTaxes)
                {
                    // Other taxes starts at its column's left edge and runs word after word; whatever doesn't,
                    // or follows a gap, is the right-aligned Fees.
                    var gap = cells[OtherTaxes].Count == 0
                        ? word.BoundingBox.Left - starts[OtherTaxes]
                        : word.BoundingBox.Left - previous!.BoundingBox.Right;
                    inFees = inFees || gap > word.Letters[0].PointSize * 0.6;
                    if (inFees) column = Fees;
                }
                cells[column].Add(word.Text);
                previous = word;
            }
            return cells.Select(c => string.Join(' ', c)).ToArray();
        }
    }
}

/// An account's page under "Current Accounts Summaries".
/// <param name="Iban">Null when the statement shows "N/A".</param>
internal sealed record RevolutAccountSummary(string Currency, string? Iban, decimal Opening, decimal Closing);

/// An account's table under "Current Accounts Transaction Statements".
internal sealed record RevolutAccountTable(string Currency, IReadOnlyList<RevolutTableRow> Rows, RevolutTableTotal Total);

/// <param name="Description">Wrapped lines joined by a single space.</param>
internal sealed record RevolutTableRow(DateOnly Date, string Description, string Category, decimal Money,
    decimal Balance, decimal Tax, decimal OtherTaxes, decimal Fees);

/// The table's Total row (it has no Balance).
internal sealed record RevolutTableTotal(decimal Money, decimal Tax, decimal OtherTaxes, decimal Fees);
