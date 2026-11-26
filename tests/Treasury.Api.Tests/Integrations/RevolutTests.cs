using System.Text;
using Treasury.Api.Integrations;

namespace Treasury.Api.Tests.Integrations;

/// Seam 2: the revolut reader (Custom statement PDF, English; ADR-0010), Source file content in, parsed statement
/// out. The statements are synthetic PDFs built by <see cref="RevolutCustomStatement"/>.
public class RevolutTests
{
    private static readonly RevolutIntegration Reader = new();

    private static ParsedStatement Read(RevolutCustomStatement statement) =>
        Reader.Read(new SourceFileContent("custom-statement.pdf", statement.Build()));

    private static string ErrorOf(RevolutCustomStatement statement) =>
        Assert.Throws<StatementFormatException>(() => Read(statement)).Message;

    private static DateOnly Nov(int day) => new(2026, 11, day);

    [Test]
    public async Task It_is_registered_as_revolut_with_an_identifier_and_with_several_currencies()
    {
        await Assert.That(Reader.Key).IsEqualTo("revolut");
        await Assert.That(Reader.Name).IsEqualTo("Revolut");
        await Assert.That(Reader.CanProvideIdentifier).IsTrue();
        await Assert.That(Reader.HoldsSeveralCurrencies).IsTrue();
    }

