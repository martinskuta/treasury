using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Treasury.Api.Integrations;

/// Reads ISO 20022 camt.053 (bank-to-customer statement) files for camt.053 Integrations such as TKB: a single
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
        foreach (var statement in statements) Check(statement);
        CheckTogether(statements);
        return statements.OrderBy(s => s.From).ToList();
    }

    private static void Check(Camt053Statement statement)
    {
        var name = statement.FileName;
        if (!statement.IsLastPage)
            throw new StatementFormatException(
                $"{name} is only one page of a statement with several pages (not the last page). Download the whole statement.");
        var opening = statement.Opening
            ?? throw new StatementFormatException($"{name}: the statement has no opening booked balance (OPBD).");
        var closing = statement.Closing
            ?? throw new StatementFormatException($"{name}: the statement has no closing booked balance (CLBD).");
        var booked = statement.Entries.Where(e => e.IsBooked).Sum(e => e.Amount);
        if (opening.Amount + booked != closing.Amount)
            throw new StatementFormatException(
                $"{name}: the opening balance {Format(opening.Amount)} plus the booked entries {Format(booked)} " +
                $"is {Format(opening.Amount + booked)}, but the closing balance is {Format(closing.Amount)}. " +
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
        for (var i = 0; i < statements.Count; i++)
        for (var j = i + 1; j < statements.Count; j++)
        {
            var (a, b) = (statements[i], statements[j]);
            if (a.From is null || a.To is null || b.From is null || b.To is null) continue;
            if (a.From <= b.To && b.From <= a.To)
                throw new StatementFormatException(
                    $"The statements {a.FileName} ({Period(a)}) and {b.FileName} ({Period(b)}) overlap; " +
                    "their movements would be imported twice.");
        }
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Period(Camt053Statement s) =>
        $"{s.From!.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} to " +
        $"{s.To!.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";

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

    /// Unpacks an entry, counting the bytes actually read (the sizes in the zip's directory may lie).
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
        var account = Child(stmt, "Acct");
        var iban = Text(account, "Id", "IBAN") ?? Text(account, "Id", "Othr", "Id")
            ?? throw new StatementFormatException($"{name}: the statement has no account IBAN.");
        var balances = Children(stmt, "Bal").Select(b => ReadBalance(name, b)).ToList();
        var opening = balances.FirstOrDefault(b => b.Code == "OPBD") ?? balances.FirstOrDefault(b => b.Code == "PRCD");
        var closing = balances.FirstOrDefault(b => b.Code == "CLBD");
        var currency = Text(account, "Ccy") ?? closing?.Currency
            ?? throw new StatementFormatException($"{name}: the statement has no account currency.");
        var lastPage = Child(stmt, "StmtPgntn", "LastPgInd") is { } stmtLastPage
            ? ParseBool(stmtLastPage, name)
            : groupLastPage ?? true;

        var period = Child(stmt, "FrToDt");
        DateTimeOffset? from = Text(period, "FrDtTm") is { } f ? ParseDateTime(f, name)
            : opening is null ? null : StartOf(opening.Date);
        DateTimeOffset? to = Text(period, "ToDtTm") is { } t ? ParseDateTime(t, name)
            : closing is null ? null : EndOf(closing.Date);

        var entries = Children(stmt, "Ntry").Select((e, i) => ReadEntry($"{name}, entry {i + 1}", e)).ToList();
        return new Camt053Statement(name, iban.Replace(" ", ""), currency.ToUpperInvariant(), from, to,
            opening is null ? null : new Camt053Balance(opening.Date, opening.Amount),
            closing is null ? null : new Camt053Balance(closing.Date, closing.Amount),
            lastPage, entries);
    }

    private sealed record RawBalance(string? Code, DateOnly Date, decimal Amount, string? Currency);

    private static RawBalance ReadBalance(string name, XElement bal)
    {
        var code = Text(bal, "Tp", "CdOrPrtry", "Cd") ?? Text(bal, "Tp", "CdOrPrtry", "Prtry");
        var amount = Child(bal, "Amt") ?? throw new StatementFormatException($"{name}: the {code} balance has no amount.");
        var date = Text(bal, "Dt", "Dt") is { } d ? ParseDate(d, name)
            : Text(bal, "Dt", "DtTm") is { } dt ? DateOnly.FromDateTime(ParseDateTime(dt, name).DateTime)
            : throw new StatementFormatException($"{name}: the {code} balance has no date.");
        return new RawBalance(code, date, Signed(ParseAmount(amount.Value, name), Text(bal, "CdtDbtInd"), name),
            amount.Attribute("Ccy")?.Value);
    }

    private static Camt053Entry ReadEntry(string at, XElement ntry)
    {
        var amount = Child(ntry, "Amt") ?? throw new StatementFormatException($"{at}: the entry has no amount.");
        var indicator = Text(ntry, "CdtDbtInd");
        var status = Text(ntry, "Sts", "Cd") ?? Text(ntry, "Sts", "Prtry") ?? Text(ntry, "Sts") ?? "";
        var bookingDate = Text(ntry, "BookgDt", "Dt") is { } d ? ParseDate(d, at)
            : Text(ntry, "BookgDt", "DtTm") is { } dt ? DateOnly.FromDateTime(ParseDateTime(dt, at).DateTime)
            : (DateOnly?)null;
        var details = Children(ntry, "NtryDtls")
            .SelectMany(n => Children(n, "TxDtls"))
            .Select(tx => ReadDetails(at, tx, indicator))
            .ToList();
        return new Camt053Entry(at, Signed(ParseAmount(amount.Value, at), indicator, at),
            amount.Attribute("Ccy")?.Value.ToUpperInvariant(), status.Trim(), bookingDate, Text(ntry, "AcctSvcrRef"),
            Text(ntry, "AddtlNtryInf"), details);
    }

    private static Camt053TransactionDetails ReadDetails(string at, XElement tx, string? entryIndicator)
    {
        var amount = Child(tx, "Amt") ?? Child(tx, "AmtDtls", "TxAmt", "Amt");
        var indicator = Text(tx, "CdtDbtInd") ?? entryIndicator;
        var instructed = Child(tx, "AmtDtls", "InstdAmt", "Amt");
        var parties = Child(tx, "RltdPties");
        var remittance = Child(tx, "RmtInf");
        return new Camt053TransactionDetails(
            amount is null ? null : Signed(ParseAmount(amount.Value, at), indicator, at),
            amount?.Attribute("Ccy")?.Value.ToUpperInvariant(),
            Text(tx, "Refs", "AcctSvcrRef"),
            PartyName(parties, "Dbtr"),
            PartyName(parties, "UltmtDbtr"),
            PartyName(parties, "Cdtr"),
            PartyName(parties, "UltmtCdtr"),
            Children(remittance, "Ustrd").Select(u => u.Value.Trim()).Where(u => u.Length > 0).ToList(),
            Children(remittance, "Strd").Select(s => Text(s, "CdtrRefInf", "Ref")).FirstOrDefault(r => r is not null),
            instructed is null ? null : Signed(ParseAmount(instructed.Value, at), indicator, at),
            instructed?.Attribute("Ccy")?.Value.ToUpperInvariant());
    }

    /// A party's name: "Nm" directly (.001.04) or inside "Pty" (.001.08).
    private static string? PartyName(XElement? parties, string role) =>
        Text(parties, role, "Nm") ?? Text(parties, role, "Pty", "Nm");

    private static decimal Signed(decimal amount, string? indicator, string at) => indicator switch
    {
        "CRDT" => amount,
        "DBIT" => -amount,
        _ => throw new StatementFormatException($"{at}: '{indicator}' is not a credit/debit indicator (CRDT or DBIT)."),
    };

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

    private static decimal ParseAmount(string value, string at) =>
        decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new StatementFormatException($"{at}: '{value.Trim()}' is not an amount.");

    private static DateOnly ParseDate(string value, string at) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new StatementFormatException($"{at}: '{value}' is not a date.");

    private static DateTimeOffset ParseDateTime(string value, string at) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt)
            ? dt
            : throw new StatementFormatException($"{at}: '{value}' is not a date and time.");

    private static bool ParseBool(XElement element, string at) => element.Value.Trim() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        var other => throw new StatementFormatException($"{at}: '{other}' is not true or false."),
    };

    private static DateTimeOffset StartOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    private static DateTimeOffset EndOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
}

