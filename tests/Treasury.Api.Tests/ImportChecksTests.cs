using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// #11: the identifier check (wrong-folder protection) and the closing-balance warning.
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
        await Assert.That(await File.ReadAllTextAsync(failed + ".error.txt")).Contains("CH93 0076 2011 6238 5295 7");
        await Assert.That(Directory.GetFiles(Path.Combine(App.IngestRoot, account.Slug, "processed"))).IsEmpty();
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
    public async Task The_identifier_check_is_skipped_when_the_account_has_no_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB");

        var result = await UploadOkAsync(anna, account.Id, "any.txt",
            new TestStatement().Identifier("CH44 3199 9123 0008 8901 2").Row("2026-11-18", -12.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
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
            openingDate, identifier,
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

    private record Account(Guid Id, string Slug, decimal Balance, BalanceWarning? BalanceWarning);
}
