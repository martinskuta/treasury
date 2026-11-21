using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Treasury.Api.Integrations;

/// Reads ISO 20022 camt.053 (bank-to-customer statement) files for camt.053 Integrations (TKB, PostFinance): a single
/// XML document, or a zip with one or more of them. Pure like every Integration reader (no database, no files).
/// Elements are matched by local name, so the schema versions (.001.04, .001.08, ...) read the same.
public static class Camt053Reader
{
    /// Total unpacked XML a zip may contain.
    public const long MaxUnpackedBytes = 50L * 1024 * 1024;

    /// Reads and checks every statement in <paramref name="file"/>, ordered by period start. Each statement must be
    /// complete (last page; opening balance + booked entries = closing balance), and all of them must be for the
    /// same account and currency with periods that don't overlap (gaps are fine); otherwise nothing is returned and a
    /// <see cref="StatementFormatException"/> says what is wrong. <paramref name="exportHint"/> (e.g. "Export the
    /// statement as camt.053 from TKB e-banking.") is added to the error for content that isn't camt.053.
    public static IReadOnlyList<Camt053Statement> Read(SourceFileContent file, string exportHint)
    {
        var statements = new List<Camt053Statement>();
        foreach (var (name, document) in Documents(file, exportHint))
            statements.AddRange(ReadDocument(name, document, exportHint));
        foreach (var statement in statements) CheckBalance(statement);
        CheckTogether(statements);
        return statements.OrderBy(s => s.From).ToList();
    }

    private static void CheckBalance(Camt053Statement statement)
    {
        var (opening, closing) = (statement.Opening.Amount, statement.Closing.Amount);
        var booked = statement.Entries.Where(e => e.IsBooked).Sum(e => e.Amount);
        if (opening + booked != closing)
            throw new StatementFormatException(
                $"{statement.FileName}: the opening balance {Format(opening)} plus the money in and out with status BOOK " +
                $"({Format(booked)}) is {Format(opening + booked)}, but the closing balance is {Format(closing)}. " +
                "The statement is incomplete or damaged.");
    }

    private static void CheckTogether(List<Camt053Statement> statements)
    {
        var first = statements[0];
        foreach (var other in statements.Skip(1))
        {
            if (other.Iban != first.Iban)
                throw new StatementFormatException(
                    $"{first.FileName} is for account {first.Iban} but {other.FileName} is for account {other.Iban}; " +
                    "one file may only contain statements of one account.");
            if (other.Currency != first.Currency)
                throw new StatementFormatException(
                    $"{first.FileName} is in {first.Currency} but {other.FileName} is in {other.Currency}; " +
                    "one file may only contain statements in one currency.");
        }
        // Periods are half-open [From, To): a statement ending at the instant the next one starts doesn't overlap it.
        for (var i = 0; i < statements.Count; i++)
        for (var j = i + 1; j < statements.Count; j++)
        {
            var (a, b) = (statements[i], statements[j]);
            if (a.From < b.To && b.From < a.To)
                throw new StatementFormatException(
                    $"The statements {a.FileName} ({Period(a)}) and {b.FileName} ({Period(b)}) overlap; " +
                    "their Transactions would be imported twice.");
        }
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Period(Camt053Statement s) =>
        $"{s.From.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} to " +
        $"{s.To.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

    private static IEnumerable<(string Name, XDocument Document)> Documents(SourceFileContent file, string exportHint)
    {
        if (!IsZip(file.Bytes))
        {
            yield return (file.FileName, Parse(file.FileName, new MemoryStream(file.Bytes), exportHint));
            yield break;
        }

        ZipArchive zip;
        try { zip = new ZipArchive(new MemoryStream(file.Bytes), ZipArchiveMode.Read); }
        catch (InvalidDataException)
        {
            throw new StatementFormatException($"{file.FileName} is not a readable zip file. {exportHint}");
        }
        using (zip)
        {
            var xmlEntries = zip.Entries
                .Where(e => !e.FullName.EndsWith('/') && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (xmlEntries.Count == 0)
                throw new StatementFormatException(
                    $"The zip {file.FileName} contains no camt.053 XML statement. {exportHint}");

            var budget = new UnpackBudget(file.FileName);
            foreach (var entry in xmlEntries)
            {
                var bytes = Unpack(entry, budget);
                yield return (entry.FullName, Parse(entry.FullName, new MemoryStream(bytes), exportHint));
            }
        }
    }

    private static bool IsZip(byte[] bytes) =>
        bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] is 3 or 5 && bytes[3] is 4 or 6;

    private sealed class UnpackBudget(string zipName)
    {
        private long _used;

        public void Take(int bytes)
        {
            _used += bytes;
            if (_used > MaxUnpackedBytes)
                throw new StatementFormatException(
                    $"The zip {zipName} unpacks to more than {MaxUnpackedBytes / (1024 * 1024)} MB of XML; that is too large to import.");
        }
    }

    /// Unpacks a zip entry, counting the bytes actually read (the sizes in the zip's directory may lie).
    private static byte[] Unpack(ZipArchiveEntry entry, UnpackBudget budget)
    {
        using var source = entry.Open();
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            budget.Take(read);
            target.Write(buffer, 0, read);
        }
        return target.ToArray();
    }