/// One camt.053 statement (Stmt).
/// <param name="FileName">The XML file it came from (its path inside the zip for a zip).</param>
/// <param name="From">Start of the statement period (FrToDt, else the opening balance's date).</param>
/// <param name="To">End of the statement period (FrToDt, else the closing balance's date).</param>
/// <param name="Opening">Opening booked balance (OPBD, else PRCD), signed.</param>
/// <param name="Closing">Closing booked balance (CLBD), signed.</param>
/// <param name="IsLastPage">False when the statement is one page of several and not the last one.</param>
public sealed record Camt053Statement(
    string FileName,
    string Iban,
    string Currency,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Camt053Balance? Opening,
    Camt053Balance? Closing,
    bool IsLastPage,
    IReadOnlyList<Camt053Entry> Entries);

public sealed record Camt053Balance(DateOnly Date, decimal Amount);

/// One entry (Ntry). Amounts are signed: credit positive, debit negative.
/// <param name="Location">Where it is, for error messages, e.g. "statement.xml, entry 3".</param>
/// <param name="Status">BOOK (booked), PDNG (pending), INFO, ...</param>
public sealed record Camt053Entry(
    string Location,
    decimal Amount,
    string? Currency,
    string Status,
    DateOnly? BookingDate,
    string? AccountServicerReference,
    string? AdditionalInfo,
    IReadOnlyList<Camt053TransactionDetails> Details)
{
    public bool IsBooked => Status == "BOOK";
}

/// One transaction's details (TxDtls) inside an entry. Amounts are signed (the detail's indicator, else the entry's).
public sealed record Camt053TransactionDetails(
    decimal? Amount,
    string? Currency,
    string? AccountServicerReference,
    string? Debtor,
    string? UltimateDebtor,
    string? Creditor,
    string? UltimateCreditor,
    IReadOnlyList<string> UnstructuredRemittance,
    string? CreditorReference,
    decimal? InstructedAmount,
    string? InstructedCurrency);
