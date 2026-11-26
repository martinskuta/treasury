using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Treasury.Api.Tests.Integrations;

namespace Treasury.Api.Tests;

/// #25: importing a Source file into a Multi-currency account (ADR-0009), by upload or from its Ingest folder.
/// Uses the test multi-currency Integrations, so the tests are about the pipeline and not about a format; the last
/// ones import synthetic Revolut Custom statement PDFs through the real revolut Integration (#36, ADR-0010).
public class MultiCurrencyImportTests : ApiTestBase
{
    [Test]
    public async Task Uploaded_rows_go_to_the_account_of_their_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var chf = group.Accounts.Single();

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("CHF", "2026-10-31", 1000m, "2026-11-30", 950m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-04", -30m, "Coop"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(2);
        await Assert.That(result.CreatedAccounts).IsEmpty();
        await Assert.That(result.Warnings).IsEmpty();
        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => (t.Description, t.AccountId)))
            .IsEquivalentTo([("Migros", (Guid?)chf.Id), ("Coop", (Guid?)chf.Id)]);
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Single().Balance).IsEqualTo(950m);
    }

    [Test]
    public async Task A_currency_without_an_account_gets_one_opened_with_the_statements_opening_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 35.50m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR")
            .Row("2026-11-07", -4.50m, "Gelateria", currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(3);
        await Assert.That(result.CreatedAccounts.Select(a => (a.Name, a.Currency))).IsEquivalentTo([("Revolut EUR", "EUR")]);
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        var euros = accounts.Single(a => a.Currency == "EUR");
        await Assert.That(euros.Id).IsEqualTo(result.CreatedAccounts.Single().Id);
        await Assert.That(euros.Name).IsEqualTo("Revolut EUR");
        await Assert.That(euros.Owner.MemberId).IsEqualTo(annaId);
        await Assert.That(euros.MultiCurrencyAccountId).IsEqualTo(group.Id);
        await Assert.That(euros.OpeningBalance).IsEqualTo(50m);
        await Assert.That(euros.OpeningDate).IsEqualTo(new DateOnly(2026, 10, 31));
        await Assert.That(euros.Balance).IsEqualTo(35.50m);
        await Assert.That(euros.BalanceWarning).IsNull();
        await Assert.That((await ListAsync(anna)).Where(t => t.Currency == "EUR").Select(t => t.AccountId).Distinct())
            .IsEquivalentTo([(Guid?)euros.Id]);
        // No Ingest folder of its own: files for it go into the group's folder.
        await Assert.That(Directory.GetDirectories(App.IngestRoot).Select(d => Path.GetFileName(d)!))
            .IsEquivalentTo(["revolut"]);
        // Renamable like any Account.
        var rename = await anna.PatchAsJsonAsync($"/api/accounts/{euros.Id}", new { name = "Revolut Euro" });
        await Assert.That(rename.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task A_currency_without_an_account_and_without_balances_on_the_statement_fails_the_file()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"].Single()).Contains("doesn't say the EUR opening balance");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
    }

    [Test]
    public async Task A_currency_with_a_balance_but_no_rows_gets_an_account_with_that_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("CHF", "2026-10-31", 1000m, "2026-11-30", 980m)
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 50m)
            .Row("2026-11-03", -20m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That(result.CreatedAccounts.Select(a => (a.Name, a.Currency))).IsEquivalentTo([("Revolut EUR", "EUR")]);
        await Assert.That(result.Warnings).IsEmpty();
        var euros = (await GetGroupAsync(anna, group.Id)).Accounts.Single(a => a.Currency == "EUR");
        await Assert.That((euros.OpeningBalance, euros.OpeningDate, euros.Balance, euros.BalanceWarning))
            .IsEqualTo((50m, new DateOnly(2026, 10, 31), 50m, (BalanceWarning?)null));
    }

    [Test]
    public async Task A_currency_with_no_rows_and_no_money_gets_no_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("EUR", "2026-10-31", 0m, "2026-11-30", 0m)
            .Row("2026-11-03", -20m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.CreatedAccounts).IsEmpty();
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
    }

    [Test]
    public async Task The_closing_balance_of_a_currency_with_no_rows_is_checked_against_its_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        // The CHF Account opened with 1000, but the statement says CHF stayed at 900 over its period.
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("CHF", "2026-10-31", 900m, "2026-11-30", 900m));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(0);
        await Assert.That(result.Warnings.Single()).Contains("900.00 CHF").And.Contains("1'000.00 CHF");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Single().BalanceWarning)
            .IsEqualTo(new BalanceWarning(new DateOnly(2026, 11, 30), 900m, 1000m));
    }

    [Test]
    public async Task A_balance_without_rows_in_a_currency_without_exchange_rates_is_skipped_with_a_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("RUB", "2026-10-31", 20m, "2026-11-30", 20m)
            .Row("2026-11-03", -20m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That(result.CreatedAccounts).IsEmpty();
        await Assert.That(result.Warnings.Single()).Contains("a balance in RUB").And.Contains("exchange rates")
            .And.Contains("RUB balance isn't tracked");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
    }

    [Test]
    public async Task A_note_about_what_the_integration_did_not_read_shows_as_a_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Note("The statement has entries under \"Savings\", which Treasury doesn't read.")
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(2);
        await Assert.That(result.Warnings)
            .IsEquivalentTo(["The statement has entries under \"Savings\", which Treasury doesn't read."]);
    }

    [Test]
    public async Task A_statement_for_another_identifier_is_rejected_and_creates_nothing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31",
            integrationKey: "test-multi-id", identifier: "LT12 1000 0111 0100 1000");

        var res = await UploadAsync(anna, group.Id, "someone-elses.txt", new TestStatement()
            .Identifier("LT60 1010 0123 4567 8901")
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"].Single()).Contains("LT60 1010 0123 4567 8901")
            .And.Contains("the Multi-currency account \"Revolut\"").And.Contains("LT12 1000 0111 0100 1000");
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
        await Assert.That(File.Exists(Path.Combine(App.IngestRoot, group.Slug, "failed", "someone-elses.txt"))).IsTrue();
    }

    [Test]
    public async Task A_statement_for_another_identifier_is_rejected_even_when_it_shows_no_money()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31",
            integrationKey: "test-multi-id", identifier: "LT12 1000 0111 0100 1000");

        var res = await UploadAsync(anna, group.Id, "someone-elses.txt", new TestStatement()
            .Identifier("LT60 1010 0123 4567 8901"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await SourceFilesAsync(anna, group.Id)).IsEmpty();
    }

    [Test]
    public async Task A_statement_whose_identifier_matches_after_normalising_is_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31",
            integrationKey: "test-multi-id", identifier: "LT12 1000 0111 0100 1000");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Identifier("lt121000011101001000")
            .Row("2026-11-03", -20m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task A_multi_currency_account_without_identifiers_accepts_any_statement_and_takes_its_identifiers()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31",
            integrationKey: "test-multi-id");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Identifier("LT60 1010 0123 4567 8901")
            .Row("2026-11-03", -20m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(1);
        // Its Accounts get none of their own.
        var reloaded = await GetGroupAsync(anna, group.Id);
        await Assert.That(reloaded.Identifiers).IsEquivalentTo(["LT60 1010 0123 4567 8901"]);
        await Assert.That(reloaded.Accounts.Single().Identifiers).IsEmpty();
    }

    [Test]
    public async Task A_revolut_custom_statement_over_a_change_of_iban_adds_the_old_iban_to_the_multi_currency_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        const string gbIban = "GB33BUKB20201555555555";
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 0m, openingDate: "2018-02-28",
            integrationKey: "revolut", identifier: "LT12 1000 0111 0100 1000");

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "custom-statement_2018-2026.pdf",
            new RevolutCustomStatement("Mar 1, 2018 - Oct 31, 2026")
                .Account("CHF", "0.00 CHF", "100.00 CHF", t => t
                    .Row("Mar 21, 2018", "Top-up by *0315", "Top up", "100.00 CHF", "100.00 CHF")
                    .Total("100.00 CHF")
                    .FormerGbIban(gbIban))
                .Build());
        // A statement from before the change shows only the old IBAN, and is now accepted.
        var old = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "custom-statement_2018.pdf",
            new RevolutCustomStatement("Mar 1, 2018 - Mar 31, 2018")
                .Account("CHF", "0.00 CHF", "100.00 CHF", t => t
                    .Row("Mar 21, 2018", "Top-up by *0315", "Top up", "100.00 CHF", "100.00 CHF")
                    .Total("100.00 CHF"), iban: gbIban)
                .Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await res.Content.ReadFromJsonAsync<ImportResult>())!.Warnings).IsEquivalentTo([
            $"The Multi-currency account \"Revolut\" now also has the Identifier {gbIban}, which this statement shows.",
        ]);
        await Assert.That(old.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await old.Content.ReadFromJsonAsync<ImportResult>())!.SkippedCount).IsEqualTo(1);
        await Assert.That((await GetGroupAsync(anna, group.Id)).Identifiers).IsEquivalentTo(["LT12 1000 0111 0100 1000", gbIban]);
    }

    [Test]
    public async Task A_pound_row_opens_a_pound_account_whose_balance_counts_in_the_total()
    {
        // Today (2026-11-20) 1 EUR = 0.93 CHF = 0.75 GBP: 15 GBP = 20 EUR = 18.60 CHF.
        App.ExchangeRates.Publish("2026-11-20", chf: 0.93m);
        App.ExchangeRates.Publish("2026-11-20", "GBP", 0.75m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("GBP", "2026-10-31", 20m, "2026-11-30", 15m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-06", -5m, "Pret", currency: "GBP"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.CreatedAccounts.Select(a => (a.Name, a.Currency))).IsEquivalentTo([("Revolut GBP", "GBP")]);
        var revolut = await EventuallyAsync(anna, group.Id, g => g.TotalBalance is not null);
        await Assert.That(revolut.Accounts.Select(a => (a.Currency, a.Balance)))
            .IsEquivalentTo([("CHF", 980m), ("GBP", 15m)]);
        await Assert.That(revolut.TotalBalance).IsEqualTo(998.60m);
    }

    [Test]
    public async Task A_file_stays_one_source_file_and_undo_removes_every_currency_but_keeps_created_accounts()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var import = (await (await UploadAsync(anna, group.Id, "november.txt", TwoCurrencies()))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        var files = await SourceFilesAsync(anna, group.Id);
        await Assert.That(files.Select(f => (f.Id, f.FileName, f.TransactionCount, f.MultiCurrencyAccountId, f.AccountId)))
            .IsEquivalentTo([(import.SourceFile.Id, "november.txt", 2, (Guid?)group.Id, (Guid?)null)]);
        await Assert.That((await ListAsync(anna)).Select(t => t.SourceFile!.Id).Distinct()).IsEquivalentTo([import.SourceFile.Id]);
        var content = await anna.GetAsync($"/api/source-files/{import.SourceFile.Id}/content");
        await Assert.That(content.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var undo = await anna.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        await Assert.That(undo.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await SourceFilesAsync(anna, group.Id)).IsEmpty();
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        await Assert.That(accounts.Select(a => (a.Currency, a.Balance))).IsEquivalentTo([("CHF", 1000m), ("EUR", 50m)]);
    }

    [Test]
    public async Task Importing_the_same_file_again_skips_every_row_in_every_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        await UploadAsync(anna, group.Id, "november.txt", TwoCurrencies());

        var again = (await (await UploadAsync(anna, group.Id, "november (copy).txt", TwoCurrencies())).Content
            .ReadFromJsonAsync<ImportResult>())!;

        await Assert.That(again.ImportedCount).IsEqualTo(0);
        await Assert.That(again.SkippedCount).IsEqualTo(2);
        await Assert.That(again.CreatedAccounts).IsEmpty();
        await Assert.That(again.Warnings).IsEmpty();
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Closing_balances_are_kept_and_checked_per_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        // The CHF Account opened with 1000, but the statement says CHF was at 900 when its period started.
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var result = (await (await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("CHF", "2026-10-31", 900m, "2026-11-30", 880m)
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR")))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That(result.Warnings).Count().IsEqualTo(1);
        await Assert.That(result.Warnings.Single()).Contains("880.00 CHF").And.Contains("980.00 CHF");
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        var francs = accounts.Single(a => a.Currency == "CHF");
        await Assert.That(francs.BalanceWarning).IsEqualTo(new BalanceWarning(new DateOnly(2026, 11, 30), 880m, 980m));
        await Assert.That(accounts.Single(a => a.Currency == "EUR").BalanceWarning).IsNull();

        // Undoing the file takes its closing balances with it, and the warning clears.
        await anna.DeleteAsync($"/api/source-files/{result.SourceFile.Id}");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.All(a => a.BalanceWarning is null)).IsTrue();
    }

    [Test]
    public async Task A_tag_the_integration_suggests_in_several_currencies_is_created_once_and_undo_keeps_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var import = (await (await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("CHF", "2026-10-31", 1000m, "2026-11-30", 976.50m)
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 39m)
            .Row("2026-11-03", -20m, "Migros")
            .Row("2026-11-04", -3.50m, "Fee: Cash", suggestedTags: ["fee"])
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR")
            .Row("2026-11-06", -1m, "Fee: Trattoria", currency: "EUR", suggestedTags: ["fee"])))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That(await TagNamesAsync(anna)).IsEquivalentTo(["fee"]);
        await Assert.That((await ListAsync(anna)).Select(t => (t.Currency, t.Description, string.Join(",", t.Tags.Select(x => x.Name)))))
            .IsEquivalentTo([
                ("CHF", "Migros", ""), ("CHF", "Fee: Cash", "fee"), ("EUR", "Trattoria", ""), ("EUR", "Fee: Trattoria", "fee"),
            ]);

        // Undoing the import removes its Transactions with their Tags; the Tag itself stays.
        await anna.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await TagNamesAsync(anna)).IsEquivalentTo(["fee"]);
    }

    [Test]
    public async Task An_exchange_between_two_of_its_accounts_is_matched_as_a_transfer()
    {
        // 1 EUR = 1.087 CHF: +92 EUR is 100.00 CHF, against -100 CHF.
        App.ExchangeRates.Publish("2026-11-09", chf: 1.087m);
        App.ExchangeRates.Publish("2026-11-10", chf: 1.087m);
        App.ExchangeRates.Publish("2026-11-11", chf: 1.087m);
        App.ExchangeRates.Publish("2026-11-13", chf: 1.087m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-11-01");
        // An earlier statement in EUR, so that the exchange rates around the exchange are stored before it is imported.
        await UploadAsync(anna, group.Id, "early-november.txt", new TestStatement()
            .Balances("EUR", "2026-11-08", 8m, "2026-11-09", 0m)
            .Row("2026-11-09", -8m, "Kiosk", currency: "EUR"));
        await RatesSyncedAsync(anna);

        var res = await UploadAsync(anna, group.Id, "two-currencies.txt", new TestStatement()
            .Balances("CHF", "2026-11-09", 1000m, "2026-11-13", 890m)
            .Balances("EUR", "2026-11-09", 0m, "2026-11-13", 67m)
            .Row("2026-11-11", -25m, "Trattoria Roma, Milano", currency: "EUR")
            .Row("2026-11-10", -100m, "Exchanged to EUR")
            .Row("2026-11-10", 92m, "Exchanged to EUR", currency: "EUR")
            .Row("2026-11-13", -10m, "Migros"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var list = await ListAsync(anna);
        var toEur = list.Single(t => t.Description == "Exchanged to EUR" && t.Currency == "CHF");
        var fromChf = list.Single(t => t.Description == "Exchanged to EUR" && t.Currency == "EUR");
        await Assert.That(toEur.Type).IsEqualTo("transfer");
        await Assert.That(fromChf.Type).IsEqualTo("transfer");
        await Assert.That(toEur.Link!.TransactionId).IsEqualTo(fromChf.Id);
        await Assert.That(list.Single(t => t.Description == "Migros").Type).IsEqualTo("expense");
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.Warnings).IsEmpty();
    }

    [Test]
    public async Task Rules_and_date_range_tags_apply_to_the_rows_of_every_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var groceries = (await anna.GetFromJsonAsync<List<CategoryDto>>("/api/categories"))!
            .Single(c => c.Path == "Food › Groceries").Id;
        await PostOkAsync(anna, "/api/rules", new { field = "description", match = "contains", text = "Market", categoryId = groceries });
        await PostOkAsync(anna, "/api/tags", new { name = "holiday", dateFrom = "2026-11-01", dateTo = "2026-11-30" });

        await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
            .Row("2026-11-03", -20m, "Market Zurich")
            .Row("2026-11-06", -10m, "Market Milano", currency: "EUR"));

        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => (t.Currency, t.Category?.Id, string.Join(",", t.Tags.Select(x => x.Name)))))
            .IsEquivalentTo([("CHF", (Guid?)groceries, "holiday"), ("EUR", (Guid?)groceries, "holiday")]);
    }

    [Test]
    public async Task Undoing_a_file_breaks_a_transfer_with_a_transaction_outside_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var checking = await PostOkAsync<IdDto>(anna, "/api/accounts", new
        {
            name = "Checking", owner = new { kind = "member", memberId = await MemberIdAsync(anna) },
            integrationKey = "test", currency = "CHF", openingBalance = 0m, openingDate = "2026-01-01",
        });
        var incoming = await PostOkAsync<IdDto>(anna, "/api/transactions",
            new { accountId = checking.Id, date = "2026-11-03", amount = 100m, description = "From Revolut" });
        var import = (await (await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Row("2026-11-03", -100m, "To Checking")))
            .Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That((await ListAsync(anna)).Single(t => t.Id == incoming.Id).Type).IsEqualTo("transfer");

        await anna.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        var after = (await ListAsync(anna)).Single();
        await Assert.That((after.Id, after.Type, after.Link)).IsEqualTo((incoming.Id, "income", (LinkRef?)null));
    }

    [Test]
    public async Task Concurrent_imports_into_two_multi_currency_accounts_both_open_their_new_accounts()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        for (var round = 0; round < 5; round++)
        {
            // Same names: both new EUR Accounts are called "Revolut <round> EUR".
            var first = await CreateGroupAsync(anna, $"Revolut {round}", openingBalance: 1000m, openingDate: "2026-10-31");
            var second = await CreateGroupAsync(anna, $"Revolut {round}", openingBalance: 1000m, openingDate: "2026-10-31");

            var results = await Task.WhenAll(
                UploadAsync(anna, first.Id, "a.txt", OneEuroRow()), UploadAsync(anna, second.Id, "b.txt", OneEuroRow()));

            var outcomes = await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"));
            await Assert.That(outcomes.Where(o => !o.StartsWith("201"))).IsEmpty();
        }
    }

    [Test]
    public async Task A_scan_reports_the_accounts_an_import_from_the_folder_created()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        await File.WriteAllTextAsync(Path.Combine(App.IngestRoot, group.Slug, "november.txt"), OneEuroRow().ToString());

        var scanned = (await ScanAsync(anna)).Single();

        await Assert.That(scanned.Succeeded).IsTrue();
        await Assert.That(scanned.CreatedAccounts.Select(a => a.Name)).IsEquivalentTo(["Revolut EUR"]);
    }

    [Test]
    public async Task A_file_that_fails_goes_to_the_multi_currency_accounts_failed_folder_and_creates_nothing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var folder = Path.Combine(App.IngestRoot, group.Slug);
        await File.WriteAllTextAsync(Path.Combine(folder, "roubles.txt"), new TestStatement()
            .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
            .Balances("RUB", "2026-10-31", 20m, "2026-11-30", 15m)
            .Row("2026-11-06", -10m, "Trattoria", currency: "EUR")
            .Row("2026-11-06", -5m, "Pret", currency: "RUB").ToString());

        var scanned = (await ScanAsync(anna)).Single();

        await Assert.That(scanned.Succeeded).IsFalse();
        await Assert.That(scanned.Error).Contains("RUB");
        await Assert.That(File.Exists(Path.Combine(folder, "failed", "roubles.txt"))).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(folder, "failed", "roubles.txt.error.txt")))
            .Contains("the Multi-currency account \"Revolut\"").And.Contains("RUB");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await SourceFilesAsync(anna, group.Id)).IsEmpty();
    }

    [Test]
    public async Task Uploading_straight_into_one_of_its_accounts_is_rejected_with_a_pointer_to_the_group()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, $"/api/accounts/{group.Accounts.Single().Id}/imports", "november.txt",
            Encoding.UTF8.GetBytes(new TestStatement().Row("2026-11-03", -20m, "Migros").ToString()));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"].Single()).Contains("Multi-currency account \"Revolut\"");
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Another_members_multi_currency_account_and_its_source_files_stay_hidden()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var import = (await (await UploadAsync(anna, group.Id, "november.txt", new TestStatement()
            .Row("2026-11-03", -20m, "Migros")))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That((await UploadAsync(bob, group.Id, "x.txt", new TestStatement())).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/multi-currency-accounts/{group.Id}/source-files")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.DeleteAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await SourceFilesAsync(anna, group.Id)).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Revolut_custom_statements_import_end_to_end_by_upload()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut");

        var october = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", OctoberStatement,
            October().Build());
        var november = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", NovemberStatement,
            November().Build());

        var octoberResult = (await october.Content.ReadFromJsonAsync<ImportResult>())!;
        var novemberResult = (await november.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That((octoberResult.ImportedCount, novemberResult.ImportedCount)).IsEqualTo((2, 5));
        // The first statement gives the Multi-currency account its IBAN.
        await Assert.That(octoberResult.Warnings).IsEquivalentTo([LearnedIban]);
        await Assert.That(novemberResult.Warnings).IsEmpty();
        await Assert.That(octoberResult.CreatedAccounts).IsEmpty();
        await Assert.That(novemberResult.CreatedAccounts.Select(a => a.Name)).IsEquivalentTo(["Revolut EUR"]);
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        await Assert.That(accounts.Select(a => (a.Currency, a.Balance, a.BalanceWarning)))
            .IsEquivalentTo([("CHF", 890m, (BalanceWarning?)null), ("EUR", 40m, null)]);
        // The EUR Account is opened where the statement's period starts.
        var euros = accounts.Single(a => a.Currency == "EUR");
        await Assert.That((euros.OpeningBalance, euros.OpeningDate)).IsEqualTo((0m, new DateOnly(2026, 11, 1)));
        var payment = (await ListAsync(anna)).Single(t => t.Description == "To Jan Novak");
        await Assert.That((payment.Date, payment.Amount, payment.Counterparty, payment.AccountId))
            .IsEqualTo((new DateOnly(2026, 11, 10), -1500m, "Jan Novak", (Guid?)accounts.Single(a => a.Currency == "CHF").Id));
        await Assert.That((await SourceFilesAsync(anna, group.Id)).Select(f => f.FileName))
            .IsEquivalentTo([OctoberStatement, NovemberStatement]);
    }

    [Test]
    public async Task Importing_a_revolut_custom_statement_tags_its_fees_as_fee()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31",
            integrationKey: "revolut");

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", NovemberStatement,
            new RevolutCustomStatement("Nov 1, 2026 - Nov 20, 2026")
                .Account("CHF", "1,000.00 CHF", "759.50 CHF", t => t
                    .Row("Nov 6, 2026", "Cash at Bahnhofstrasse", "Cash", "-203.50 CHF", "796.50 CHF", fees: "3.50 CHF")
                    .Row("Nov 7, 2026", "Card Delivery Fee", "Fees", "-7.00 CHF", "789.50 CHF")
                    .Row("Nov 8, 2026", "Bookshop", "Merchant", "-30.00 CHF", "759.50 CHF")
                    .Total("-240.50 CHF", fees: "3.50 CHF"))
                .Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(await TagNamesAsync(anna)).IsEquivalentTo(["fee"]);
        await Assert.That((await ListAsync(anna)).Select(t => (t.Description, t.Amount, string.Join(",", t.Tags.Select(x => x.Name)))))
            .IsEquivalentTo([
                ("Cash at Bahnhofstrasse", -200.00m, ""), ("Fee: Cash at Bahnhofstrasse", -3.50m, "fee"),
                ("Card Delivery Fee", -7.00m, "fee"), ("Bookshop", -30.00m, ""),
            ]);
    }

    [Test]
    public async Task Revolut_custom_statements_import_end_to_end_from_the_ingest_folder()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut");
        var folder = Path.Combine(App.IngestRoot, group.Slug);
        await File.WriteAllBytesAsync(Path.Combine(folder, OctoberStatement), October().Build());
        await File.WriteAllBytesAsync(Path.Combine(folder, NovemberStatement), November().Build());

        var scanned = await ScanAsync(anna);

        await Assert.That(scanned.Select(f => (f.FileName, f.Succeeded, f.ImportedCount, f.MultiCurrencyAccountId, f.AccountId)))
            .IsEquivalentTo([
                (OctoberStatement, true, 2, (Guid?)group.Id, (Guid?)null),
                (NovemberStatement, true, 5, (Guid?)group.Id, (Guid?)null),
            ]);
        await Assert.That(Directory.GetFiles(folder)).IsEmpty();
        await Assert.That(Directory.GetFiles(Path.Combine(folder, "processed")).Select(f => Path.GetFileName(f)!))
            .IsEquivalentTo([OctoberStatement, NovemberStatement]);
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        await Assert.That(accounts.Select(a => (a.Currency, a.Balance, a.BalanceWarning)))
            .IsEquivalentTo([("CHF", 890m, (BalanceWarning?)null), ("EUR", 40m, null)]);
        await Assert.That(await SourceFilesAsync(anna, group.Id)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task A_revolut_custom_statement_opens_accounts_for_the_currencies_holding_money()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        // The identifier is the statement's IBAN, written the way a Member would type it.
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2450m, openingDate: "2026-10-31",
            integrationKey: "revolut", identifier: "LT12 1000 0111 0100 1000");

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", NovemberStatement,
            November()
                .Account("USD", "$120.00", "$120.00", zero: "$0.00")
                .Account("GBP", "£0.00", "£0.00", zero: "£0.00")
                .Section("Investment Services Summaries", "Stocks Account (USD)", "Opening balance $1,500.00")
                .Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(5);
        await Assert.That(result.CreatedAccounts.Select(a => a.Name)).IsEquivalentTo(["Revolut EUR", "Revolut USD"]);
        await Assert.That(result.Warnings).IsEquivalentTo([
            "The statement has entries under \"Investment Services Summaries\", which Treasury doesn't read.",
        ]);
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        await Assert.That(accounts.Select(a => (a.Currency, a.OpeningBalance, a.OpeningDate, a.Balance, a.BalanceWarning)))
            .IsEquivalentTo([
                ("CHF", 2450m, new DateOnly(2026, 10, 31), 890m, (BalanceWarning?)null),
                ("EUR", 0m, new DateOnly(2026, 11, 1), 40m, null),
                ("USD", 120m, new DateOnly(2026, 11, 1), 120m, null),
            ]);
    }

    [Test]
    public async Task A_revolut_custom_statement_with_another_iban_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut", identifier: "LT60 1010 0123 4567 8901");

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", OctoberStatement,
            October().Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"].Single()).Contains(RevolutCustomStatement.Iban)
            .And.Contains("LT60 1010 0123 4567 8901");
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await SourceFilesAsync(anna, group.Id)).IsEmpty();
    }

    [Test]
    public async Task A_revolut_custom_statement_imported_again_or_overlapping_an_earlier_one_skips_what_is_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut");
        var url = $"/api/multi-currency-accounts/{group.Id}/imports";
        await UploadAsync(anna, url, OctoberStatement, October().Build());

        var again = (await (await UploadAsync(anna, url, "october (copy).pdf", October().Build()))
            .Content.ReadFromJsonAsync<ImportResult>())!;
        // From the middle of October: its first row is October's second.
        var overlapping = (await (await UploadAsync(anna, url, "custom-statement_2026-10-15_2026-11-20.pdf",
            new RevolutCustomStatement("Oct 15, 2026 - Nov 20, 2026")
                .Account("CHF", "2,470.00 CHF", "890.00 CHF", t => t
                    .Row("Oct 20, 2026", "Padel Club", "Merchant", "-20.00 CHF", "2,450.00 CHF")
                    .Row("Nov 3, 2026", "Top-up by card", "Others", "40.00 CHF", "2,490.00 CHF")
                    .Row("Nov 5, 2026", "Bakery", "Merchant", "-100.00 CHF", "2,390.00 CHF")
                    .Row("Nov 10, 2026", "To Jan Novak", "Others", "-1,500.00 CHF", "890.00 CHF")
                    .Total("-1,580.00 CHF"))
                .Build())).Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That((again.ImportedCount, again.SkippedCount)).IsEqualTo((0, 2));
        await Assert.That((overlapping.ImportedCount, overlapping.SkippedCount)).IsEqualTo((3, 1));
        await Assert.That(again.Warnings.Concat(overlapping.Warnings)).IsEmpty();
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(5);
        var francs = (await GetGroupAsync(anna, group.Id)).Accounts.Single();
        await Assert.That((francs.Balance, francs.BalanceWarning)).IsEqualTo((890m, (BalanceWarning?)null));
    }

    [Test]
    public async Task A_revolut_custom_statement_without_any_transaction_imports_cleanly()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut");

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", OctoberStatement,
            new RevolutCustomStatement("Oct 1, 2026 - Oct 31, 2026")
                .Account("CHF", "2,500.00 CHF", "2,500.00 CHF")
                .Account("EUR", "€0.00", "€0.00", zero: "€0.00")
                .Account("USD", "$120.00", "$120.00", zero: "$0.00")
                .Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(0);
        await Assert.That(result.CreatedAccounts.Select(a => a.Name)).IsEquivalentTo(["Revolut USD"]);
        await Assert.That(result.Warnings).IsEquivalentTo([LearnedIban]);
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => (a.Currency, a.Balance, a.BalanceWarning)))
            .IsEquivalentTo([("CHF", 2500m, (BalanceWarning?)null), ("USD", 120m, null)]);
        await Assert.That((await SourceFilesAsync(anna, group.Id)).Select(f => (f.FileName, f.TransactionCount)))
            .IsEquivalentTo([(OctoberStatement, 0)]);
    }

    [Test]
    public async Task A_revolut_custom_statement_checks_an_empty_currency_without_rows_that_already_has_an_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2500m, openingDate: "2026-09-30",
            integrationKey: "revolut");
        var url = $"/api/multi-currency-accounts/{group.Id}/imports";
        await UploadAsync(anna, url, OctoberStatement, October().Build());
        // Opens the EUR Account and leaves 40 EUR in it.
        await UploadAsync(anna, url, NovemberStatement, November().Build());

        // December says EUR was empty all month: whatever emptied it was never imported.
        var res = await UploadAsync(anna, url, "custom-statement_2026-12-01_2026-12-31.pdf",
            new RevolutCustomStatement("Dec 1, 2026 - Dec 31, 2026")
                .Account("CHF", "890.00 CHF", "890.00 CHF")
                .Account("EUR", "€0.00", "€0.00", zero: "€0.00")
                .Account("USD", "$0.00", "$0.00", zero: "$0.00")
                .Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.CreatedAccounts).IsEmpty();
        await Assert.That(result.Warnings.Single()).Contains("31.12.2026").And.Contains("0.00 EUR").And.Contains("40.00 EUR");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.Select(a => (a.Currency, a.BalanceWarning)))
            .IsEquivalentTo([
                ("CHF", (BalanceWarning?)null),
                ("EUR", new BalanceWarning(new DateOnly(2026, 12, 31), 0m, 40m)),
            ]);
    }

    // ---- helpers ----

    /// One CHF row and one row in EUR, a currency the Multi-currency account has no Account for yet.
    private static TestStatement TwoCurrencies() => new TestStatement()
        .Balances("CHF", "2026-10-31", 1000m, "2026-11-30", 980m)
        .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
        .Row("2026-11-03", -20m, "Migros")
        .Row("2026-11-06", -10m, "Trattoria", currency: "EUR");

    private static TestStatement OneEuroRow() => new TestStatement()
        .Balances("EUR", "2026-10-31", 50m, "2026-11-30", 40m)
        .Row("2026-11-06", -10m, "Trattoria", currency: "EUR");

    /// What the first Revolut import into a Multi-currency account without Identifiers says.
    private const string LearnedIban =
        $"The Multi-currency account \"Revolut\" now also has the Identifier {RevolutCustomStatement.Iban}, which this statement shows.";

    private const string OctoberStatement = "custom-statement_2026-10-01_2026-10-31.pdf";
    private const string NovemberStatement = "custom-statement_2026-11-01_2026-11-20.pdf";

    /// CHF goes from 2'500 to 2'450; the EUR account is empty.
    private static RevolutCustomStatement October() => new RevolutCustomStatement("Oct 1, 2026 - Oct 31, 2026")
        .Account("CHF", "2,500.00 CHF", "2,450.00 CHF", t => t
            .Row("Oct 12, 2026", "Padel Club", "Merchant", "-30.00 CHF", "2,470.00 CHF")
            .Row("Oct 20, 2026", "Padel Club", "Merchant", "-20.00 CHF", "2,450.00 CHF")
            .Total("-50.00 CHF"))
        .Account("EUR", "€0.00", "€0.00", zero: "€0.00");

    /// Continues October: CHF goes from 2'450 to 890, and EUR gets its first rows.
    private static RevolutCustomStatement November() => new RevolutCustomStatement("Nov 1, 2026 - Nov 20, 2026")
        .Account("CHF", "2,450.00 CHF", "890.00 CHF", t => t
            .Row("Nov 3, 2026", "Top-up by card", "Others", "40.00 CHF", "2,490.00 CHF")
            .Row("Nov 5, 2026", "Bakery", "Merchant", "-100.00 CHF", "2,390.00 CHF")
            .Row("Nov 10, 2026", "To Jan Novak", "Others", "-1,500.00 CHF", "890.00 CHF")
            .Total("-1,560.00 CHF"))
        .Account("EUR", "€0.00", "€40.00", t => t
            .Row("Nov 6, 2026", "Payment from Acme, Inc.", "Top up", "€50.00", "€50.00")
            .Row("Nov 8, 2026", "Trattoria", "Merchant", "-€10.00", "€40.00")
            .Total("€40.00"), zero: "€0.00");

    private static async Task<List<SourceFileDto>> SourceFilesAsync(HttpClient client, Guid groupId) =>
        (await client.GetFromJsonAsync<List<SourceFileDto>>($"/api/multi-currency-accounts/{groupId}/source-files"))!;

    private static async Task<List<ScannedFile>> ScanAsync(HttpClient client)
    {
        var res = await client.PostAsync("/api/ingest/scan", null);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Scan failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<ScannedFile>>())!;
    }

    /// Waits until the exchange-rate job has stored rates for the visible Transactions (their CHF value is known).
    private static async Task RatesSyncedAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await client.GetFromJsonAsync<List<ChfAmountDto>>("/api/transactions"))!.Any(t => t.ChfAmount is null))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Exchange rates were not synced.");
            await Task.Delay(25);
        }
    }

    private record ChfAmountDto(decimal? ChfAmount);

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record ScannedFile(Guid? AccountId, Guid? MultiCurrencyAccountId, string FileName, bool Succeeded,
        int ImportedCount, string? Error, List<CreatedAccount> CreatedAccounts);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid groupId, string fileName, TestStatement statement) =>
        await UploadAsync(client, $"/api/multi-currency-accounts/{groupId}/imports", fileName,
            Encoding.UTF8.GetBytes(statement.ToString()));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".pdf") ? "application/pdf" : "text/plain");
        form.Add(file, "file", fileName);
        return await client.PostAsync(url, form);
    }

    private static async Task<Group> CreateGroupAsync(HttpClient client, string name, decimal openingBalance,
        string openingDate, object? owner = null, string integrationKey = "test-multi", string? identifier = null)
    {
        owner ??= new { kind = "member", memberId = await MemberIdAsync(client) };
        var res = await client.PostAsJsonAsync("/api/multi-currency-accounts", new
        {
            name, owner, integrationKey, mainCurrency = "CHF", openingBalance, openingDate,
            identifiers = identifier is null ? null : new[] { identifier },
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Group>())!;
    }

    private static async Task<Group> GetGroupAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{id}"))!;

    /// The names of the Family's Tags.
    private static async Task<List<string>> TagNamesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TagRef>>("/api/tags"))!.Select(t => t.Name).ToList();

    private static async Task<List<TransactionDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private record Me(Guid Id);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record BalanceWarning(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance);

    private record Account(Guid Id, string Name, OwnerDto Owner, string Currency, decimal OpeningBalance,
        DateOnly OpeningDate, decimal Balance, BalanceWarning? BalanceWarning, Guid? MultiCurrencyAccountId,
        List<string> Identifiers);

    private record Group(Guid Id, string Name, string Slug, OwnerDto Owner, List<Account> Accounts, decimal? TotalBalance,
        List<string> Identifiers);

    /// Polls one Multi-currency account until the condition holds. Exchange rates are fetched in the background on a
    /// schedule, so the clock is moved past the next run between polls.
    private async Task<Group> EventuallyAsync(HttpClient client, Guid groupId, Func<Group, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var group = await GetGroupAsync(client, groupId);
            if (done(group)) return group;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(25);
            App.Clock.Advance(TimeSpan.FromMinutes(30));
        }
    }

    private record SourceFileDto(Guid Id, Guid? AccountId, Guid? MultiCurrencyAccountId, string FileName,
        int TransactionCount);

    private record CreatedAccount(Guid Id, string Name, string Currency);

    private record ImportResult(SourceFileDto SourceFile, int ImportedCount, int SkippedCount, List<string> Warnings,
        List<CreatedAccount> CreatedAccounts);

    private record SourceFileRef(Guid Id, string FileName);

    private record LinkRef(string Kind, Guid TransactionId);

    private record TransactionDto(Guid Id, Guid? AccountId, DateOnly Date, decimal Amount, string Currency,
        string Type, string Description, string? Counterparty, SourceFileRef? SourceFile, LinkRef? Link,
        CategoryRef? Category, List<TagRef> Tags);

    private record CategoryRef(Guid Id);

    private record TagRef(string Name);

    private record CategoryDto(Guid Id, string Path);

    private record IdDto(Guid Id);

    private static async Task PostOkAsync(HttpClient client, string url, object body) =>
        await PostOkAsync<IdDto>(client, url, body);

    private static async Task<T> PostOkAsync<T>(HttpClient client, string url, object body)
    {
        var res = await client.PostAsJsonAsync(url, body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"POST {url} failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}