    private static XDocument Parse(string name, Stream content, string exportHint)
    {
        try
        {
            using var reader = XmlReader.Create(content, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new StatementFormatException($"{name} is not a camt.053 XML statement. {exportHint}");
        }
    }

    private static IEnumerable<Camt053Statement> ReadDocument(string name, XDocument document, string exportHint)
    {
        var root = document.Root!;
        var message = root.Elements().FirstOrDefault();
        if (root.Name.LocalName != "Document" || message?.Name.LocalName != "BkToCstmrStmt")
            throw new StatementFormatException(
                $"{name} is not a camt.053 bank-to-customer statement" +
                (root.Name.NamespaceName.Length > 0 ? $" ({root.Name.NamespaceName})" : "") + $". {exportHint}");

        var lastPage = Child(message, "GrpHdr", "MsgPgntn", "LastPgInd") is { } groupLastPage
            ? (bool?)ParseBool(groupLastPage, name)
            : null;
        var statementElements = message.Elements().Where(e => e.Name.LocalName == "Stmt").ToList();
        if (statementElements.Count == 0)
            throw new StatementFormatException($"{name} contains no statement (Stmt).");
        return statementElements.Select(s => ReadStatement(name, s, lastPage)).ToList();
    }

    private static Camt053Statement ReadStatement(string name, XElement stmt, bool? groupLastPage)
    {
        var isLastPage = Child(stmt, "StmtPgntn", "LastPgInd") is { } stmtLastPage
            ? ParseBool(stmtLastPage, name)
            : groupLastPage ?? true;
        if (!isLastPage)
            throw new StatementFormatException(
                $"{name} is only one page of a statement with several pages (not the last page). Download the whole statement.");

        var account = Child(stmt, "Acct");
        var iban = Text(account, "Id", "IBAN") ?? Text(account, "Id", "Othr", "Id")
            ?? throw new StatementFormatException($"{name}: the statement has no account IBAN.");
        var balances = Children(stmt, "Bal").Select(b => ReadBalance(name, b)).ToList();
        var opening = balances.FirstOrDefault(b => b.Code == "OPBD") ?? balances.FirstOrDefault(b => b.Code == "PRCD")
            ?? throw new StatementFormatException(
                $"{name}: the statement has no opening booked balance (OPBD, or PRCD for the previous day's closing balance).");
        var closing = balances.FirstOrDefault(b => b.Code == "CLBD")
            ?? throw new StatementFormatException($"{name}: the statement has no closing booked balance (CLBD).");
        var currency = Text(account, "Ccy") ?? closing.Currency
            ?? throw new StatementFormatException($"{name}: the statement has no account currency.");
        var entries = Children(stmt, "Ntry").Select((e, i) => ReadEntry($"{name}, Ntry {i + 1}", e)).ToList();

        // Without FrToDt the period runs from the first booking date (the opening balance may be PRCD, dated the
        // previous day) to the end of the closing balance's day.
        var period = Child(stmt, "FrToDt");
        var from = Text(period, "FrDtTm") is { } f ? ParseDateTime(f, name)
            : StartOf(entries.Where(e => e.IsBooked).Min(e => e.BookingDate) ?? closing.Date);
        var to = Text(period, "ToDtTm") is { } t ? ParseDateTime(t, name) : StartOf(closing.Date.AddDays(1));

        return new Camt053Statement(name, iban.Replace(" ", ""), currency.ToUpperInvariant(), from, to,
            opening, closing, entries);
    }

    private static Camt053Balance ReadBalance(string name, XElement bal)
    {
        var code = Text(bal, "Tp", "CdOrPrtry", "Cd") ?? Text(bal, "Tp", "CdOrPrtry", "Prtry");
        var amount = Child(bal, "Amt") ?? throw new StatementFormatException($"{name}: the {code} balance has no amount.");
        var date = ReadDate(Child(bal, "Dt"), name)
            ?? throw new StatementFormatException($"{name}: the {code} balance has no date.");
        return new Camt053Balance(code, date, SignedAmount(amount, Text(bal, "CdtDbtInd"), name), Currency(amount));
    }

    private static Camt053Entry ReadEntry(string at, XElement ntry)
    {
        var amount = Child(ntry, "Amt") ?? throw new StatementFormatException($"{at}: there is no amount (Amt).");
        var indicator = Text(ntry, "CdtDbtInd");
        var status = Text(ntry, "Sts", "Cd") ?? Text(ntry, "Sts", "Prtry") ?? Text(ntry, "Sts") ?? "";
        var details = Children(ntry, "NtryDtls")
            .SelectMany(n => Children(n, "TxDtls"))
            .Select(tx => ReadDetails(at, tx, indicator))
            .ToList();
        return new Camt053Entry(at, SignedAmount(amount, indicator, at), indicator == "CRDT", Currency(amount),
            status, ReadDate(Child(ntry, "BookgDt"), at), Text(ntry, "AcctSvcrRef"), Text(ntry, "AddtlNtryInf"),
            InstructedAmount(ntry, indicator, at), details);
    }

    private static Camt053TransactionDetails ReadDetails(string at, XElement tx, string? entryIndicator)
    {
        var amount = Child(tx, "Amt") ?? Child(tx, "AmtDtls", "TxAmt", "Amt");
        var indicator = Text(tx, "CdtDbtInd") ?? entryIndicator;
        var parties = Child(tx, "RltdPties");
        var remittance = Child(tx, "RmtInf");
        return new Camt053TransactionDetails(
            amount is null ? null : SignedAmount(amount, indicator, at),
            indicator == "CRDT",
            Currency(amount),
            Text(tx, "Refs", "AcctSvcrRef"),
            PartyName(parties, "Dbtr"),
            PartyName(parties, "UltmtDbtr"),
            PartyName(parties, "Cdtr"),
            PartyName(parties, "UltmtCdtr"),
            Children(remittance, "Ustrd").Select(u => u.Value.Trim()).Where(u => u.Length > 0).ToList(),
            Children(remittance, "Strd").Select(s => Text(s, "CdtrRefInf", "Ref")).FirstOrDefault(r => r is not null),
            InstructedAmount(tx, indicator, at));
    }

    /// AmtDtls/InstdAmt of an entry or a transaction's details, signed like its parent.
    private static Camt053Amount? InstructedAmount(XElement parent, string? indicator, string at) =>
        Child(parent, "AmtDtls", "InstdAmt", "Amt") is { } amount
            ? new Camt053Amount(SignedAmount(amount, indicator, at), Currency(amount))
            : null;

    /// A party's name: "Nm" directly (.001.04) or inside "Pty" (.001.08).
    private static string? PartyName(XElement? parties, string role) =>
        Text(parties, role, "Nm") ?? Text(parties, role, "Pty", "Nm");

    /// An Amt element's value, positive for CRDT and negative for DBIT.
    private static decimal SignedAmount(XElement amount, string? indicator, string at)
    {
        var value = amount.Value.Trim();
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
            throw new StatementFormatException($"{at}: '{value}' is not an amount.");
        return indicator switch
        {
            "CRDT" => parsed,
            "DBIT" => -parsed,
            _ => throw new StatementFormatException(
                $"{at}: '{indicator}' is not a money in/out indicator (CdtDbtInd CRDT or DBIT)."),
        };
    }

    /// An Amt element's Ccy attribute, upper case.
    private static string? Currency(XElement? amount) =>
        amount?.Attribute("Ccy")?.Value.Trim() is { Length: > 0 } ccy ? ccy.ToUpperInvariant() : null;

    /// The date of a date choice element: its Dt child, else the date part of its DtTm child.
    private static DateOnly? ReadDate(XElement? choice, string at) =>
        Text(choice, "Dt") is { } d ? ParseDate(d, at)
        : Text(choice, "DtTm") is { } dt ? DateOnly.FromDateTime(ParseDateTime(dt, at).DateTime)
        : null;

    private static XElement? Child(XElement? parent, params string[] path)
    {
        var current = parent;
        foreach (var name in path)
        {
            current = current?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (current is null) return null;
        }
        return current;
    }

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string? Text(XElement? parent, params string[] path) =>
        Child(parent, path)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static DateOnly ParseDate(string value, string at) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new StatementFormatException($"{at}: '{value}' is not a date.");

    /// A date and time; one without an offset is taken as UTC, so the result never depends on the server's time zone.
    private static DateTimeOffset ParseDateTime(string value, string at) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : throw new StatementFormatException($"{at}: '{value}' is not a date and time.");

    private static bool ParseBool(XElement element, string at) => element.Value.Trim() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        var other => throw new StatementFormatException($"{at}: '{other}' is not true or false."),
    };

