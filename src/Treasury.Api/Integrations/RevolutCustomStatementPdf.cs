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
/// <param name="SkippedAccounts">Headings of the current accounts that aren't a "Personal Account (CUR)", e.g.
/// "Joint Account (EUR)": neither their summary nor their table is read.</param>
/// <param name="SkippedSectionsWithEntries">Headings of the sections Treasury doesn't read that aren't empty.</param>
internal sealed record RevolutCustomStatementPdf(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<RevolutAccountSummary> Summaries,
    IReadOnlyList<RevolutAccountTable> Tables,
    IReadOnlyList<string> SkippedAccounts,
    IReadOnlyList<string> SkippedSectionsWithEntries)
{
    public const string ExportHint =
        "In the Revolut app, open Profile → Documents & statements → Custom statement, export it as PDF in " +
        "English, and import that file.";

    private const string Title = "Custom Statement";
    private const string SummariesHeading = "Current Accounts Summaries";
    private const string TransactionsHeading = "Current Accounts Transaction Statements";

    /// The statement is read from word positions, not from its text: table cells wrap, and every amount of a
    /// non-CHF account is printed twice, in the account's currency and converted to CHF, one in the usual font and
    /// the other in a much smaller one beneath it. Which one is the converted amount depends on when the statement
    /// was generated (<see cref="InAccountCurrency"/>).
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
            || !pages[0].Any(l => l.Text == Title)
            || !pages.SelectMany(p => p).Any(l => l.Text == SummariesHeading)
            || !TryDate(periodLine.Groups["from"].Value, out var periodStart)
            || !TryDate(periodLine.Groups["to"].Value, out var periodEnd))
            throw new StatementFormatException($"This PDF isn't a Revolut Custom statement in English. {ExportHint}");

        var reading = new Reading(periodLine.Value);
        foreach (var page in pages) reading.ReadPage(page);
        return new RevolutCustomStatementPdf(periodStart, periodEnd, reading.Summaries(), reading.Tables(),
            reading.SkippedAccounts, reading.SkippedSectionsWithEntries());
    }

    /// The statement's layout wasn't what Treasury expects.
    private static StatementFormatException Unreadable(string what) => new(
        $"Treasury couldn't read this Revolut Custom statement: {what}. Revolut may have changed the statement's " +
        "layout. Nothing was imported.");

    private static readonly Regex Period = new(@"^(?<from>\w{3} \d{1,2}, \d{4}) - (?<to>\w{3} \d{1,2}, \d{4})$");

    private static readonly Regex AccountHeading = new(@"^(?<kind>.+ Account) \((?<currency>[A-Z]{3})\)$");

    /// "Account Number (IBAN) LT12…", or for a period over which Revolut moved the account to a new IBAN, one line
    /// per IBAN: "Account Number (LT IBAN) LT12…" and "Account Number (GB IBAN) GB29…".
    private static readonly Regex IbanLine =
        new(@"^Account Number \((?:\w+ )?IBAN\) (?<iban>[A-Z]{2}\d{2}(?: ?[A-Z0-9])+?)(?= Opening | Closing | Days |$)");

    private static readonly Regex OpeningBalance = new(@"(?:^| )Opening balance (?<amount>.+)$");

    private static readonly Regex ClosingBalance = new(@"(?:^| )Closing balance (?<amount>.+)$");

    /// "-$4.20", "€25.50", "AU$0.00", "¥0", "1,500 ISK", "-1,250.00 CHF": a sign before the symbol or before
    /// the number, thousands separators, decimals or none, a symbol before or a code after. Neither symbol nor
    /// code decides the currency: that is the account's heading.
    private static readonly Regex Amount = new(
        @"^(?<sign>[-−+])?\s*[^\d\s.,+\-−]*\s*(?<sign>[-−+])?(?<number>\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?)\s*(?:[A-Za-z]{3})?$");

    /// A currency code at the end of an amount: "1,250.00 CHF", but not the "AU$" of "AU$0.00".
    private static readonly Regex TrailingCode = new(@"(?:^|[\d\s])(?<code>[A-Z]{3})$");

    /// Of an amount's two printings, the one in the account's currency. Statements generated until late 2026 print
    /// it in the usual font with the CHF amount small beneath it ("€25.50" over "27.10 CHF"); later ones the other
    /// way round ("27.10 CHF" over "€25.50"). The usual-font one is taken unless it names another currency.
    private static string InAccountCurrency(string? usual, string? small, string currency, string where)
    {
        usual ??= "";
        if (!InOtherCurrency(usual)) return usual;
        if (small is { Length: > 0 } && !InOtherCurrency(small)) return small;
        throw Unreadable($"{where} is '{usual}', which is not in {currency}, with no {currency} amount beneath it");

        bool InOtherCurrency(string text) =>
            TrailingCode.Match(text.Trim()) is { Success: true } code && code.Groups["code"].Value != currency;
    }

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

    /// <param name="Small">The line's words are in a font much smaller than the statement's usual one: the second
    /// printing of the amounts above it, in another currency.</param>
    private sealed record Line(double Y, double Size, bool Small, IReadOnlyList<Word> Words)
    {
        public string Text { get; } = string.Join(' ', Words.Select(w => w.Text));
    }

    /// Every page's words as lines, top to bottom, each line left to right. Words in a font much smaller than the
    /// statement's usual one are lines of their own.
    private static List<List<Line>> Lines(PdfDocument document)
    {
        var pages = document.GetPages().Select(p => p.GetWords().Where(w => w.Letters.Count > 0).ToList()).ToList();
        // The usual font is the one of the page header's "Generated on the ..." line. (Not the most common one: a
        // long table of a non-CHF account has about as many small amounts as other words.)
        var usual = pages.SelectMany(p => p).FirstOrDefault(w => w.Text == "Generated")?.Letters[0].PointSize ?? 0;
        bool IsSmall(Word word) => word.Letters[0].PointSize < usual * 0.75;
        return pages.Select(words =>
        {
            var lines = new List<List<Word>>();
            foreach (var word in words.OrderByDescending(Baseline))
                if (lines.Count > 0 && Baseline(lines[^1][0]) - Baseline(word) < 2 && IsSmall(lines[^1][0]) == IsSmall(word))
                    lines[^1].Add(word);
                else lines.Add([word]);
            return lines
                .Select(l => new Line(Baseline(l[0]), l.Max(w => w.Letters[0].PointSize), IsSmall(l[0]),
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
        private readonly List<string> _skippedAccounts = [];
        private readonly List<(string Heading, bool HasEntries)> _skipped = [];
        private SummaryInProgress? _summary;
        private TableInProgress? _table;

        public void ReadPage(List<Line> lines)
        {
            // The table header is three lines ("Money in" above and "/out" below "Date ... Fees").
            var headers = lines.Where(IsTableHeader).ToList();
            foreach (var line in lines)
            {
                if (line.Text == Title || line.Text == periodText
                    || line.Text.StartsWith("Generated on the ", StringComparison.Ordinal))
                    continue;
                if (line.Small)
                {
                    if (_section == Section.Summaries) SmallSummaryLine(line);
                    else if (_section == Section.Transactions) SmallTableLine(line);
                    continue;
                }
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

        /// Whether the line is an account's heading. Only a "Personal Account (CUR)" is read: for any other kind
        /// (e.g. a joint account) <paramref name="currency"/> is null and the heading is kept as skipped.
        private bool IsAccountHeading(Line line, out string? currency)
        {
            currency = null;
            var heading = AccountHeading.Match(line.Text);
            if (!heading.Success) return false;
            if (heading.Groups["kind"].Value == "Personal Account") currency = heading.Groups["currency"].Value;
            else if (!_skippedAccounts.Contains(line.Text)) _skippedAccounts.Add(line.Text);
            return true;
        }

        private void SummaryLine(Line line)
        {
            if (IsAccountHeading(line, out var currency))
            {
                // The lines of an account that isn't read belong to no summary.
                _summary = currency is null ? null : new SummaryInProgress(currency);
                if (_summary is not null) _summaries.Add(_summary);
                return;
            }
            if (_summary is null) return;
            _summary.Last = null;
            var where = $"the {_summary.Currency} account's summary";
            // What the line gives for one of the summary's figures, which the summary must not have yet.
            string? Found(Regex figure, string group, bool alreadyRead, string twice)
            {
                var match = figure.Match(line.Text);
                if (!match.Success) return null;
                return alreadyRead ? throw Unreadable($"{where} has two {twice}") : match.Groups[group].Value;
            }

            if (IbanLine.Match(line.Text) is { Success: true } iban)
                _summary.Ibans.Add(iban.Groups["iban"].Value.Replace(" ", ""));
            if (Found(OpeningBalance, "amount", _summary.Opening is not null, "opening balances") is { } opening)
                _summary.Last = _summary.Opening = new Printed(opening);
            if (Found(ClosingBalance, "amount", _summary.Closing is not null, "closing balances") is { } closing)
                _summary.Last = _summary.Closing = new Printed(closing);
        }

        /// The small printing of the summary figure on the line above it.
        private void SmallSummaryLine(Line line)
        {
            if (_summary?.Last is { } figure) figure.Small = figure.Small is null ? line.Text : $"{figure.Small} {line.Text}";
        }

        /// The small printings of the amounts of the row above it.
        private void SmallTableLine(Line line)
        {
            if (_table?.Columns is null || (_table.Total ?? _table.Rows.LastOrDefault()) is not { } row) return;
            row.AddSmall(_table.Columns.Split(line));
        }

        private void TableLine(Line line)
        {
            if (IsAccountHeading(line, out var currency))
            {
                // The lines of an account that isn't read belong to no table.
                if (currency is null) _table = null;
                // A table that runs onto the next page may get its account's heading printed again there: until
                // its Total row, the same heading continues the table. After it, the account is listed twice.
                else if (_table is null || _table.Currency != currency || _table.Total is not null)
                    _tables.Add(_table = new TableInProgress(currency));
                return;
            }
            // Between the account's heading and its table there is only the sub-heading "Transaction statement".
            if (_table?.Columns is null || line.Text == "Transaction statement") return;

            var where = _table.Where;
            var cells = _table.Columns.Split(line);
            if (cells[Columns.Date].Length > 0)
            {
                if (_table.Total is not null) throw Unreadable($"{where} continues after its Total row with '{line.Text}'");
                if (cells[Columns.Date] == "Total") _table.Total = new TableRowInProgress();
                else if (TryDate(cells[Columns.Date], out _)) _table.Rows.Add(new TableRowInProgress());
                else throw Unreadable($"'{cells[Columns.Date]}' in {where} is not a date");
            }
            // A line without a date continues the cells of the row above it (wrapped description or amount).
            var row = _table.Total ?? _table.Rows.LastOrDefault()
                ?? throw Unreadable($"{where} starts with '{line.Text}', which is not a row");
            row.Add(cells);
        }

        public IReadOnlyList<RevolutAccountSummary> Summaries() => _summaries
            .Select(s =>
            {
                decimal Figure(Printed? printed, string name)
                {
                    var where = $"the {s.Currency} account's {name}";
                    if (printed is null) throw Unreadable($"{where} wasn't found");
                    return ParseAmount(InAccountCurrency(printed.Usual, printed.Small, s.Currency, where), where);
                }
                return new RevolutAccountSummary(s.Currency, s.Ibans,
                    Figure(s.Opening, "opening balance"), Figure(s.Closing, "closing balance"));
            })
            .ToList();

        public IReadOnlyList<RevolutAccountTable> Tables() => _tables.Select(t =>
        {
            var where = t.Where;
            decimal Cell(TableRowInProgress row, int column, string name)
            {
                var cell = $"{where}, {name}";
                return ParseAmount(InAccountCurrency(row.Usual[column], row.Small[column], t.Currency, cell), cell);
            }
            var total = t.Total ?? throw Unreadable($"{where} has no Total row");
            return new RevolutAccountTable(t.Currency,
                t.Rows.Select(row =>
                {
                    var r = row.Usual;
                    TryDate(r[Columns.Date]!, out var date);
                    return new RevolutTableRow(date, r[Columns.Description] ?? "", r[Columns.Category] ?? "",
                        Cell(row, Columns.Money, "Money in/out"), Cell(row, Columns.Balance, "Balance"),
                        Cell(row, Columns.Tax, "Tax withheld"), Cell(row, Columns.OtherTaxes, "Other taxes"),
                        Cell(row, Columns.Fees, "Fees"));
                }).ToList(),
                new RevolutTableTotal(Cell(total, Columns.Money, "Total of Money in/out"),
                    Cell(total, Columns.Tax, "Total of Tax withheld"), Cell(total, Columns.OtherTaxes, "Total of Other taxes"),
                    Cell(total, Columns.Fees, "Total of Fees")));
        }).ToList();

        public IReadOnlyList<string> SkippedAccounts => _skippedAccounts;

        public IReadOnlyList<string> SkippedSectionsWithEntries() =>
            _skipped.Where(s => s.HasEntries).Select(s => s.Heading).Distinct().ToList();
    }

    private sealed class SummaryInProgress(string currency)
    {
        public string Currency => currency;
        public List<string> Ibans { get; } = [];
        public Printed? Opening { get; set; }
        public Printed? Closing { get; set; }

        /// The figure on the summary's last line, which a small line beneath it prints again.
        public Printed? Last { get; set; }
    }

    /// A summary figure's amount as printed in the usual font, and in the small one beneath it.
    private sealed class Printed(string usual)
    {
        public string Usual => usual;
        public string? Small { get; set; }
    }

    private sealed class TableInProgress(string currency)
    {
        public string Currency => currency;

        /// How messages name the table.
        public string Where => $"the {currency} transaction statement";
        public Columns? Columns { get; set; }

        public List<TableRowInProgress> Rows { get; } = [];
        public TableRowInProgress? Total { get; set; }
    }

    /// The text of a row's cells so far, by column: in the usual font, and in the small one beneath it.
    private sealed class TableRowInProgress
    {
        public string?[] Usual { get; } = new string?[Columns.Count];
        public string?[] Small { get; } = new string?[Columns.Count];

        public void Add(string[] cells) => Append(Usual, cells);

        public void AddSmall(string[] cells) => Append(Small, cells);

        /// Wrapped cells are joined by a single space.
        private static void Append(string?[] row, string[] cells)
        {
            for (var i = 0; i < Columns.Count; i++)
                if (cells[i].Length > 0) row[i] = row[i] is { Length: > 0 } above ? $"{above} {cells[i]}" : cells[i];
        }
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
/// <param name="Ibans">In the order printed; empty when the statement shows "N/A", two when the account was moved to
/// a new IBAN during the period.</param>
internal sealed record RevolutAccountSummary(string Currency, IReadOnlyList<string> Ibans, decimal Opening, decimal Closing);

/// An account's table under "Current Accounts Transaction Statements".
internal sealed record RevolutAccountTable(string Currency, IReadOnlyList<RevolutTableRow> Rows, RevolutTableTotal Total);

/// <param name="Description">Wrapped lines joined by a single space.</param>
internal sealed record RevolutTableRow(DateOnly Date, string Description, string Category, decimal Money,
    decimal Balance, decimal Tax, decimal OtherTaxes, decimal Fees);

/// The table's Total row (it has no Balance).
internal sealed record RevolutTableTotal(decimal Money, decimal Tax, decimal OtherTaxes, decimal Fees);
