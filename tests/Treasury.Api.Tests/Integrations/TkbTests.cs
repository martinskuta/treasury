using System.Globalization;
using System.IO.Compression;
using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the tkb reader (camt.053), Source file content in, parsed statement out (samples in Tkb/).
public class TkbTests
{
    private static readonly TkbIntegration Reader = new();

    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Tkb", name));

    private static ParsedStatement ReadSample(string name) => Reader.Read(new SourceFileContent(name, Sample(name)));

    [Test]
    public async Task It_is_registered_as_tkb_with_an_identifier()
    {
        await Assert.That(Reader.Key).IsEqualTo("tkb");
        await Assert.That(Reader.Name).IsEqualTo("TKB");
        await Assert.That(Reader.CanProvideIdentifier).IsTrue();
    }

    [Test]
    [Arguments("camt053-2026-10-31.xml")]
    [Arguments("camt053-2026-10-31.zip")]
    public async Task Reads_the_sample_statement_as_xml_and_as_zip(string name)
    {
        var statement = ReadSample(name);

        await Assert.That(statement.Rows).Count().IsEqualTo(55);
        await Assert.That(statement.AccountIdentifier).IsEqualTo("CH1889144434487348535");
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(new DateOnly(2026, 10, 31), 1023381.13m));
    }

    [Test]
    public async Task Maps_card_payments_credit_transfers_and_direct_debits_of_the_sample()
    {
        var rows = ReadSample("camt053-2026-10-31.xml").Rows;

        await Assert.That(rows[0]).IsEqualTo(new StatementRow(new DateOnly(2026, 10, 1), -4731.46m, "CHF",
            "Card Payment\nKulas - Farrell, Marksfurt\nKulas - Farrell, Marksfurt 2026-10-01 card ****2149",
            "Kulas - Farrell, Marksfurt", "BKUZ169C7YTPU0SR"));
        await Assert.That(rows[1]).IsEqualTo(new StatementRow(new DateOnly(2026, 10, 1), 6151.30m, "CHF",
            "Credit Transfer\nRatke Group\nRF2988133774884", "Ratke Group", "WSN72MKQNPVIDD1Y"));
        await Assert.That(rows.Single(r => r.Reference == "5TEJNV0KPE5BAA4C")).IsEqualTo(new StatementRow(
            new DateOnly(2026, 10, 9), -5655.20m, "CHF", "Direct Debit\nCummings - Lemke\nContract 95438411 10/2026",
            "Cummings - Lemke", "5TEJNV0KPE5BAA4C"));
    }

    [Test]
    public async Task A_batch_entry_becomes_one_row_per_payment_and_a_batch_summary_stays_one_row()
    {
        var statement = ReadSample("batch.xml");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 15), -120.50m, "CHF",
                "Sammelauftrag\nHelvetia Versicherungen\n210000000003139471430009017", "Helvetia Versicherungen"),
            new StatementRow(new DateOnly(2026, 10, 15), -229.50m, "CHF",
                "Sammelauftrag\nEKT Energie\nStromrechnung\nOktober 2026", "EKT Energie", "TKB-BATCH-1-2"),
            new StatementRow(new DateOnly(2026, 10, 20), -80m, "CHF", "Sammelauftrag mit 2 Zahlungen", null, "TKB-BATCH-2"),
        });
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(new DateOnly(2026, 10, 31), 570m));
    }

    [Test]
    public async Task Only_booked_entries_are_read_and_pending_ones_are_left_out_of_the_balance_check()
    {
        var statement = ReadSample("pending.xml");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 10), 50m, "CHF", "TWINT Gutschrift\nAnna Muster", "Anna Muster", "TKB-TWINT-1"),
        });
    }

    [Test]
    public async Task A_foreign_currency_purchase_keeps_its_original_amount_and_currency()
    {
        var row = ReadSample("foreign-currency.xml").Rows.Single();

        await Assert.That(row).IsEqualTo(new StatementRow(new DateOnly(2026, 10, 21), -46.20m, "CHF",
            "Kartenzahlung\nTrattoria Roma", "Trattoria Roma", "TKB-CARD-FX-1", -48.00m, "EUR"));
    }

    [Test]
    public async Task Schema_version_001_08_reads_the_same()
    {
        var statement = ReadSample("camt053-001-08.xml");

        await Assert.That(statement.AccountIdentifier).IsEqualTo("CH9300762011623852957");
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(new DateOnly(2026, 10, 31), 180m));
        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 25), 200m, "CHF", "Gutschrift\nMuster AG\nLohn Oktober", "Muster AG", "TKB-V08-1"),
            new StatementRow(new DateOnly(2026, 10, 26), -20m, "CHF", "Kartenzahlung\nCoop", "Coop", "TKB-V08-2"),
        });
    }

    [Test]
    public async Task The_ultimate_creditor_or_debtor_is_preferred_as_counterparty()
    {
        var rows = ReadSample("ultimate-parties.xml").Rows;

        await Assert.That(rows.Select(r => r.Counterparty!)).IsEquivalentTo(["Digitec Galaxus AG", "Beat Beispiel"]);
        await Assert.That(rows[0].Description).IsEqualTo("Lastschrift\nDigitec Galaxus AG\nBestellung 4711");
    }

    [Test]
    [Arguments("broken-balance.xml", "balance")]
    [Arguments("not-last-page.xml", "page")]
    public async Task Incomplete_or_wrong_statements_are_rejected(string name, string mentions)
    {
        var e = Assert.Throws<StatementFormatException>(() => ReadSample(name));

        await Assert.That(e.Message).Contains(mentions);
        await Assert.That(e.Message).Contains(name);
    }

    [Test]
    [Arguments("Buchungsdatum;Text;Belastung;Gutschrift;Saldo\n31.10.2026;Bezug;10.00;;90.00\n")]
    [Arguments("")]
    public async Task A_csv_or_other_non_xml_file_is_rejected_with_a_hint_to_export_camt053(string content)
    {
        var e = Assert.Throws<StatementFormatException>(() =>
            Reader.Read(new SourceFileContent("export.csv", Encoding.UTF8.GetBytes(content))));

        await Assert.That(e.Message).Contains("camt.053");
        await Assert.That(e.Message).Contains("TKB e-banking");
    }

    [Test]
    public async Task A_camt054_file_is_rejected_with_a_hint_to_export_camt053()
    {
        var e = Assert.Throws<StatementFormatException>(() => ReadSample("camt054.xml"));

        await Assert.That(e.Message).Contains("TKB e-banking");
    }

    // ---- zips with several statements ----

    [Test]
    public async Task A_zip_with_several_statements_is_read_in_period_order_with_the_latest_closing_balance()
    {
        var zip = Zip(
            ("2026/day2.xml", Camt("2026-11-02", "2026-11-02", 100m, ("2026-11-02", -10m, "B"))),
            ("2026/day1.xml", Camt("2026-11-01", "2026-11-01", 0m, ("2026-11-01", 100m, "A"))),
            ("2026/day4.xml", Camt("2026-11-04", "2026-11-04", 90m, ("2026-11-04", -5m, "C"), ("2026-11-04", -1m, "D"))));

        var statement = Reader.Read(new SourceFileContent("download.zip", zip));

        await Assert.That(string.Join(",", statement.Rows.Select(r => r.Reference))).IsEqualTo("A,B,C,D");
        await Assert.That(statement.AccountIdentifier).IsEqualTo(Iban);
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(new DateOnly(2026, 11, 4), 84m));
    }

    [Test]
    public async Task Statements_with_overlapping_periods_in_one_zip_are_rejected_naming_both_files()
    {
        var zip = Zip(
            ("week.xml", Camt("2026-11-01", "2026-11-05", 0m, ("2026-11-02", 10m, "A"))),
            ("day.xml", Camt("2026-11-05", "2026-11-05", 10m, ("2026-11-05", 5m, "B"))));

        var e = Assert.Throws<StatementFormatException>(() => Reader.Read(new SourceFileContent("download.zip", zip)));

        await Assert.That(e.Message).Contains("week.xml");
        await Assert.That(e.Message).Contains("day.xml");
    }

    [Test]
    public async Task Statements_for_different_accounts_in_one_zip_are_rejected()
    {
        var zip = Zip(
            ("a.xml", Camt("2026-11-01", "2026-11-01", 0m, ("2026-11-01", 10m, "A"))),
            ("b.xml", Camt("2026-11-02", "2026-11-02", 0m, [("2026-11-02", 5m, "B")], iban: "CH5604835012345678009")));

        var e = Assert.Throws<StatementFormatException>(() => Reader.Read(new SourceFileContent("download.zip", zip)));

        await Assert.That(e.Message).Contains(Iban);
        await Assert.That(e.Message).Contains("CH5604835012345678009");
    }

    [Test]
    public async Task Statements_in_different_currencies_in_one_zip_are_rejected()
    {
        var zip = Zip(
            ("a.xml", Camt("2026-11-01", "2026-11-01", 0m, ("2026-11-01", 10m, "A"))),
            ("b.xml", Camt("2026-11-02", "2026-11-02", 0m, [("2026-11-02", 5m, "B")], currency: "EUR")));

        var e = Assert.Throws<StatementFormatException>(() => Reader.Read(new SourceFileContent("download.zip", zip)));

        await Assert.That(e.Message).Contains("EUR");
        await Assert.That(e.Message).Contains("CHF");
    }

    [Test]
    public async Task Other_files_in_the_zip_are_ignored()
    {
        var zip = Zip(
            ("statement.xml", Camt("2026-11-01", "2026-11-01", 0m, ("2026-11-01", 10m, "A"))),
            ("statement.pdf", "%PDF-1.7 not a statement"));

        var statement = Reader.Read(new SourceFileContent("download.zip", zip));

        await Assert.That(statement.Rows.Select(r => r.Reference!)).IsEquivalentTo(["A"]);
    }

    [Test]
    public async Task A_zip_without_xml_is_rejected()
    {
        var zip = Zip(("statement.pdf", "%PDF-1.7 not a statement"));

        var e = Assert.Throws<StatementFormatException>(() => Reader.Read(new SourceFileContent("download.zip", zip)));

        await Assert.That(e.Message).Contains("no camt.053");
        await Assert.That(e.Message).Contains("download.zip");
    }

    [Test]
    public async Task A_zip_that_unpacks_to_more_than_50_mb_is_rejected()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("huge.xml", CompressionLevel.Fastest).Open();
            var spaces = new byte[1024 * 1024];
            Array.Fill(spaces, (byte)' ');
            for (var i = 0; i < 51; i++) entry.Write(spaces);
        }

        var e = Assert.Throws<StatementFormatException>(() =>
            Reader.Read(new SourceFileContent("download.zip", buffer.ToArray())));

        await Assert.That(e.Message).Contains("50 MB");
    }

    private const string Iban = "CH9300762011623852957";

    private static string Camt(string from, string to, decimal opening, params (string Date, decimal Amount, string Reference)[] entries) =>
        Camt(from, to, opening, entries, Iban);

    /// A minimal camt.053 statement whose closing balance (dated <paramref name="to"/>) is opening + entries.
    private static string Camt(string from, string to, decimal opening, (string Date, decimal Amount, string Reference)[] entries,
        string iban = Iban, string currency = "CHF")
    {
        static string Amount(decimal a) => Math.Abs(a).ToString("0.00", CultureInfo.InvariantCulture);
        static string Indicator(decimal a) => a < 0 ? "DBIT" : "CRDT";
        var closing = opening + entries.Sum(e => e.Amount);
        var ntries = string.Concat(entries.Select(e => $"""
            <Ntry><Amt Ccy="{currency}">{Amount(e.Amount)}</Amt><CdtDbtInd>{Indicator(e.Amount)}</CdtDbtInd><Sts>BOOK</Sts>
              <BookgDt><Dt>{e.Date}</Dt></BookgDt><AcctSvcrRef>{e.Reference}</AcctSvcrRef><AddtlNtryInf>Entry {e.Reference}</AddtlNtryInf></Ntry>
            """));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.04"><BkToCstmrStmt>
              <GrpHdr><MsgId>M</MsgId><CreDtTm>2026-11-20T06:00:00+01:00</CreDtTm></GrpHdr>
              <Stmt><Id>S</Id>
                <FrToDt><FrDtTm>{from}T00:00:00+01:00</FrDtTm><ToDtTm>{to}T23:59:59+01:00</ToDtTm></FrToDt>
                <Acct><Id><IBAN>{iban}</IBAN></Id><Ccy>{currency}</Ccy></Acct>
                <Bal><Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp><Amt Ccy="{currency}">{Amount(opening)}</Amt>
                  <CdtDbtInd>{Indicator(opening)}</CdtDbtInd><Dt><Dt>{from}</Dt></Dt></Bal>
                <Bal><Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp><Amt Ccy="{currency}">{Amount(closing)}</Amt>
                  <CdtDbtInd>{Indicator(closing)}</CdtDbtInd><Dt><Dt>{to}</Dt></Dt></Bal>
                {ntries}
              </Stmt>
            </BkToCstmrStmt></Document>
            """;
    }

    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        return buffer.ToArray();
    }
}