    private static DateTimeOffset StartOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// One complete camt.053 statement (Stmt): the last page, with both booked balances.
/// <param name="FileName">The XML file it came from (its path inside the zip for a zip).</param>
/// <param name="From">Start of the statement period (FrDtTm, else the first booking date, else the closing balance's date).</param>
/// <param name="To">End of the statement period, exclusive (ToDtTm, else the day after the closing balance's date).</param>
/// <param name="Opening">Opening booked balance (OPBD, else PRCD).</param>
/// <param name="Closing">Closing booked balance (CLBD).</param>
public sealed record Camt053Statement(
    string FileName,
    string Iban,
    string Currency,
    DateTimeOffset From,
    DateTimeOffset To,
    Camt053Balance Opening,
    Camt053Balance Closing,
    IReadOnlyList<Camt053Entry> Entries);

/// A balance (Bal). The amount is signed: credit positive, debit negative.
/// <param name="Code">OPBD, PRCD, CLBD, ...</param>
public sealed record Camt053Balance(string? Code, DateOnly Date, decimal Amount, string? Currency);

/// An amount with its currency (null when the Amt has no Ccy). Signed like its parent.
public sealed record Camt053Amount(decimal Amount, string? Currency);

/// One entry (Ntry). Amounts are signed: credit positive, debit negative.
/// <param name="Location">Where it is, for error messages, e.g. "statement.xml, Ntry 3".</param>
/// <param name="IsCredit">CdtDbtInd is CRDT (money in); otherwise DBIT (money out).</param>
/// <param name="Status">BOOK (booked), PDNG (pending), INFO, ...</param>
/// <param name="InstructedAmount">The entry's own AmtDtls/InstdAmt.</param>
public sealed record Camt053Entry(
    string Location,
    decimal Amount,
    bool IsCredit,
    string? Currency,
    string Status,
    DateOnly? BookingDate,
    string? AccountServicerReference,
    string? AdditionalInfo,
    Camt053Amount? InstructedAmount,
    IReadOnlyList<Camt053TransactionDetails> Details)
{
    public bool IsBooked => Status == "BOOK";
}

/// One transaction's details (TxDtls) inside an entry. Amounts are signed and <paramref name="IsCredit"/> is set by
/// the detail's own CdtDbtInd, else the entry's.
public sealed record Camt053TransactionDetails(
    decimal? Amount,
    bool IsCredit,
    string? Currency,
    string? AccountServicerReference,
    string? Debtor,
    string? UltimateDebtor,
    string? Creditor,
    string? UltimateCreditor,
    IReadOnlyList<string> UnstructuredRemittance,
    string? CreditorReference,
    Camt053Amount? InstructedAmount);
