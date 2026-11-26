using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the postfinance reader (Kontoauszug PDF, German; ADR-0012), Source file content in, parsed statement
/// out. The statements are synthetic PDFs built by <see cref="PostFinanceKontoauszug"/>.
public class PostFinanceTests
{
    private static readonly PostFinanceIntegration Reader = new();

    private static ParsedStatement Read(PostFinanceKontoauszug statement) => Read(statement.Build());

    private static ParsedStatement Read(byte[] bytes) => Reader.Read(new SourceFileContent("kontoauszug.pdf", bytes));

    private static string ErrorOf(PostFinanceKontoauszug statement) => ErrorOf(statement.Build());

    private static string ErrorOf(byte[] bytes) => Assert.Throws<StatementFormatException>(() => Read(bytes)).Message;

    private static DateOnly Oct(int day) => new(2026, 10, day);

    /// A statement over two pages with every kind of row.
    public static PostFinanceKontoauszug October() => new PostFinanceKontoauszug()
        .Opening("30.09.26", "1 000.00")
        .Debit("01.10.26", "100.00", null,
            "LASTSCHRIFT", "CH0000000000000000001", "EXAMPLE INSURANCE AG", "MUSTERSTRASSE 1", "8000 ZÜRICH")
        .Debit(null, "50.50", "849.50",
            "LASTSCHRIFT", "EXAMPLE BANK", "8000 ZÜRICH", "CH0000000000000000002", "ERIKA MUSTER", "SENDER REFERENZ:",
            "20261001000000000000001")
        .Debit("13.10.26", "28.80", "820.70",
            "TWINT KAUF/DIENSTLEISTUNG", "VOM 13.10.2026", "EXAMPLE SHOP /", "ECOM", "ZÜRICH (CH)")
        .NextPage()
        .Debit("23.10.26", "100.00", null,
            "AUFTRAG CH-DD-", "BASISLASTSCHRIFT", "ID-NR. DES", "ZAHLUNGSEMPFÄNGERS:", "41100000000000001",
            "ZAHLUNGSEMPFÄNGER:", "EXAMPLE CHARITY", "TRANSAKTIONS-ID:", "000-000000001")
        .Credit(null, "2 000.00", "2 720.70",
            "GUTSCHRIFT", "CH0000000000000000003", "ABSENDER:", "EXAMPLE EMPLOYER AG", "MITTEILUNGEN:", "SALARY 10/2026")
        .Credit("26.10.26", "196.00", "2 916.70",
            "GUTSCHRIFT", "AUFTRAGGEBER:", "KANTON BEISPIEL", "MITTEILUNGEN:", "RUECKERSTATTUNG")
        .Debit("31.10.26", "5.00", "2 911.70", "PREIS FÜR", "BANKPAKET SMART 09.2026")
        .Total("2 196.00", "284.30")
        .Closing("31.10.26", "2 911.70");

    [Test]
    public async Task It_is_registered_as_postfinance_with_an_identifier_and_one_currency()
    {
        await Assert.That(Reader.Key).IsEqualTo("postfinance");
        await Assert.That(Reader.Name).IsEqualTo("PostFinance");
        await Assert.That(Reader.CanProvideIdentifier).IsTrue();
        await Assert.That(Reader.HoldsSeveralCurrencies).IsFalse();
    }

