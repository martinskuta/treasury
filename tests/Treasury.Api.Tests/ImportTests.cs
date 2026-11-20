using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

public class ImportTests : ApiTestBase
{
    [Test]
    public async Task Uploading_a_statement_imports_its_rows_as_transactions_in_the_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var statement = new TestStatement()
            .Row("2026-11-18", -12.50m, "COOP-1234 ZURICH", counterparty: "Coop", reference: "R-1")
            .Row("2026-11-19", 5000m, "SALARY NOVEMBER");

        var res = await UploadAsync(anna, account.Id, "november.txt", statement);

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(2);
        var list = await ListAsync(anna);
        await Assert.That(list).Count().IsEqualTo(2);
        var coop = list.Single(t => t.Description == "COOP-1234 ZURICH");
        await Assert.That(coop.AccountId).IsEqualTo(account.Id);
        await Assert.That(coop.Date).IsEqualTo(new DateOnly(2026, 11, 18));
        await Assert.That(coop.Amount).IsEqualTo(-12.50m);
        await Assert.That(coop.Currency).IsEqualTo("CHF");
        await Assert.That(coop.Type).IsEqualTo("expense");
        await Assert.That(coop.Counterparty).IsEqualTo("Coop");
        await Assert.That(coop.Reference).IsEqualTo("R-1");
        await Assert.That(coop.Origin).IsEqualTo("imported");
        await Assert.That(coop.Owner.Kind).IsEqualTo("member");
        var salary = list.Single(t => t.Description == "SALARY NOVEMBER");
        await Assert.That(salary.Type).IsEqualTo("income");
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(4987.50m);
    }

    [Test]
    public async Task The_source_file_is_kept_in_processed_and_each_imported_transaction_links_to_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var statement = new TestStatement()
            .Row("2026-11-18", -12.50m, "Coffee")
            .Row("2026-11-19", -30m, "Books");

        var result = await UploadOkAsync(anna, account.Id, "november.txt", statement);

        foreach (var t in await ListAsync(anna))
        {
            await Assert.That(t.SourceFile).IsNotNull();
            await Assert.That(t.SourceFile!.Id).IsEqualTo(result.SourceFile.Id);
            await Assert.That(t.SourceFile.FileName).IsEqualTo("november.txt");
        }
        var sourceFile = (await anna.GetFromJsonAsync<SourceFile>($"/api/source-files/{result.SourceFile.Id}"))!;
        await Assert.That(sourceFile.AccountId).IsEqualTo(account.Id);
        await Assert.That(sourceFile.FileName).IsEqualTo("november.txt");
        await Assert.That(sourceFile.TransactionCount).IsEqualTo(2);
        await Assert.That(await anna.GetStringAsync($"/api/source-files/{result.SourceFile.Id}/content"))
            .IsEqualTo(statement.ToString());
        var listed = (await anna.GetFromJsonAsync<List<SourceFile>>($"/api/accounts/{account.Id}/source-files"))!;
        await Assert.That(listed.Select(f => f.Id)).IsEquivalentTo([result.SourceFile.Id]);
        var processed = Path.Combine(App.IngestRoot, account.Slug, "processed", "november.txt");
        await Assert.That(File.Exists(processed)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(processed)).IsEqualTo(statement.ToString());
    }

    [Test]
    public async Task Manual_transactions_have_no_source_file()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));

        await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = account.Id, date = "2026-11-18", amount = -5m, description = "Ice cream" });

        await Assert.That((await ListAsync(anna)).Single().SourceFile).IsNull();
    }

    [Test]
    public async Task Another_members_source_files_are_not_visible()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var result = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement().Row("2026-11-18", -1m, "X"));

        await Assert.That((await bob.GetAsync($"/api/source-files/{result.SourceFile.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/source-files/{result.SourceFile.Id}/content")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await bob.GetAsync($"/api/accounts/{account.Id}/source-files")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await UploadAsync(bob, account.Id, "x.txt", new TestStatement().Row("2026-11-18", -1m, "X"))).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_file_the_integration_cannot_read_goes_to_failed_with_an_error_note_and_nothing_is_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var broken = new TestStatement()
            .Row("2026-11-18", -12.50m, "Coffee")
            .Line("this is not a statement line");

        var res = await UploadAsync(anna, account.Id, "broken.txt", broken);

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"][0]).Contains("line 2");
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await anna.GetFromJsonAsync<List<SourceFile>>($"/api/accounts/{account.Id}/source-files")).IsEmpty();
        var folder = Path.Combine(App.IngestRoot, account.Slug);
        var failed = Path.Combine(folder, "failed", "broken.txt");
        await Assert.That(await File.ReadAllTextAsync(failed)).IsEqualTo(broken.ToString());
        await Assert.That(await File.ReadAllTextAsync(failed + ".error.txt")).Contains("line 2");
        await Assert.That(Directory.GetFiles(Path.Combine(folder, "processed"))).IsEmpty();
    }

    [Test]
    public async Task A_statement_in_another_currency_than_the_account_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Revolut EUR", await MeAsync(anna), currency: "EUR");

        var res = await UploadAsync(anna, account.Id, "chf.txt",
            new TestStatement().Row("2026-11-18", -12.50m, "Coffee").Row("2026-11-18", -3m, "Tea", currency: "CHF"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(File.Exists(Path.Combine(App.IngestRoot, account.Slug, "failed", "chf.txt.error.txt"))).IsTrue();
    }

    [Test]
    public async Task Uploading_a_file_with_the_same_name_again_keeps_both_files()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));

        await UploadOkAsync(anna, account.Id, "export.txt", new TestStatement().Row("2026-11-18", -1m, "A"));
        await UploadOkAsync(anna, account.Id, "export.txt", new TestStatement().Row("2026-11-19", -2m, "B"));

        var processed = Directory.GetFiles(Path.Combine(App.IngestRoot, account.Slug, "processed")).Select(f => Path.GetFileName(f)!);
        await Assert.That(processed).IsEquivalentTo(["export.txt", "export (2).txt"]);
    }

    [Test]
    public async Task Uploading_without_a_file_is_a_validation_error()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));

        var res = await anna.PostAsync($"/api/accounts/{account.Id}/imports", new MultipartFormDataContent());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await res.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains("file");
    }

    [Test]
    public async Task A_foreign_currency_purchase_shows_its_original_amount_and_currency_next_to_the_amount_charged()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Card", await MeAsync(anna));

        await UploadOkAsync(anna, account.Id, "card.txt", new TestStatement()
            .Row("2026-11-18", -46.20m, "TRATTORIA ROMA", originalAmount: -48.00m, originalCurrency: "eur")
            .Row("2026-11-18", -5m, "KIOSK"));

        var list = await ListAsync(anna);
        var trattoria = list.Single(t => t.Description == "TRATTORIA ROMA");
        await Assert.That(trattoria.Amount).IsEqualTo(-46.20m);
        await Assert.That(trattoria.Currency).IsEqualTo("CHF");
        await Assert.That(trattoria.OriginalAmount).IsEqualTo(-48.00m);
        await Assert.That(trattoria.OriginalCurrency).IsEqualTo("EUR");
        var kiosk = list.Single(t => t.Description == "KIOSK");
        await Assert.That(kiosk.OriginalAmount).IsNull();
        await Assert.That(kiosk.OriginalCurrency).IsNull();
    }

    [Test]
    [Arguments("date")]
    [Arguments("amount")]
    [Arguments("description")]
    public async Task The_facts_of_an_imported_transaction_are_read_only(string fact)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        await UploadOkAsync(anna, account.Id, "card.txt", new TestStatement()
            .Row("2026-11-18", -46.20m, "TRATTORIA ROMA", originalAmount: -48.00m, originalCurrency: "EUR"));
        var imported = (await ListAsync(anna)).Single();
        var change = new Dictionary<string, object>
        {
            ["date"] = "2026-11-01", ["amount"] = -1m, ["description"] = "Something else",
        };

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{imported.Id}",
            new Dictionary<string, object> { [fact] = change[fact], ["note"] = "sneaky" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await res.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains("transaction");
        var after = (await ListAsync(anna)).Single();
        await Assert.That(after).IsEqualTo(imported);
    }

    [Test]
    public async Task The_original_amount_of_an_imported_transaction_cannot_be_changed_and_it_cannot_be_deleted()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        await UploadOkAsync(anna, account.Id, "card.txt", new TestStatement()
            .Row("2026-11-18", -46.20m, "TRATTORIA ROMA", originalAmount: -48.00m, originalCurrency: "EUR"));
        var imported = (await ListAsync(anna)).Single();

        await anna.PatchAsJsonAsync($"/api/transactions/{imported.Id}",
            new { originalAmount = -1m, originalCurrency = "USD", currency = "EUR" });
        var delete = await anna.DeleteAsync($"/api/transactions/{imported.Id}");

        await Assert.That(delete.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var after = (await ListAsync(anna)).Single();
        await Assert.That(after.OriginalAmount).IsEqualTo(-48.00m);
        await Assert.That(after.OriginalCurrency).IsEqualTo("EUR");
        await Assert.That(after.Currency).IsEqualTo("CHF");
    }

    [Test]
    public async Task The_display_name_and_note_of_an_imported_transaction_can_be_edited_and_the_original_description_stays()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        await UploadOkAsync(anna, account.Id, "tkb.txt", new TestStatement().Row("2026-11-18", -12.50m, "COOP-1234 ZURICH HB"));
        var imported = (await ListAsync(anna)).Single();
        await Assert.That(imported.DisplayName).IsNull();

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{imported.Id}",
            new { displayName = "  Coop groceries ", note = "Party snacks" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var edited = (await ListAsync(anna)).Single();
        await Assert.That(edited.DisplayName).IsEqualTo("Coop groceries");
        await Assert.That(edited.Note).IsEqualTo("Party snacks");
        await Assert.That(edited.Description).IsEqualTo("COOP-1234 ZURICH HB");
        await Assert.That(edited.Amount).IsEqualTo(-12.50m);

        await anna.PatchAsJsonAsync($"/api/transactions/{imported.Id}", new { displayName = "" });
        var cleared = (await ListAsync(anna)).Single();
        await Assert.That(cleared.DisplayName).IsNull();
        await Assert.That(cleared.Note).IsEqualTo("Party snacks");
    }

    [Test]
    public async Task A_patch_without_a_display_name_keeps_it_and_an_overlong_one_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        await UploadOkAsync(anna, account.Id, "tkb.txt", new TestStatement().Row("2026-11-18", -12.50m, "COOP"));
        var id = (await ListAsync(anna)).Single().Id;
        await anna.PatchAsJsonAsync($"/api/transactions/{id}", new { displayName = "Coop" });

        await anna.PatchAsJsonAsync($"/api/transactions/{id}", new { note = "x" });
        var tooLong = await anna.PatchAsJsonAsync($"/api/transactions/{id}", new { displayName = new string('a', 201) });

        await Assert.That(tooLong.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await tooLong.Content.ReadFromJsonAsync<ValidationProblem>())!.Errors.Keys).Contains("displayName");
        await Assert.That((await ListAsync(anna)).Single().DisplayName).IsEqualTo("Coop");
    }

    [Test]
    public async Task A_manual_transaction_can_have_a_display_name_too()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var created = await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = account.Id, date = "2026-11-18", amount = -5m, description = "ice cream stand" });
        var id = (await created.Content.ReadFromJsonAsync<Transaction>())!.Id;

        var res = await anna.PatchAsJsonAsync($"/api/transactions/{id}", new { displayName = "Gelato" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var t = (await ListAsync(anna)).Single();
        await Assert.That(t.DisplayName).IsEqualTo("Gelato");
        await Assert.That(t.Description).IsEqualTo("ice cream stand");
    }

    [Test]
    public async Task Imported_transactions_in_a_family_account_belong_to_the_family()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);

        await UploadOkAsync(anna, joint.Id, "joint.txt", new TestStatement().Row("2026-11-18", -80m, "MIGROS"));

        var seenByBob = (await ListAsync(bob)).Single();
        await Assert.That(seenByBob.Owner.Kind).IsEqualTo("family");
        await Assert.That(seenByBob.SourceFile).IsNotNull();
        await Assert.That((await bob.GetAsync($"/api/source-files/{seenByBob.SourceFile!.Id}/content")).StatusCode)
            .IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task The_generic_csv_integration_is_available_and_imports_through_upload()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var integrations = (await anna.GetFromJsonAsync<List<IntegrationDto>>("/api/integrations"))!;
        await Assert.That(integrations).Contains(new IntegrationDto("generic-csv", "Generic CSV", false));
        var account = await CreateAccountAsync(anna, "Postfinance", await MeAsync(anna), integrationKey: "generic-csv");

        await UploadOkAsync(anna, account.Id, "export.csv",
            "date,amount,description,counterparty\n2026-11-18,-3.20,CAFE CENTRAL,Café Central\n");

        var t = (await ListAsync(anna)).Single();
        await Assert.That(t.Description).IsEqualTo("CAFE CENTRAL");
        await Assert.That(t.Counterparty).IsEqualTo("Café Central");
        await Assert.That(t.Amount).IsEqualTo(-3.20m);
    }

    [Test]
    public async Task Importing_old_transactions_backfills_the_exchange_rates_they_need()
    {
        App.ExchangeRates.Publish("2026-11-18", chf: 0.9266m);
        App.ExchangeRates.Publish("2025-06-02", chf: 0.9400m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var euros = await CreateAccountAsync(anna, "Euro card", await MeAsync(anna), currency: "EUR");
        await UploadOkAsync(anna, euros.Id, "recent.txt", new TestStatement().Row("2026-11-18", -100m, "Recent", currency: "EUR"));
        await EventuallyAsync(anna, l => l.Single().ChfAmount is not null);

        await UploadOkAsync(anna, euros.Id, "old.txt", new TestStatement()
            .Row("2025-06-04", -10m, "Later old one", currency: "EUR")
            .Row("2025-06-03", -50m, "Old one", currency: "EUR"));

        var list = await EventuallyAsync(anna, l => l.All(t => t.ChfAmount is not null));
        await Assert.That(list.Single(t => t.Description == "Old one").ChfAmount).IsEqualTo(-47.00m);
    }

    // ---- helpers ----

    private static async Task<List<Transaction>> EventuallyAsync(HttpClient client, Func<List<Transaction>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var list = await ListAsync(client);
            if (done(list)) return list;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(25);
        }
    }

    private static async Task<ImportResult> UploadOkAsync(HttpClient client, Guid accountId, string fileName, object content)
    {
        var res = await UploadAsync(client, accountId, fileName, content);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid accountId, string fileName,
        object content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content.ToString()!));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/accounts/{accountId}/imports", form);
    }

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner,
        string integrationKey = "test", string currency = "CHF", string openingDate = "2026-01-01")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey, currency, openingBalance = 0m, openingDate,
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private record IntegrationDto(string Key, string Name, bool CanProvideIdentifier);

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id, string UserName);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name, string Slug, OwnerDto Owner, decimal Balance);

    private record SourceFileRef(Guid Id, string FileName);

    private record SourceFile(Guid Id, Guid AccountId, string FileName, DateTimeOffset ImportedAt, int TransactionCount);

    private record ImportResult(SourceFile SourceFile, int ImportedCount);

    private record Transaction(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string Currency, decimal? ChfAmount, string Type,
        string Description, string? DisplayName, string? Counterparty, string? Reference, decimal? OriginalAmount,
        string? OriginalCurrency, string? Note, OwnerDto Owner, string Origin, SourceFileRef? SourceFile);
}
