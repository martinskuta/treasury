using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the revolut reader (account statement CSV, English), Source file content in, parsed statement out
/// (samples in Revolut/).
public class RevolutTests
{
    private static readonly RevolutIntegration Reader = new();

    private const string OctoberSample = "account-statement_2026-10-01_2026-10-31_en-us_6a51a3.csv";
    private const string NovemberSample = "account-statement_2026-11-01_2026-11-19_en-us_affca4.csv";

    private static ParsedStatement ReadSample(string name) => Reader.Read(new SourceFileContent(name,
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Revolut", name))));

    [Test]
    public async Task It_is_registered_as_revolut_without_an_identifier_and_with_several_currencies()
    {
        await Assert.That(Reader.Key).IsEqualTo("revolut");
        await Assert.That(Reader.Name).IsEqualTo("Revolut");
        await Assert.That(Reader.CanProvideIdentifier).IsFalse();
        await Assert.That(Reader.HoldsSeveralCurrencies).IsTrue();
    }

    [Test]
    public async Task Reads_the_october_sample()
    {
        var statement = ReadSample(OctoberSample);

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 19), -31.40m, "CHF", "Playtomic", "Playtomic"),
            new StatementRow(new DateOnly(2026, 10, 25), -18.90m, "CHF", "Playtomic", "Playtomic"),
        });
        await Assert.That(statement.AccountIdentifier).IsNull();
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", new DateOnly(2026, 10, 19), 2534.73m,
                new ClosingBalance(new DateOnly(2026, 10, 25), 2484.43m)),
        });
    }

    [Test]
    public async Task Reads_the_november_sample_continuing_october_and_skipping_the_pending_payment()
    {
        var statement = ReadSample(NovemberSample);

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 4), 50.00m, "CHF", "Revolut Bank UAB", "Revolut Bank UAB"),
            new StatementRow(new DateOnly(2026, 11, 5), -42.80m, "CHF", "ÖMI'S", "ÖMI'S"),
            new StatementRow(new DateOnly(2026, 11, 5), -129.95m, "CHF", "Tmnábytek.cz", "Tmnábytek.cz"),
            new StatementRow(new DateOnly(2026, 11, 9), -1500.00m, "CHF", "To Jan Novak", "Jan Novak"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            // October closed at 2484.43, so November starts where October ended.
            new CurrencyBalances("CHF", new DateOnly(2026, 11, 4), 2484.43m,
                new ClosingBalance(new DateOnly(2026, 11, 9), 861.68m)),
        });
    }

    [Test]
    public async Task Only_completed_rows_of_the_current_product_are_read()
    {
        var statement = ReadSample("states-and-deposit.csv");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 1), 500.00m, "CHF", "Top-up by *1234"),
            new StatementRow(new DateOnly(2026, 11, 4), -200.00m, "CHF", "To pocket CHF Savings", "pocket CHF Savings"),
            new StatementRow(new DateOnly(2026, 11, 6), 15.00m, "CHF", "Coop", "Coop"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", new DateOnly(2026, 11, 1), 1000.00m,
                new ClosingBalance(new DateOnly(2026, 11, 6), 1315.00m)),
        });
    }

    [Test]
    public async Task A_fee_becomes_a_second_row_charged_by_revolut()
    {
        var statement = ReadSample("fee.csv");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 6), -200.00m, "CHF", "Cash at Bahnhofstrasse"),
            new StatementRow(new DateOnly(2026, 11, 6), -3.50m, "CHF", "Fee: Cash at Bahnhofstrasse", "Revolut"),
            new StatementRow(new DateOnly(2026, 11, 8), -30.00m, "CHF", "Amazon", "Amazon"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", new DateOnly(2026, 11, 6), 1000.00m,
                new ClosingBalance(new DateOnly(2026, 11, 8), 766.50m)),
        });
    }

    [Test]
    public async Task Each_currency_gets_its_own_balances_in_completed_date_order()
    {
        var statement = ReadSample("two-currencies.csv");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 11), -25.00m, "EUR", "Trattoria Roma, Milano", "Trattoria Roma, Milano"),
            new StatementRow(new DateOnly(2026, 11, 10), -100.00m, "CHF", "Exchanged to EUR"),
            new StatementRow(new DateOnly(2026, 11, 10), 92.00m, "EUR", "Exchanged to EUR"),
            new StatementRow(new DateOnly(2026, 11, 13), -10.00m, "CHF", "Migros", "Migros"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", new DateOnly(2026, 11, 10), 1000.00m,
                new ClosingBalance(new DateOnly(2026, 11, 13), 890.00m)),
            new CurrencyBalances("EUR", new DateOnly(2026, 11, 10), 0m,
                new ClosingBalance(new DateOnly(2026, 11, 11), 67.00m)),
        });
        await Assert.That(statement.ClosingBalance).IsNull();
    }

    [Test]
    public async Task Older_upper_case_types_and_states_are_read_like_title_case()
    {
        var statement = ReadSample("upper-case.csv");

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 11, 1), 100.00m, "CHF", "Top-Up by *1234"),
            new StatementRow(new DateOnly(2026, 11, 3), -20.00m, "CHF", "Migros", "Migros"),
            new StatementRow(new DateOnly(2026, 11, 4), 5.00m, "CHF", "Migros", "Migros"),
            new StatementRow(new DateOnly(2026, 11, 5), 50.00m, "CHF", "From Eva Muster", "Eva Muster"),
        });
    }

    [Test]
    public async Task A_header_with_a_byte_order_mark_is_fine()
    {
        var statement = Read("﻿" + Header + "\nCard Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,580.00\n");

        await Assert.That(statement.Rows).Count().IsEqualTo(1);
    }

    [Test]
    public async Task A_non_english_header_asks_for_an_english_export()
    {
        var error = Assert.Throws<StatementFormatException>(() => ReadSample("german-header.csv"));

        await Assert.That(error.Message).Contains("export the statement in English");
    }

    [Test]
    public async Task A_line_with_a_wrong_number_of_fields_is_rejected_with_its_line_number()
    {
        var error = Assert.Throws<StatementFormatException>(() => ReadSample("malformed.csv"));

        await Assert.That(error.Message).StartsWith("line 3: expected 10 fields");
    }

    [Test]
    [Arguments("2026-11-03,Migros,-20.00,0.00,CHF,COMPLETED,580.00", "'2026-11-03' is not a Completed Date")]
    [Arguments("2026-11-03 08:00:00,Migros,-20,00,0.00,CHF,COMPLETED,580.00", "expected 10 fields")]
    [Arguments("2026-11-03 08:00:00,Migros,twenty,0.00,CHF,COMPLETED,580.00", "'twenty' is not an Amount")]
    [Arguments("2026-11-03 08:00:00,Migros,-20.00,,CHF,COMPLETED,580.00", "'' is not a Fee")]
    [Arguments("2026-11-03 08:00:00,Migros,-20.00,0.00,,COMPLETED,580.00", "the Currency is empty")]
    [Arguments("2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,", "'' is not a Balance")]
    public async Task A_malformed_completed_row_is_rejected_with_its_line_number(string tail, string expected)
    {
        var error = Assert.Throws<StatementFormatException>(() =>
            Read($"{Header}\n\nCard Payment,Current,2026-11-02 12:00:00,{tail}\n"));

        await Assert.That(error.Message).StartsWith("line 3: ");
        await Assert.That(error.Message).Contains(expected);
    }

    [Test]
    public async Task An_empty_file_is_rejected()
    {
        var error = Assert.Throws<StatementFormatException>(() => Read("\n"));

        await Assert.That(error.Message).Contains("empty");
    }

    private const string Header = "Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance";

    private static ParsedStatement Read(string content) =>
        Reader.Read(new SourceFileContent("statement.csv", Encoding.UTF8.GetBytes(content)));
}
