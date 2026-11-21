using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// #25: importing a Source file into a Multi-currency account (ADR-0009), by upload or from its Ingest folder.
/// Uses the real revolut Integration with statements written inline in Revolut's CSV format.
public class MultiCurrencyImportTests : ApiTestBase
{
    private const string Header = "Type,Product,Started Date,Completed Date,Description,Amount,Fee,Currency,State,Balance";

    [Test]
    public async Task Uploaded_rows_go_to_the_account_of_their_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        var chf = group.Accounts.Single();

        var res = await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-03 12:00:00,2026-11-04 08:00:00,Coop,-30.00,0.00,CHF,COMPLETED,950.00"));

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
    public async Task A_currency_without_an_account_gets_one_opened_with_the_balance_before_its_first_row()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00",
            "Card Payment,Current,2026-11-06 12:00:00,2026-11-07 08:00:00,Gelateria,-4.50,0.00,EUR,COMPLETED,35.50"));

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
        await Assert.That(euros.OpeningDate).IsEqualTo(new DateOnly(2026, 11, 5));
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
    public async Task A_pound_row_opens_a_pound_account_whose_balance_counts_in_the_total()
    {
        // Today (2026-11-20) 1 EUR = 0.93 CHF = 0.75 GBP: 15 GBP = 20 EUR = 18.60 CHF.
        App.ExchangeRates.Publish("2026-11-20", chf: 0.93m);
        App.ExchangeRates.Publish("2026-11-20", "GBP", 0.75m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var res = await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Pret,-5.00,0.00,GBP,COMPLETED,15.00"));

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
        var import = (await (await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00")))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        var files = await SourceFilesAsync(anna, group.Id);
        await Assert.That(files.Select(f => (f.Id, f.FileName, f.TransactionCount, f.MultiCurrencyAccountId, f.AccountId)))
            .IsEquivalentTo([(import.SourceFile.Id, "november.csv", 2, (Guid?)group.Id, (Guid?)null)]);
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
        var csv = Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00");
        await UploadAsync(anna, group.Id, "november.csv", csv);

        var again = (await (await UploadAsync(anna, group.Id, "november (copy).csv", csv)).Content
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
        // The CHF Account opened with 1000, but the statement says CHF was at 900 before its first row.
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");

        var result = (await (await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,880.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00")))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That(result.Warnings).Count().IsEqualTo(1);
        await Assert.That(result.Warnings.Single()).Contains("880.00 CHF").And.Contains("980.00 CHF");
        var accounts = (await GetGroupAsync(anna, group.Id)).Accounts;
        var francs = accounts.Single(a => a.Currency == "CHF");
        await Assert.That(francs.BalanceWarning).IsEqualTo(new BalanceWarning(new DateOnly(2026, 11, 3), 880m, 980m));
        await Assert.That(accounts.Single(a => a.Currency == "EUR").BalanceWarning).IsNull();

        // Undoing the file takes its closing balances with it, and the warning clears.
        await anna.DeleteAsync($"/api/source-files/{result.SourceFile.Id}");
        await Assert.That((await GetGroupAsync(anna, group.Id)).Accounts.All(a => a.BalanceWarning is null)).IsTrue();
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
        await UploadAsync(anna, group.Id, "early-november.csv", Csv(
            "Card Payment,Current,2026-11-09 12:00:00,2026-11-09 18:00:00,Kiosk,-8.00,0.00,EUR,COMPLETED,0.00"));
        await RatesSyncedAsync(anna);

        var res = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "two-currencies.csv",
            Sample("two-currencies.csv"));

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

        await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Market Zurich,-20.00,0.00,CHF,COMPLETED,980.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Market Milano,-10.00,0.00,EUR,COMPLETED,40.00"));

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
        var import = (await (await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Transfer,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,To Checking,-100.00,0.00,CHF,COMPLETED,900.00")))
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
            var csv = Csv(
                "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00");

            var results = await Task.WhenAll(UploadAsync(anna, first.Id, "a.csv", csv), UploadAsync(anna, second.Id, "b.csv", csv));

            var outcomes = await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"));
            await Assert.That(outcomes.Where(o => !o.StartsWith("201"))).IsEmpty();
        }
    }

    [Test]
    public async Task The_anonymised_revolut_samples_import_end_to_end_by_upload()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        // October's first row leaves 2'503.33 after -31.40: the balance before was 2'534.73.
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2534.73m, openingDate: "2026-10-18");

        var october = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", OctoberSample,
            Sample(OctoberSample));
        var november = await UploadAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", NovemberSample,
            Sample(NovemberSample));

        var octoberResult = (await october.Content.ReadFromJsonAsync<ImportResult>())!;
        var novemberResult = (await november.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That((octoberResult.ImportedCount, novemberResult.ImportedCount)).IsEqualTo((2, 4));
        await Assert.That(octoberResult.Warnings.Concat(novemberResult.Warnings)).IsEmpty();
        await Assert.That(octoberResult.CreatedAccounts.Concat(novemberResult.CreatedAccounts)).IsEmpty();
        var francs = (await GetGroupAsync(anna, group.Id)).Accounts.Single();
        await Assert.That(francs.Balance).IsEqualTo(861.68m);
        await Assert.That(francs.BalanceWarning).IsNull();
        var payment = (await ListAsync(anna)).Single(t => t.Description == "To Jan Novak");
        await Assert.That((payment.Date, payment.Amount, payment.Counterparty, payment.AccountId))
            .IsEqualTo((new DateOnly(2026, 11, 9), -1500m, "Jan Novak", (Guid?)francs.Id));
        await Assert.That((await SourceFilesAsync(anna, group.Id)).Select(f => f.FileName))
            .IsEquivalentTo([OctoberSample, NovemberSample]);
    }

    [Test]
    public async Task The_anonymised_revolut_samples_import_end_to_end_from_the_ingest_folder()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 2534.73m, openingDate: "2026-10-18");
        var folder = Path.Combine(App.IngestRoot, group.Slug);
        await File.WriteAllBytesAsync(Path.Combine(folder, OctoberSample), Sample(OctoberSample));
        await File.WriteAllBytesAsync(Path.Combine(folder, NovemberSample), Sample(NovemberSample));

        var scanned = await ScanAsync(anna);

        await Assert.That(scanned.Select(f => (f.FileName, f.Succeeded, f.ImportedCount, f.MultiCurrencyAccountId, f.AccountId)))
            .IsEquivalentTo([
                (OctoberSample, true, 2, (Guid?)group.Id, (Guid?)null),
                (NovemberSample, true, 4, (Guid?)group.Id, (Guid?)null),
            ]);
        await Assert.That(Directory.GetFiles(folder)).IsEmpty();
        await Assert.That(Directory.GetFiles(Path.Combine(folder, "processed")).Select(f => Path.GetFileName(f)!))
            .IsEquivalentTo([OctoberSample, NovemberSample]);
        var francs = (await GetGroupAsync(anna, group.Id)).Accounts.Single();
        await Assert.That(francs.Balance).IsEqualTo(861.68m);
        await Assert.That(francs.BalanceWarning).IsNull();
        await Assert.That(await SourceFilesAsync(anna, group.Id)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task A_scan_reports_the_accounts_an_import_from_the_folder_created()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut", openingBalance: 1000m, openingDate: "2026-10-31");
        await File.WriteAllTextAsync(Path.Combine(App.IngestRoot, group.Slug, "november.csv"), Csv(
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00"));

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
        await File.WriteAllTextAsync(Path.Combine(folder, "roubles.csv"), Csv(
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Trattoria,-10.00,0.00,EUR,COMPLETED,40.00",
            "Card Payment,Current,2026-11-05 12:00:00,2026-11-06 08:00:00,Pret,-5.00,0.00,RUB,COMPLETED,15.00"));

        var scanned = (await ScanAsync(anna)).Single();

        await Assert.That(scanned.Succeeded).IsFalse();
        await Assert.That(scanned.Error).Contains("RUB");
        await Assert.That(File.Exists(Path.Combine(folder, "failed", "roubles.csv"))).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(folder, "failed", "roubles.csv.error.txt")))
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

        var res = await UploadAsync(anna, $"/api/accounts/{group.Accounts.Single().Id}/imports", "november.csv",
            Encoding.UTF8.GetBytes(Csv(
                "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00")));

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
        var import = (await (await UploadAsync(anna, group.Id, "november.csv", Csv(
            "Card Payment,Current,2026-11-02 12:00:00,2026-11-03 08:00:00,Migros,-20.00,0.00,CHF,COMPLETED,980.00")))
            .Content.ReadFromJsonAsync<ImportResult>())!;

        await Assert.That((await UploadAsync(bob, group.Id, "x.csv", Csv())).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/multi-currency-accounts/{group.Id}/source-files")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.DeleteAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await SourceFilesAsync(anna, group.Id)).Count().IsEqualTo(1);
    }

    // ---- helpers ----

    private const string OctoberSample = "account-statement_2026-10-01_2026-10-31_en-us_6a51a3.csv";
    private const string NovemberSample = "account-statement_2026-11-01_2026-11-19_en-us_affca4.csv";

    private static byte[] Sample(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Revolut", name));

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

    private static string Csv(params string[] rows) => string.Join('\n', [Header, .. rows]) + "\n";

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid groupId, string fileName, string content) =>
        await UploadAsync(client, $"/api/multi-currency-accounts/{groupId}/imports", fileName, Encoding.UTF8.GetBytes(content));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        return await client.PostAsync(url, form);
    }

    private static async Task<Group> CreateGroupAsync(HttpClient client, string name, decimal openingBalance,
        string openingDate, object? owner = null)
    {
        owner ??= new { kind = "member", memberId = await MemberIdAsync(client) };
        var res = await client.PostAsJsonAsync("/api/multi-currency-accounts", new
        {
            name, owner, integrationKey = "revolut", mainCurrency = "CHF", openingBalance, openingDate,
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Group>())!;
    }

    private static async Task<Group> GetGroupAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{id}"))!;

    private static async Task<List<TransactionDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private record Me(Guid Id);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record BalanceWarning(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance);

    private record Account(Guid Id, string Name, OwnerDto Owner, string Currency, decimal OpeningBalance,
        DateOnly OpeningDate, decimal Balance, BalanceWarning? BalanceWarning, Guid? MultiCurrencyAccountId);

    private record Group(Guid Id, string Name, string Slug, OwnerDto Owner, List<Account> Accounts, decimal? TotalBalance);

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
