using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// #11: the Identifier check (wrong-folder protection, with Identifiers learned on import: ADR-0002) and the
/// closing-balance warning.
public class ImportChecksTests : ApiTestBase
{
    [Test]
    public async Task A_statement_for_another_identifier_goes_to_failed_with_a_note_and_imports_nothing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", identifier: "CH93 0076 2011 6238 5295 7");
        var statement = new TestStatement()
            .Identifier("CH44 3199 9123 0008 8901 2")
            .Row("2026-11-18", -12.50m, "Coffee");

        var res = await UploadAsync(anna, account.Id, "wrong.txt", statement);

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"][0]).Contains("CH44 3199 9123 0008 8901 2");
        await Assert.That(await ListAsync(anna)).IsEmpty();
        await Assert.That(await anna.GetFromJsonAsync<List<SourceFileDto>>($"/api/accounts/{account.Id}/source-files")).IsEmpty();
        var failed = Path.Combine(App.IngestRoot, account.Slug, "failed", "wrong.txt");
        await Assert.That(await File.ReadAllTextAsync(failed)).IsEqualTo(statement.ToString());
        await Assert.That(await File.ReadAllTextAsync(failed + ".error.txt")).Contains("CH93 0076 2011 6238 5295 7")
            .And.Contains("add it to its Identifiers");
        await Assert.That(Directory.GetFiles(Path.Combine(App.IngestRoot, account.Slug, "processed"))).IsEmpty();
        // Nothing is learned from a rejected statement.
        await Assert.That((await GetAccountAsync(anna, account.Id)).Identifiers).IsEquivalentTo(["CH93 0076 2011 6238 5295 7"]);
    }

    [Test]
    [Arguments("CH93 0076 2011 6238 5295 7", "ch9300762011623852957")]
    [Arguments("CH9300762011623852957", "CH93-0076-2011-6238-5295-7")]
    [Arguments("1234", "5413 **** **** 1234")]
    [Arguments("5413 XXXX XXXX 1234", "1234")]
    [Arguments("**** 1234", "5413000000001234")]
    public async Task A_statement_whose_identifier_matches_after_normalising_is_imported(string accountId, string statementId)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", identifier: accountId);

        var result = await UploadOkAsync(anna, account.Id, "ok.txt",
            new TestStatement().Identifier(statementId).Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
    }

    [Test]
    [Arguments("1234", "5413 **** **** 9234")]
    [Arguments("CH93 0076 2011 6238 5295 7", "CH93 0076 2011 6238 5295 8")]
    [Arguments("CH93 0076 2011 6238 5295 7", "52957")]
    public async Task Near_misses_are_rejected(string accountId, string statementId)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", identifier: accountId);

        var res = await UploadAsync(anna, account.Id, "wrong.txt",
            new TestStatement().Identifier(statementId).Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task The_first_import_into_an_account_without_identifiers_gives_it_those_the_statement_shows()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB");

        var result = await UploadOkAsync(anna, account.Id, "any.txt",
            new TestStatement().Identifier("CH44 3199 9123 0008 8901 2").Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Identifiers).IsEquivalentTo(["CH44 3199 9123 0008 8901 2"]);
        await Assert.That(result.Warnings).IsEquivalentTo([
            "The Account \"TKB\" now also has the Identifier CH44 3199 9123 0008 8901 2, which this statement shows.",
        ]);
    }

    [Test]
    public async Task A_statement_showing_a_known_and_a_new_identifier_adds_the_new_one()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Revolut", identifier: "GB33 BUKB 2020 1555 5555 55");

        // A statement over the period in which the bank moved the account to a new IBAN shows both.
        var result = await UploadOkAsync(anna, account.Id, "2018-2026.txt", new TestStatement()
            .Identifier("LT121000011101001000").Identifier("GB33BUKB20201555555555").Row("2026-11-18", -12.50m, "Coffee"));
        // From then on, a statement showing only the new IBAN is accepted.
        var newer = await UploadOkAsync(anna, account.Id, "2026-12.txt",
            new TestStatement().Identifier("LT121000011101001000").Row("2026-12-02", -3m, "Tea"));

        await Assert.That(result.Warnings).Count().IsEqualTo(1);
        await Assert.That(newer.ImportedCount).IsEqualTo(1);
        await Assert.That(newer.Warnings).IsEmpty();
        await Assert.That((await GetAccountAsync(anna, account.Id)).Identifiers)
            .IsEquivalentTo(["GB33 BUKB 2020 1555 5555 55", "LT121000011101001000"]);
    }

    [Test]
    public async Task A_statement_whose_rows_are_all_imported_already_still_adds_its_new_identifiers()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Revolut", identifier: "GB33BUKB20201555555555");
        await UploadOkAsync(anna, account.Id, "2018.txt", new TestStatement()
            .Identifier("GB33BUKB20201555555555").Row("2018-03-21", 100m, "Top-up"));

        var again = await UploadOkAsync(anna, account.Id, "2018-again.txt", new TestStatement()
            .Identifier("GB33BUKB20201555555555").Identifier("LT121000011101001000").Row("2018-03-21", 100m, "Top-up"));

        await Assert.That(again.ImportedCount).IsEqualTo(0);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Identifiers)
            .IsEquivalentTo(["GB33BUKB20201555555555", "LT121000011101001000"]);
    }

    [Test]
    public async Task A_statement_that_fails_after_the_identifier_check_adds_no_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB");

        // The row is in EUR, but the Account is in CHF.
        var res = await UploadAsync(anna, account.Id, "eur.txt", new TestStatement()
            .Identifier("CH44 3199 9123 0008 8901 2").Row("2026-11-18", -12.50m, "Coffee", currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Identifiers).IsEmpty();
    }

    [Test]
    public async Task A_statement_showing_an_identifier_of_another_account_is_rejected_and_names_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        await CreateAccountAsync(anna, "TKB", identifier: "CH44 3199 9123 0008 8901 2");
        var other = await CreateAccountAsync(anna, "New account");

        var res = await UploadAsync(anna, other.Id, "tkb.txt",
            new TestStatement().Identifier("CH4431999123000889012").Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"][0]).Contains("CH4431999123000889012").And.Contains("the Account \"TKB\"");
        await Assert.That((await GetAccountAsync(anna, other.Id)).Identifiers).IsEmpty();
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Another_members_personal_account_holding_the_identifier_is_not_named()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        await CreateAccountAsync(bob, "Bob's secret savings", identifier: "CH44 3199 9123 0008 8901 2");
        var res = await anna.PostAsJsonAsync("/api/accounts", new
        {
            name = "Joint", owner = new { kind = "family" }, integrationKey = "test", currency = "CHF",
            openingBalance = 0m, openingDate = "2026-10-31",
        });
        var joint = (await res.Content.ReadFromJsonAsync<Account>())!;

        var upload = await UploadAsync(anna, joint.Id, "bobs.txt",
            new TestStatement().Identifier("CH44 3199 9123 0008 8901 2").Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(upload.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await upload.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors["file"][0]).Contains("another Member's account")
            .And.DoesNotContain("secret");
    }

    [Test]
    public async Task The_identifier_check_is_skipped_when_the_statement_has_no_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", identifier: "CH93 0076 2011 6238 5295 7");

        var result = await UploadOkAsync(anna, account.Id, "noid.txt", new TestStatement().Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
    }

    [Test]
    public async Task The_identifier_check_is_skipped_for_an_integration_that_cannot_provide_one()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash book", integrationKey: "test-noid");

        var result = await UploadOkAsync(anna, account.Id, "noid.txt",
            new TestStatement().Identifier("CH44 3199 9123 0008 8901 2").Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
    }

    [Test]
    public async Task A_closing_balance_that_differs_from_the_calculated_balance_shows_a_warning_on_the_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 1000m, openingDate: "2026-10-31");

        // Statement says 950 on Nov 30, but only -30 of movements are in it (a row is missing): calculated 970.
        var result = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", 950m)
            .Row("2026-11-10", -30m, "Groceries"));

        await Assert.That(result.Warnings).Count().IsEqualTo(1);
        await Assert.That(result.Warnings[0]).Contains("950");
        var warning = (await GetAccountAsync(anna, account.Id)).BalanceWarning;
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Date).IsEqualTo(new DateOnly(2026, 11, 30));
        await Assert.That(warning.StatementBalance).IsEqualTo(950m);
        await Assert.That(warning.CalculatedBalance).IsEqualTo(970m);
        await Assert.That(warning.SourceFile.Id).IsEqualTo(result.SourceFile.Id);
        await Assert.That(warning.SourceFile.FileName).IsEqualTo("november.txt");
        var listed = (await anna.GetFromJsonAsync<List<Account>>("/api/accounts"))!.Single();
        await Assert.That(listed.BalanceWarning).IsEqualTo(warning);
    }

    [Test]
    public async Task A_matching_closing_balance_shows_no_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 1000m, openingDate: "2026-10-31");

        var result = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", 970m)
            .Row("2026-10-30", -999m, "Already in the opening balance")
            .Row("2026-11-10", -30m, "Groceries")
            .Row("2026-12-01", -5m, "After the closing date"));

        await Assert.That(result.Warnings).IsEmpty();
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNull();
    }

    [Test]
    public async Task The_warning_clears_once_the_missing_data_is_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 1000m, openingDate: "2026-09-30");
        // November's statement arrives first; October's movements are still missing.
        await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", 900m)
            .Row("2026-11-10", -30m, "Groceries"));
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNotNull();

        var october = await UploadOkAsync(anna, account.Id, "october.txt", new TestStatement()
            .Closing("2026-10-31", 930m)
            .Row("2026-10-05", -70m, "Rent share"));

        await Assert.That(october.Warnings).IsEmpty();
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNull();
    }

    [Test]
    public async Task The_warning_follows_the_most_recent_statement()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 100m, openingDate: "2026-09-30");
        await UploadOkAsync(anna, account.Id, "october.txt", new TestStatement()
            .Closing("2026-10-31", 90m)
            .Row("2026-10-05", -10m, "Fee"));
        // An extra October row the bank never booked (not a duplicate: its description differs),
        // so the November closing balance no longer matches.
        var doubled = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", 80m)
            .Row("2026-10-05", -10m, "Fee correction")
            .Row("2026-11-05", -10m, "Fee"));

        var warning = (await GetAccountAsync(anna, account.Id)).BalanceWarning;

        await Assert.That(doubled.Warnings).Count().IsEqualTo(1);
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.SourceFile.FileName).IsEqualTo("november.txt");
        await Assert.That(warning.CalculatedBalance).IsEqualTo(70m);
    }

    [Test]
    public async Task Undoing_the_import_that_caused_the_mismatch_clears_the_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 100m, openingDate: "2026-09-30");
        await UploadOkAsync(anna, account.Id, "october.txt", new TestStatement()
            .Closing("2026-10-31", 90m)
            .Row("2026-10-05", -10m, "Fee"));
        var bad = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", 50m)
            .Row("2026-11-05", -10m, "Fee"));
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNotNull();

        var undo = await anna.DeleteAsync($"/api/source-files/{bad.SourceFile.Id}");

        await Assert.That(undo.IsSuccessStatusCode).IsTrue();
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNull();
    }

    [Test]
    public async Task A_manual_transaction_that_fills_the_gap_clears_the_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 0m, openingDate: "2026-10-31");
        await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Closing("2026-11-30", -50m)
            .Row("2026-11-10", -30m, "Groceries"));

        var added = await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = account.Id, date = "2026-11-20", amount = -20m, description = "Cash withdrawal" });

        await Assert.That(added.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNull();
    }

    [Test]
    public async Task A_closing_balance_before_the_opening_date_is_not_compared()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", openingBalance: 500m, openingDate: "2026-11-01");

        var result = await UploadOkAsync(anna, account.Id, "october.txt", new TestStatement()
            .Closing("2026-10-31", 123m)
            .Row("2026-10-10", -30m, "Groceries"));

        await Assert.That(result.Warnings).IsEmpty();
        await Assert.That((await GetAccountAsync(anna, account.Id)).BalanceWarning).IsNull();
    }

    [Test]
    public async Task Another_members_view_of_a_family_account_shows_the_same_warning()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var res = await anna.PostAsJsonAsync("/api/accounts", new
        {
            name = "Joint", owner = new { kind = "family" }, integrationKey = "test", currency = "CHF",
            openingBalance = 0m, openingDate = "2026-10-31",
        });
        var joint = (await res.Content.ReadFromJsonAsync<Account>())!;
        await UploadOkAsync(anna, joint.Id, "joint.txt", new TestStatement()
            .Closing("2026-11-30", -100m)
            .Row("2026-11-10", -30m, "Groceries"));

        var seenByBob = await GetAccountAsync(bob, joint.Id);

        await Assert.That(seenByBob.BalanceWarning).IsNotNull();
        await Assert.That(seenByBob.BalanceWarning!.CalculatedBalance).IsEqualTo(-30m);
    }

    // ---- helpers ----

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

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, string? identifier = null,
        string integrationKey = "test", decimal openingBalance = 0m, string openingDate = "2026-01-01")
    {
        var me = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!;
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner = new { kind = "member", memberId = me.Id }, integrationKey, currency = "CHF", openingBalance,
            openingDate, identifiers = identifier is null ? null : new[] { identifier },
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private static async Task<List<TransactionDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;

    private record Me(Guid Id);

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record SourceFileDto(Guid Id, string FileName);

    private record ImportResult(SourceFileDto SourceFile, int ImportedCount, List<string> Warnings);

    private record TransactionDto(Guid Id, decimal Amount, string Description);

    private record BalanceWarning(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance, SourceFileDto SourceFile);

    private record Account(Guid Id, string Slug, decimal Balance, BalanceWarning? BalanceWarning, List<string> Identifiers);
}
