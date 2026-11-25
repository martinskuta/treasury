using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the generic-csv reader, Source file content in, parsed statement out (samples in GenericCsv/).
public class GenericCsvTests
{
    private static readonly GenericCsvIntegration Reader = new();

    private static ParsedStatement ReadSample(string name) =>
        Reader.Read(new SourceFileContent(name, File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "GenericCsv", name))));

    private static ParsedStatement ReadText(string text) =>
        Reader.Read(new SourceFileContent("x.csv", Encoding.UTF8.GetBytes(text)));

    [Test]
    public async Task It_is_registered_as_generic_csv_without_an_identifier()
    {
        await Assert.That(Reader.Key).IsEqualTo("generic-csv");
        await Assert.That(Reader.CanProvideIdentifier).IsFalse();
    }

    [Test]
    public async Task Reads_every_column_of_the_full_sample()
    {
        var statement = ReadSample("full.csv");

        await Assert.That(statement.AccountIdentifiers).IsEmpty();
        await Assert.That(statement.ClosingBalance).IsNull();
        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 2), -12.50m, null, "COOP-1234 ZURICH HB", "Coop", "TX-0001"),
            new StatementRow(new DateOnly(2026, 11, 3), 5000m, null, "SALARY NOVEMBER, ACME AG", "ACME AG", "TX-0002"),
            new StatementRow(new DateOnly(2026, 11, 5), -46.20m, null, "TRATTORIA \"DA MARIO\" ROMA", "Trattoria da Mario",
                "TX-0003", -48.00m, "EUR"),
            new StatementRow(new DateOnly(2026, 11, 6), -1250m, null, "RENT DECEMBER"),
        });
    }

    [Test]
    public async Task Reads_semicolons_swiss_dates_and_columns_in_any_order_and_case()
    {
        var statement = ReadSample("minimal-semicolon.csv");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 18), -4.80m, null, "Bakery"),
            new StatementRow(new DateOnly(2026, 11, 19), 20m, null, "Refund"),
        });
    }

    [Test]
    public async Task An_optional_currency_column_is_passed_on()
    {
        var statement = ReadText("date,amount,currency,description\n2026-11-18,-3.20,EUR,Coffee\n");

        await Assert.That(statement.Rows.Single().Currency).IsEqualTo("EUR");
    }

    [Test]
    public async Task A_byte_order_mark_and_crlf_line_endings_are_fine()
    {
        var statement = ReadText("﻿date,amount,description\r\n2026-11-18,-3.20,Coffee\r\n");

        await Assert.That(statement.Rows.Single().Description).IsEqualTo("Coffee");
    }

    [Test]
    [Arguments("date,description\n2026-11-18,Coffee\n", "amount")]
    [Arguments("", "header")]
    [Arguments("date,amount,description\n18/11/2026,-3.20,Coffee\n", "line 2")]
    [Arguments("date,amount,description\n2026-11-18,-3,20,Coffee\n", "line 2")]
    [Arguments("date,amount,description\n2026-11-18,abc,Coffee\n", "line 2")]
    [Arguments("date,amount,description\n2026-11-18,-3.20,\"Coffee\n", "line 2")]
    public async Task Unreadable_files_are_rejected_with_a_helpful_message(string content, string mentions)
    {
        var e = Assert.Throws<StatementFormatException>(() => ReadText(content));

        await Assert.That(e.Message).Contains(mentions);
    }
}