    [Test]
    public async Task Reads_the_rows_the_iban_and_the_closing_balance()
    {
        var statement = Read(October());

        // The date is carried down to a day's later rows; the description is the text block, line by line; the
        // counterparty is set only where the statement labels it or for a TWINT purchase; no row has a reference.
        await Assert.That(statement.Rows.Take(6)).IsEquivalentTo(new[]
        {
            new StatementRow(Oct(1), -100.00m, "CHF",
                "LASTSCHRIFT\nCH0000000000000000001\nEXAMPLE INSURANCE AG\nMUSTERSTRASSE 1\n8000 ZÜRICH"),
            new StatementRow(Oct(1), -50.50m, "CHF",
                "LASTSCHRIFT\nEXAMPLE BANK\n8000 ZÜRICH\nCH0000000000000000002\nERIKA MUSTER\nSENDER REFERENZ:\n" +
                "20261001000000000000001"),
            new StatementRow(Oct(13), -28.80m, "CHF",
                "TWINT KAUF/DIENSTLEISTUNG\nVOM 13.10.2026\nEXAMPLE SHOP /\nECOM\nZÜRICH (CH)", "EXAMPLE SHOP /"),
            new StatementRow(Oct(23), -100.00m, "CHF",
                "AUFTRAG CH-DD-\nBASISLASTSCHRIFT\nID-NR. DES\nZAHLUNGSEMPFÄNGERS:\n41100000000000001\n" +
                "ZAHLUNGSEMPFÄNGER:\nEXAMPLE CHARITY\nTRANSAKTIONS-ID:\n000-000000001", "EXAMPLE CHARITY"),
            new StatementRow(Oct(23), 2000.00m, "CHF",
                "GUTSCHRIFT\nCH0000000000000000003\nABSENDER:\nEXAMPLE EMPLOYER AG\nMITTEILUNGEN:\nSALARY 10/2026",
                "EXAMPLE EMPLOYER AG"),
            new StatementRow(Oct(26), 196.00m, "CHF",
                "GUTSCHRIFT\nAUFTRAGGEBER:\nKANTON BEISPIEL\nMITTEILUNGEN:\nRUECKERSTATTUNG", "KANTON BEISPIEL"),
        });
        await Assert.That(statement.Rows.Take(6).SelectMany(r => r.SuggestedTags)).IsEmpty();
        await Assert.That(statement.Rows).Count().IsEqualTo(7);
        await Assert.That(statement.AccountIdentifiers).IsEquivalentTo([PostFinanceKontoauszug.Iban]);
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(Oct(31), 2911.70m));
        await Assert.That(statement.BalancesByCurrency).IsEmpty();
        await Assert.That(statement.Notes).IsEmpty();
    }

    [Test]
    public async Task A_price_postfinance_charges_suggests_the_fee_tag()
    {
        var fee = Read(October()).Rows[^1];

        await Assert.That(fee.Date).IsEqualTo(Oct(31));
        await Assert.That(fee.Amount).IsEqualTo(-5.00m);
        await Assert.That(fee.Description).IsEqualTo("PREIS FÜR\nBANKPAKET SMART 09.2026");
        await Assert.That(fee.Counterparty).IsEqualTo("PostFinance");
        await Assert.That(fee.SuggestedTags).IsEquivalentTo(new[] { "fee" });
    }

    [Test]
    public async Task Two_identical_rows_on_one_day_are_both_read()
    {
        var statement = Read(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("13.10.26", "4.50", null, "TWINT KAUF/DIENSTLEISTUNG", "VOM 13.10.2026", "EXAMPLE CAFE")
            .Debit(null, "4.50", "91.00", "TWINT KAUF/DIENSTLEISTUNG", "VOM 13.10.2026", "EXAMPLE CAFE")
            .Total("0.00", "9.00")
            .Closing("31.10.26", "91.00"));

        await Assert.That(statement.Rows).Count().IsEqualTo(2);
        await Assert.That(statement.Rows[0]).IsEqualTo(statement.Rows[1]);
    }

    [Test]
    public async Task A_text_block_that_runs_over_a_page_break_stays_one_row()
    {
        var statement = Read(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("05.10.26", "40.00", "60.00", "LASTSCHRIFT", "EXAMPLE BANK")
            .NextPage()
            .Text("CH0000000000000000002", "ERIKA MUSTER")
            .Total("0.00", "40.00")
            .Closing("31.10.26", "60.00"));

        await Assert.That(statement.Rows.Single().Description)
            .IsEqualTo("LASTSCHRIFT\nEXAMPLE BANK\nCH0000000000000000002\nERIKA MUSTER");
    }

    [Test]
    public async Task The_currency_is_the_one_printed_after_the_iban()
    {
        var statement = Read(new PostFinanceKontoauszug(currency: "EUR")
            .Opening("30.09.26", "100.00")
            .Credit("05.10.26", "1 234 567.89", "1 234 667.89", "GUTSCHRIFT")
            .Total("1 234 567.89", "0.00")
            .Closing("31.10.26", "1 234 667.89"));

        await Assert.That(statement.Rows.Single().Currency).IsEqualTo("EUR");
        await Assert.That(statement.Rows.Single().Amount).IsEqualTo(1234567.89m);
    }

    [Test]
    public async Task A_statement_of_any_period_is_read()
    {
        var statement = Read(new PostFinanceKontoauszug("01.01.2026", "31.12.2026")
            .Opening("31.12.25", "100.00")
            .Debit("15.06.26", "40.00", "60.00", "LASTSCHRIFT")
            .Total("0.00", "40.00")
            .Closing("31.12.26", "60.00"));

        await Assert.That(statement.Rows.Single().Date).IsEqualTo(new DateOnly(2026, 6, 15));
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(new DateOnly(2026, 12, 31), 60.00m));
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_statement_without_movements_has_no_rows_and_keeps_its_closing_balance(bool withTotal)
    {
        var empty = new PostFinanceKontoauszug().Opening("30.09.26", "250.00");
        if (withTotal) empty.Total("0.00", "0.00");
        var statement = Read(empty.Closing("31.10.26", "250.00"));

        await Assert.That(statement.Rows).IsEmpty();
        await Assert.That(statement.ClosingBalance).IsEqualTo(new ClosingBalance(Oct(31), 250.00m));
    }

    // ---- reconciliation ----

    [Test]
    public async Task Rejects_a_statement_whose_rows_dont_end_at_the_closing_kontostand()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("05.10.26", "40.00", null, "LASTSCHRIFT")
            .Total("0.00", "40.00")
            .Closing("31.10.26", "70.00"));

        await Assert.That(error).Contains("end at a balance of 60.00 CHF");
        await Assert.That(error).Contains("closing Kontostand is 70.00 CHF");
        await Assert.That(error).Contains("Nothing was imported");
    }

    [Test]
    public async Task Rejects_a_statement_whose_printed_saldo_doesnt_follow_from_the_rows()
    {
        // The two amounts of 01.10. are swapped between rows of different days: the end still adds up.
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("01.10.26", "10.00", "70.00", "LASTSCHRIFT", "EXAMPLE ONE")
            .Debit("02.10.26", "30.00", "60.00", "LASTSCHRIFT", "EXAMPLE TWO")
            .Total("0.00", "40.00")
            .Closing("31.10.26", "60.00"));

        await Assert.That(error).Contains("\"LASTSCHRIFT\" of 01.10.2026");
        await Assert.That(error).Contains("give a balance of 90.00 CHF");
        await Assert.That(error).Contains("the statement says 70.00 CHF");
    }

    [Test]
    public async Task Rejects_a_statement_whose_amounts_dont_add_up_to_the_total_line()
    {
        // A Gutschrift read as a Lastschrift of the opposite... here: the Total says more money in and out.
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Credit("05.10.26", "20.00", "120.00", "GUTSCHRIFT")
            .Debit("06.10.26", "40.00", "80.00", "LASTSCHRIFT")
            .Total("25.00", "45.00")
            .Closing("31.10.26", "80.00"));

        await Assert.That(error).Contains("Gutschrift amounts add up to 20.00 CHF");
        await Assert.That(error).Contains("Total line says 25.00 CHF");
    }

    [Test]
    public async Task Rejects_a_statement_with_rows_but_no_total_line()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("05.10.26", "40.00", "60.00", "LASTSCHRIFT")
            .Closing("31.10.26", "60.00"));

        await Assert.That(error).Contains("no Total line");
    }

    // ---- lines the reader doesn't know ----

    [Test]
    public async Task Rejects_a_table_line_that_is_neither_a_row_nor_text_naming_page_and_line()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("05.10.26", "40.00", "60.00", "LASTSCHRIFT")
            .NextPage()
            .Raw(text: "Übertrag", saldo: "60.00")
            .Debit("06.10.26", "10.00", "50.00", "LASTSCHRIFT")
            .Total("0.00", "50.00")
            .Closing("31.10.26", "50.00"));

        await Assert.That(error).Contains("neither a row nor part of a row's text");
        await Assert.That(error).Contains("page 2, line 'Übertrag 60.00'");
        await Assert.That(error).Contains("Nothing was imported");
    }

    [Test]
    public async Task Rejects_a_row_with_both_a_gutschrift_and_a_lastschrift()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Raw("05.10.26", "LASTSCHRIFT", credit: "10.00", debit: "10.00", valuta: "05.10.26", saldo: "100.00")
            .Total("10.00", "10.00")
            .Closing("31.10.26", "100.00"));

        await Assert.That(error).Contains("both a Gutschrift and a Lastschrift");
    }

    [Test]
    public async Task Rejects_text_before_the_first_row()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Text("EXAMPLE BANK")
            .Closing("31.10.26", "100.00"));

        await Assert.That(error).Contains("neither a row nor part of a row's text");
    }

    [Test]
    public async Task Rejects_a_statement_without_a_closing_kontostand()
    {
        var error = ErrorOf(new PostFinanceKontoauszug()
            .Opening("30.09.26", "100.00")
            .Debit("05.10.26", "40.00", "60.00", "LASTSCHRIFT")
            .Total("0.00", "40.00"));

        await Assert.That(error).Contains("closing Kontostand");
    }

    // ---- not a Kontoauszug ----

    [Test]
    [Arguments("<?xml version=\"1.0\"?><Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.08\"/>")]
    [Arguments("Datum;Text;Gutschrift;Lastschrift\n")]
    public async Task Rejects_a_file_that_isnt_a_pdf_with_a_hint_to_download_the_kontoauszug(string content)
    {
        var error = ErrorOf(Encoding.UTF8.GetBytes(content));

        await Assert.That(error).Contains("isn't a PDF");
        await Assert.That(error).Contains("Kontoauszug");
        await Assert.That(error).Contains("PostFinance e-finance");
    }

    [Test]
    public async Task Rejects_another_pdf()
    {
        var error = ErrorOf(PostFinanceKontoauszug.OtherPdf("Rechnung", "Kreditkarte", "Total 500.25"));

        await Assert.That(error).Contains("isn't a PostFinance Kontoauszug in German");
    }

    [Test]
    public async Task Rejects_a_statement_in_another_language()
    {
        var error = ErrorOf(new PostFinanceKontoauszug(title: "Extrait de compte")
            .Opening("30.09.26", "100.00")
            .Closing("31.10.26", "100.00"));

        await Assert.That(error).Contains("isn't a PostFinance Kontoauszug in German");
    }

    [Test]
    public async Task Rejects_a_broken_pdf()
    {
        var error = ErrorOf(Encoding.ASCII.GetBytes("%PDF-1.4\nnot really a pdf"));

        await Assert.That(error).Contains("couldn't be opened");
    }
}