    [Test]
    public async Task Reads_the_rows_and_balances_of_every_currency()
    {
        var statement = Read(new RevolutCustomStatement("Nov 1, 2026 - Nov 20, 2026")
            .Account("CHF", "1,000.00 CHF", "935.00 CHF", t => t
                .Row("Nov 3, 2026", "Top-up by card", "Others", "25.00 CHF", "1,025.00\nCHF")
                .Row("Nov 6, 2026", "Migros", "Merchant", "-60.00 CHF", "965.00\nCHF")
                .Row("Nov 10, 2026", "To Jan Novak", "Others", "-30.00 CHF", "935.00\nCHF")
                .Total("-65.00 CHF"))
            .Account("USD", "$120.00", "$108.00", t => t
                .Row("Nov 12, 2026", "Acme Hosting", "Merchant", "-$12.00", "$108.00")
                .Total("-$12.00"), zero: "$0.00")
            .Account("EUR", "€5.00", "€159.50", t => t
                .Row("Nov 2, 2026", "Payment from Acme, Inc.", "Top up", "€200.00", "€205.00")
                .Row("Nov 11, 2026", "Café Central", "Merchant", "-€45.50", "€159.50")
                .Total("€154.50"), zero: "€0.00"));

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(Nov(3), 25.00m, "CHF", "Top-up by card"),
            new StatementRow(Nov(6), -60.00m, "CHF", "Migros", "Migros"),
            new StatementRow(Nov(10), -30.00m, "CHF", "To Jan Novak", "Jan Novak"),
            new StatementRow(Nov(12), -12.00m, "USD", "Acme Hosting", "Acme Hosting"),
            new StatementRow(Nov(2), 200.00m, "EUR", "Payment from Acme, Inc."),
            new StatementRow(Nov(11), -45.50m, "EUR", "Café Central", "Café Central"),
        });
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            // Opened on the period's first day, closed on its last day.
            new CurrencyBalances("CHF", Nov(1), 1000.00m, new ClosingBalance(Nov(20), 935.00m)),
            new CurrencyBalances("EUR", Nov(1), 5.00m, new ClosingBalance(Nov(20), 159.50m)),
            new CurrencyBalances("USD", Nov(1), 120.00m, new ClosingBalance(Nov(20), 108.00m)),
        });
        await Assert.That(statement.AccountIdentifiers).IsEquivalentTo([RevolutCustomStatement.Iban]);
        await Assert.That(statement.ClosingBalance).IsNull();
        await Assert.That(statement.Notes).IsEmpty();
    }

    [Test]
    public async Task A_currency_without_rows_has_balances_whether_it_holds_money_or_not()
    {
        var statement = Read(new RevolutCustomStatement("Oct 1, 2026 - Oct 31, 2026")
            .Account("CHF", "500.00 CHF", "480.00 CHF", t => t
                .Row("Oct 12, 2026", "Padel Club", "Merchant", "-20.00 CHF", "480.00 CHF")
                .Total("-20.00 CHF"))
            .Account("GBP", "£15.40", "£15.40", zero: "£0.00")
            .Account("ISK", "6,400 ISK", "6,400 ISK", iban: null, zero: "0 ISK")
            .Account("AUD", "AU$0.00", "AU$0.00", zero: "AU$0.00")
            .Account("CZK", "0.00 CZK", "0.00 CZK")
            .Account("JPY", "¥0", "¥0", zero: "¥0"));

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(new DateOnly(2026, 10, 12), -20.00m, "CHF", "Padel Club", "Padel Club"),
        });
        var opened = new DateOnly(2026, 10, 1);
        var closed = new DateOnly(2026, 10, 31);
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", opened, 500.00m, new ClosingBalance(closed, 480.00m)),
            new CurrencyBalances("GBP", opened, 15.40m, new ClosingBalance(closed, 15.40m)),
            new CurrencyBalances("ISK", opened, 6400m, new ClosingBalance(closed, 6400m)),
            // Whether an empty currency gets an Account is the import's call, not the reader's.
            new CurrencyBalances("AUD", opened, 0m, new ClosingBalance(closed, 0m)),
            new CurrencyBalances("CZK", opened, 0m, new ClosingBalance(closed, 0m)),
            new CurrencyBalances("JPY", opened, 0m, new ClosingBalance(closed, 0m)),
        });
    }

    [Test]
    public async Task A_statement_without_any_row_is_read_with_its_balances()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "1,234.50 CHF", "1,234.50 CHF")
            .Account("USD", "$0.00", "$0.00", zero: "$0.00"));

        await Assert.That(statement.Rows).IsEmpty();
        await Assert.That(statement.BalancesByCurrency).IsEquivalentTo(new[]
        {
            new CurrencyBalances("CHF", Nov(1), 1234.50m, new ClosingBalance(Nov(30), 1234.50m)),
            new CurrencyBalances("USD", Nov(1), 0m, new ClosingBalance(Nov(30), 0m)),
        });
    }

    [Test]
    public async Task Amounts_are_read_with_symbols_or_codes_thousands_separators_and_no_decimals()
    {
        var statement = Read(new RevolutCustomStatement()
            // A long amount wraps its code onto the next line.
            .Account("CHF", "30,250.75 CHF", "18,230.75 CHF", t => t
                .Row("Nov 10, 2026", "To Jan Novak", "Others", "-12,000.00\nCHF", "18,250.75\nCHF")
                .Row("Nov 10, 2026", "Kiosk", "Merchant", "-20.00 CHF", "18,230.75\nCHF")
                .Total("-12,020.00\nCHF"))
            .Account("AUD", "AU$10.00", "AU$60.50", t => t
                .Row("Nov 2, 2026", "From Eva Muster", "Others", "AU$50.50", "AU$60.50")
                .Total("AU$50.50"), zero: "AU$0.00")
            .Account("GBP", "£15.40", "£2.90", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-£12.50", "£2.90")
                .Total("-£12.50"), zero: "£0.00")
            .Account("JPY", "¥120,000", "¥117,500", t => t
                .Row("Nov 4, 2026", "Ramen Bar", "Merchant", "-¥2,500", "¥117,500")
                .Total("-¥2,500"), zero: "¥0")
            .Account("ISK", "6,400 ISK", "4,900 ISK", t => t
                .Row("Nov 5, 2026", "Sundlaug", "Merchant", "-1,500 ISK", "4,900 ISK")
                .Total("-1,500 ISK"), iban: null, zero: "0 ISK")
            .Account("CZK", "0.00 CZK", "12,345.60 CZK", t => t
                .Row("Nov 6, 2026", "Exchanged to CZK", "Exchange", "12,345.60 CZK", "12,345.60 CZK")
                .Total("12,345.60 CZK")));

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(Nov(10), -12000.00m, "CHF", "To Jan Novak", "Jan Novak"),
            new StatementRow(Nov(10), -20.00m, "CHF", "Kiosk", "Kiosk"),
            new StatementRow(Nov(2), 50.50m, "AUD", "From Eva Muster", "Eva Muster"),
            new StatementRow(Nov(3), -12.50m, "GBP", "Bakery", "Bakery"),
            new StatementRow(Nov(4), -2500m, "JPY", "Ramen Bar", "Ramen Bar"),
            new StatementRow(Nov(5), -1500m, "ISK", "Sundlaug", "Sundlaug"),
            new StatementRow(Nov(6), 12345.60m, "CZK", "Exchanged to CZK"),
        });
        await Assert.That(statement.BalancesByCurrency.Select(b => (b.Currency, b.OpeningBalance, b.Closing.Amount)))
            .IsEquivalentTo([
                ("AUD", 10.00m, 60.50m), ("CHF", 30250.75m, 18230.75m), ("CZK", 0m, 12345.60m),
                ("GBP", 15.40m, 2.90m), ("ISK", 6400m, 4900m), ("JPY", 120000m, 117500m),
            ]);
    }

    [Test]
    public async Task A_description_wrapped_over_several_lines_is_read_as_one()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("EUR", "€100.00", "€40.00", t => t
                .Row("Nov 3, 2026", "Trattoria Roma Centro\nStorico Di Milano\nNavigli", "Merchant", "-€25.00", "€75.00")
                .Row("Nov 5, 2026", "To Maximilian Alexander\nMustermann", "Others", "-€35.00", "€40.00")
                .Total("-€60.00"), zero: "€0.00"));

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(Nov(3), -25.00m, "EUR", "Trattoria Roma Centro Storico Di Milano Navigli",
                "Trattoria Roma Centro Storico Di Milano Navigli"),
            new StatementRow(Nov(5), -35.00m, "EUR", "To Maximilian Alexander Mustermann", "Maximilian Alexander Mustermann"),
        });
    }

    [Test]
    public async Task Two_identical_payments_on_one_day_are_both_read()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "20.00 CHF", "11.00 CHF", t => t
                .Row("Nov 3, 2026", "Coffee Corner", "Merchant", "-4.50 CHF", "15.50 CHF")
                .Row("Nov 3, 2026", "Coffee Corner", "Merchant", "-4.50 CHF", "11.00 CHF")
                .Total("-9.00 CHF")));

        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(Nov(3), -4.50m, "CHF", "Coffee Corner", "Coffee Corner"),
            new StatementRow(Nov(3), -4.50m, "CHF", "Coffee Corner", "Coffee Corner"),
        });
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_table_running_over_two_pages_is_read_completely(bool headerRepeated)
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "60.00 CHF", t => t
                .Row("Nov 2, 2026", "Bakery", "Merchant", "-10.00 CHF", "90.00 CHF")
                .Row("Nov 3, 2026", "Kiosk", "Merchant", "-10.00 CHF", "80.00 CHF")
                .NextPage(headerRepeated)
                .Row("Nov 4, 2026", "Florist", "Merchant", "-10.00 CHF", "70.00 CHF")
                .Row("Nov 5, 2026", "Cinema", "Merchant", "-10.00 CHF", "60.00 CHF")
                .Total("-40.00 CHF"))
            .Account("EUR", "€5.00", "€5.00", zero: "€0.00"));

        await Assert.That(statement.Rows.Select(r => (r.Date, r.Description, r.Amount))).IsEquivalentTo([
            (Nov(2), "Bakery", -10.00m), (Nov(3), "Kiosk", -10.00m), (Nov(4), "Florist", -10.00m), (Nov(5), "Cinema", -10.00m),
        ]);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_table_continued_on_a_page_that_repeats_the_account_heading_is_read_as_one(bool headerRepeated)
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "60.00 CHF", t => t
                .Row("Nov 2, 2026", "Bakery", "Merchant", "-10.00 CHF", "90.00 CHF")
                .Row("Nov 3, 2026", "Kiosk", "Merchant", "-10.00 CHF", "80.00 CHF")
                .NextPage(headerRepeated, repeatAccountHeading: true)
                .Row("Nov 4, 2026", "Florist", "Merchant", "-10.00 CHF", "70.00 CHF")
                .Row("Nov 5, 2026", "Cinema", "Merchant", "-10.00 CHF", "60.00 CHF")
                .Total("-40.00 CHF"))
            .Account("EUR", "€5.00", "€5.00", zero: "€0.00"));

        await Assert.That(statement.Rows.Select(r => (r.Date, r.Description, r.Amount))).IsEquivalentTo([
            (Nov(2), "Bakery", -10.00m), (Nov(3), "Kiosk", -10.00m), (Nov(4), "Florist", -10.00m), (Nov(5), "Cinema", -10.00m),
        ]);
        await Assert.That(statement.BalancesByCurrency.Select(b => (b.Currency, b.Closing.Amount)))
            .IsEquivalentTo([("CHF", 60.00m), ("EUR", 5.00m)]);
    }

    [Test]
    public async Task A_description_wrapped_onto_the_page_that_continues_the_table_is_read_as_one()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("EUR", "€100.00", "€40.00", t => t
                .Row("Nov 3, 2026", "Trattoria Roma Centro", "Merchant", "-€25.00", "€75.00")
                .NextPage(repeatAccountHeading: true)
                .DescriptionContinued("Storico Di Milano")
                .Row("Nov 5, 2026", "To Jan Novak", "Others", "-€35.00", "€40.00")
                .Total("-€60.00"), zero: "€0.00"));

        await Assert.That(statement.Rows.Select(r => (r.Date, r.Description, r.Amount))).IsEquivalentTo([
            (Nov(3), "Trattoria Roma Centro Storico Di Milano", -25.00m), (Nov(5), "To Jan Novak", -35.00m),
        ]);
    }

    [Test]
    public async Task A_fee_is_taken_out_of_the_amount_into_a_second_row_charged_by_revolut()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "1,000.00 CHF", "766.50 CHF", t => t
                .Row("Nov 6, 2026", "Cash at Bahnhofstrasse", "Cash", "-203.50 CHF", "796.50 CHF", fees: "3.50 CHF")
                .Row("Nov 8, 2026", "Bookshop", "Merchant", "-30.00 CHF", "766.50 CHF")
                .Total("-233.50 CHF", fees: "3.50 CHF")));

        await Assert.That(statement.Rows.Select(r => (r.Date, r.Amount, r.Currency ?? "", r.Description, r.Counterparty ?? "")))
            .IsEquivalentTo([
                (Nov(6), -200.00m, "CHF", "Cash at Bahnhofstrasse", ""),
                (Nov(6), -3.50m, "CHF", "Fee: Cash at Bahnhofstrasse", "Revolut"),
                (Nov(8), -30.00m, "CHF", "Bookshop", "Bookshop"),
            ]);
    }

    [Test]
    public async Task The_fee_taken_out_of_a_row_suggests_the_tag_fee_and_the_other_rows_suggest_none()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "1,000.00 CHF", "766.50 CHF", t => t
                .Row("Nov 6, 2026", "Cash at Bahnhofstrasse", "Cash", "-203.50 CHF", "796.50 CHF", fees: "3.50 CHF")
                .Row("Nov 8, 2026", "Bookshop", "Merchant", "-30.00 CHF", "766.50 CHF")
                .Total("-233.50 CHF", fees: "3.50 CHF")));

        await Assert.That(statement.Rows.Select(r => (r.Description, string.Join(",", r.SuggestedTags)))).IsEquivalentTo([
            ("Cash at Bahnhofstrasse", ""), ("Fee: Cash at Bahnhofstrasse", "fee"), ("Bookshop", ""),
        ]);
    }

    [Test]
    public async Task A_row_revolut_categorises_as_fees_is_charged_by_revolut()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "50.00 CHF", "43.00 CHF", t => t
                .Row("Nov 6, 2026", "Card Delivery Fee", "Fees", "-7.00 CHF", "43.00 CHF")
                .Total("-7.00 CHF")));

        await Assert.That(statement.Rows.Select(r => (r.Date, r.Amount, r.Currency ?? "", r.Description, r.Counterparty ?? "")))
            .IsEquivalentTo([(Nov(6), -7.00m, "CHF", "Card Delivery Fee", "Revolut")]);
    }

    [Test]
    public async Task A_row_revolut_categorises_as_fees_suggests_the_tag_fee()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "50.00 CHF", "33.00 CHF", t => t
                .Row("Nov 6, 2026", "Card Delivery Fee", "Fees", "-7.00 CHF", "43.00 CHF")
                // "Fee" in the description alone doesn't make a row a fee: Revolut's Category does.
                .Row("Nov 7, 2026", "Entrance Fee Museum", "Merchant", "-10.00 CHF", "33.00 CHF")
                .Total("-17.00 CHF")));

        await Assert.That(statement.Rows.Select(r => (r.Description, string.Join(",", r.SuggestedTags)))).IsEquivalentTo([
            ("Card Delivery Fee", "fee"), ("Entrance Fee Museum", ""),
        ]);
    }

    [Test]
    public async Task A_fee_in_an_account_printing_symbols_is_told_apart_from_other_taxes()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("AUD", "AU$90.00", "AU$67.75", t => t
                .Row("Nov 6, 2026", "Cash at George Street", "Cash", "-AU$22.25", "AU$67.75", fees: "AU$2.25")
                .Total("-AU$22.25", fees: "AU$2.25"), zero: "AU$0.00"));

        await Assert.That(statement.Rows.Select(r => (r.Description, r.Amount))).IsEquivalentTo([
            ("Cash at George Street", -20.00m), ("Fee: Cash at George Street", -2.25m),
        ]);
    }

    [Test]
    public async Task A_row_whose_balance_does_not_follow_from_the_previous_one_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Account("EUR", "€100.00", "€60.00", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-€10.00", "€90.00")
                .Row("Nov 4, 2026", "Florist", "Merchant", "-€25.00", "€60.00")
                .Total("-€35.00"), zero: "€0.00"));

        await Assert.That(error).Contains("EUR").And.Contains("Florist").And.Contains("04.11.2026")
            .And.Contains("90.00 EUR").And.Contains("-25.00 EUR").And.Contains("65.00 EUR").And.Contains("60.00 EUR")
            .And.Contains("Nothing was imported");
    }

    [Test]
    public async Task A_printed_opening_balance_the_first_row_does_not_follow_from_is_passed_on_with_the_rows_start()
    {
        // Revolut printed 0.00 HRK, but the currency held 6'946.77 HRK: the rows agree with everything else.
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Account("HRK", "0.00 HRK", "4,617.57 HRK", t => t
                .Row("Nov 3, 2026", "Swiss Luxury Apartments", "Merchant", "-1,170.99 HRK", "5,775.78 HRK")
                .Row("Nov 4, 2026", "Zalando", "Merchant", "-1,158.21 HRK", "4,617.57 HRK")
                .Total("-2,329.20 HRK"), zero: "0.00 HRK"));

        var kuna = statement.BalancesByCurrency.Single(b => b.Currency == "HRK");
        await Assert.That((kuna.OpeningBalance, kuna.PrintedOpeningBalance)).IsEqualTo((6946.77m, (decimal?)0m));
        await Assert.That(statement.BalancesByCurrency.Single(b => b.Currency == "CHF").PrintedOpeningBalance).IsNull();
        await Assert.That(statement.Rows.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Rows_that_do_not_end_at_the_closing_balance_reject_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("USD", "$100.00", "$80.00", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-$12.00", "$88.00")
                .Total("-$12.00"), zero: "$0.00"));

        await Assert.That(error).Contains("USD rows end at a balance of 88.00 USD").And.Contains("closing balance is 80.00 USD");
    }

    [Test]
    public async Task A_currency_without_rows_whose_balance_changed_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("GBP", "£1,015.40", "£20.00", zero: "£0.00"));

        await Assert.That(error).Contains("GBP account has no rows").And.Contains("1'015.40 GBP").And.Contains("20.00 GBP");
    }

    [Test]
    public async Task Rows_that_do_not_add_up_to_the_total_row_reject_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "80.00 CHF", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-10.00 CHF", "90.00 CHF")
                .Row("Nov 4, 2026", "Florist", "Merchant", "-10.00 CHF", "80.00 CHF")
                .Total("-35.00 CHF")));

        await Assert.That(error).Contains("CHF rows add up to -20.00 CHF").And.Contains("Total row says -35.00 CHF");
    }

    [Test]
    public async Task Fees_that_do_not_add_up_to_the_total_row_reject_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "1,000.00 CHF", "766.50 CHF", t => t
                .Row("Nov 6, 2026", "Cash at Bahnhofstrasse", "Cash", "-203.50 CHF", "796.50 CHF", fees: "3.50 CHF")
                .Row("Nov 8, 2026", "Bookshop", "Merchant", "-30.00 CHF", "766.50 CHF")
                .Total("-233.50 CHF", fees: "5.00 CHF")));

        await Assert.That(error).Contains("CHF rows' fees add up to 3.50 CHF").And.Contains("Total row says 5.00 CHF");
    }

    [Test]
    public async Task A_table_without_a_total_row_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "90.00 CHF", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-10.00 CHF", "90.00 CHF")
                .NoTotal()));

        await Assert.That(error).Contains("CHF transaction statement has no Total row");
    }

    [Test]
    public async Task Tax_withheld_in_a_row_rejects_the_file_as_not_supported_yet()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("EUR", "€100.00", "€101.00", t => t
                .Row("Nov 3, 2026", "Interest", "Others", "€1.00", "€101.00", tax: "€0.15")
                .Total("€1.00"), zero: "€0.00"));

        await Assert.That(error).Contains("0.15 EUR").And.Contains("\"Tax withheld\"").And.Contains("03.11.2026")
            .And.Contains("doesn't support yet");
    }

    [Test]
    public async Task Other_taxes_in_the_total_row_reject_the_file_as_not_supported_yet()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "101.00 CHF", t => t
                .Row("Nov 3, 2026", "Interest", "Others", "1.00 CHF", "101.00 CHF")
                .Total("1.00 CHF", otherTaxes: "0.35 CHF")));

        await Assert.That(error).Contains("0.35 CHF").And.Contains("\"Other taxes\"").And.Contains("Total row")
            .And.Contains("doesn't support yet");
    }

    [Test]
    public async Task An_account_missing_from_the_transaction_statements_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Account("EUR", "€5.00", "€5.00", t => t.OnlyInSummaries(), zero: "€0.00"));

        await Assert.That(error).Contains("EUR account is in the statement's summaries, but it has no transaction statement");
    }

    [Test]
    public async Task An_account_missing_from_the_summaries_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Account("EUR", "€5.00", "€5.00", t => t.OnlyInTransactionStatements(), zero: "€0.00"));

        await Assert.That(error).Contains("EUR account has a transaction statement, but it is not in the statement's summaries");
    }

    [Test]
    public async Task The_same_currency_twice_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("EUR", "€5.00", "€5.00", zero: "€0.00")
            .Account("EUR", "€7.00", "€7.00", zero: "€0.00"));

        await Assert.That(error).Contains("lists the EUR account twice");
    }

    [Test]
    public async Task A_second_transaction_statement_for_a_currency_after_its_total_row_rejects_the_file()
    {
        var error = ErrorOf(new RevolutCustomStatement()
            .Account("EUR", "€5.00", "€5.00", zero: "€0.00")
            .Account("EUR", "€5.00", "€5.00", t => t.OnlyInTransactionStatements(), zero: "€0.00"));

        await Assert.That(error).Contains("lists the EUR account twice");
    }

    [Test]
    public async Task Every_iban_the_personal_accounts_show_is_an_identifier_once()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Account("EUR", "€5.00", "€5.00", iban: "LT601010012345678901", zero: "€0.00")
            .Account("USD", "$0.00", "$0.00", zero: "$0.00"));

        await Assert.That(statement.AccountIdentifiers)
            .IsEquivalentTo([RevolutCustomStatement.Iban, "LT601010012345678901"]);
    }

    [Test]
    public async Task A_period_over_which_revolut_moved_the_login_to_a_new_iban_shows_the_old_and_the_new_one()
    {
        const string gbIban = "GB33BUKB20201555555555";
        var statement = Read(new RevolutCustomStatement("Mar 1, 2018 - Oct 31, 2026")
            .Account("CHF", "0.00 CHF", "100.00 CHF", t => t
                .Row("Mar 21, 2018", "Top-up by *0315", "Top up", "100.00 CHF", "100.00 CHF")
                .Total("100.00 CHF")
                .FormerGbIban(gbIban))
            .Account("GBP", "£0.00", "£0.00", t => t.GbAccountNumber("12345678").FormerGbIban(gbIban), zero: "£0.00")
            // A currency closed before the move shows only the old IBAN.
            .Account("HRK", "0.00 HRK", "0.00 HRK", iban: gbIban));

        await Assert.That(statement.AccountIdentifiers).IsEquivalentTo([RevolutCustomStatement.Iban, gbIban]);
        await Assert.That(statement.Rows).Count().IsEqualTo(1);
        await Assert.That(statement.BalancesByCurrency.Select(b => (b.Currency, b.Closing.Amount)))
            .IsEquivalentTo([("CHF", 100.00m), ("GBP", 0m), ("HRK", 0m)]);
    }

    [Test]
    public async Task Accounts_without_an_iban_and_a_british_account_number_add_no_identifier()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("ISK", "6,400 ISK", "6,400 ISK", iban: null, zero: "0 ISK")
            .Account("GBP", "£15.40", "£15.40", t => t.GbAccountNumber("12345678"), zero: "£0.00")
            .Account("CHF", "100.00 CHF", "100.00 CHF"));

        await Assert.That(statement.AccountIdentifiers).IsEquivalentTo([RevolutCustomStatement.Iban]);
        await Assert.That(statement.BalancesByCurrency.Select(b => b.Currency)).IsEquivalentTo(["CHF", "GBP", "ISK"]);
    }

    [Test]
    public async Task A_statement_whose_accounts_show_no_iban_has_no_identifier()
    {
        var statement = Read(new RevolutCustomStatement().Account("ISK", "6,400 ISK", "6,400 ISK", iban: null, zero: "0 ISK"));

        await Assert.That(statement.AccountIdentifiers).IsEmpty();
    }

    [Test]
    public async Task Entries_in_a_section_that_is_not_read_are_noted()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("CHF", "100.00 CHF", "100.00 CHF")
            .Section("Investment Services Summaries", "Stocks Account (USD)", "Opening balance $1,500.00",
                "Closing balance $1,650.00"));

        await Assert.That(statement.Notes).IsEquivalentTo([
            "The statement has entries under \"Investment Services Summaries\", which Treasury doesn't read.",
        ]);
        await Assert.That(statement.Rows).IsEmpty();
        await Assert.That(statement.BalancesByCurrency.Select(b => b.Currency)).IsEquivalentTo(["CHF"]);
    }

    [Test]
    public async Task An_account_that_is_not_a_personal_account_is_skipped_with_a_note()
    {
        var statement = Read(new RevolutCustomStatement()
            .Account("EUR", "€500.00", "€450.00", t => t
                .Row("Nov 4, 2026", "Florist", "Merchant", "-€20.00", "€480.00")
                .NextPage()
                .Row("Nov 5, 2026", "Cinema", "Merchant", "-€30.00", "€450.00")
                .Total("-€50.00"), iban: "LT601010012345678901", zero: "€0.00", kind: "Joint Account")
            .Account("CHF", "100.00 CHF", "90.00 CHF", t => t
                .Row("Nov 3, 2026", "Bakery", "Merchant", "-10.00 CHF", "90.00 CHF")
                .Total("-10.00 CHF"))
            .Account("EUR", "€5.00", "€5.00", zero: "€0.00"));

        await Assert.That(statement.Notes).IsEquivalentTo([
            "The statement also has \"Joint Account (EUR)\", which Treasury doesn't import.",
        ]);
        await Assert.That(statement.Rows).IsEquivalentTo(new[]
        {
            new StatementRow(Nov(3), -10.00m, "CHF", "Bakery", "Bakery"),
        });
        await Assert.That(statement.BalancesByCurrency.Select(b => (b.Currency, b.OpeningBalance, b.Closing.Amount)))
            .IsEquivalentTo([("CHF", 100.00m, 90.00m), ("EUR", 5.00m, 5.00m)]);
        // The joint account's IBAN is another one.
        await Assert.That(statement.AccountIdentifiers).IsEquivalentTo([RevolutCustomStatement.Iban]);
    }

    [Test]
    public async Task A_revolut_csv_is_rejected_with_a_hint_to_export_the_custom_statement_as_pdf()
    {
        var csv = "Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance\n" +
                  "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,580.00\n";

        var error = Assert.Throws<StatementFormatException>(() =>
            Reader.Read(new SourceFileContent("account-statement.csv", Encoding.UTF8.GetBytes(csv))));

        await Assert.That(error.Message).Contains("isn't a PDF").And.Contains("Custom statement")
            .And.Contains("export it as PDF in English");
    }

    [Test]
    [Arguments("Account statement", "Nov 1, 2026 - Nov 30, 2026", "Current Accounts Summaries")]
    [Arguments("Benutzerdefinierter Auszug", "1. Nov. 2026 - 30. Nov. 2026", "Zusammenfassung der Girokonten")]
    public async Task A_pdf_that_is_not_a_custom_statement_in_english_is_rejected_saying_what_is_expected(
        string title, string period, string heading)
    {
        var error = Assert.Throws<StatementFormatException>(() => Reader.Read(new SourceFileContent("other.pdf",
            RevolutCustomStatement.OtherPdf(title, period, heading, "Personal Account (CHF)"))));

        await Assert.That(error.Message).Contains("isn't a Revolut Custom statement in English")
            .And.Contains("export it as PDF in English");
    }

    [Test]
    public async Task A_broken_pdf_is_rejected_with_the_export_hint()
    {
        var error = Assert.Throws<StatementFormatException>(() =>
            Reader.Read(new SourceFileContent("broken.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\nnot really"))));

        await Assert.That(error.Message).Contains("couldn't be opened").And.Contains("export it as PDF in English");
    }
}
