using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Treasury.Api.Tests.Integrations;

namespace Treasury.Api.Tests;

/// Seam 1: the postfinance Integration end to end, uploading a (synthetic) Kontoauszug PDF into a postfinance
/// Account.
public class PostFinanceImportTests : ApiTestBase
{
    [Test]
    public async Task Uploading_the_kontoauszug_imports_every_row_and_matches_the_closing_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var integrations = (await anna.GetFromJsonAsync<List<IntegrationDto>>("/api/integrations"))!;
        await Assert.That(integrations).Contains(new IntegrationDto("postfinance", "PostFinance", true));
        // The statement opens with 1'000.00 at the start of October.
        var account = await CreateAccountAsync(anna, "CH93 0076 2011 6238 5295 7", 1000.00m, "2026-09-30");

        var res = await UploadAsync(anna, account.Id, "kontoauszug-2026-10.pdf", PostFinanceTests.October().Build());

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(7);
        await Assert.That(result.Warnings).IsEmpty();
        var after = await GetAccountAsync(anna, account.Id);
        await Assert.That(after.Balance).IsEqualTo(2911.70m);
        await Assert.That(after.BalanceWarning).IsNull();
        var transactions = (await anna.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;
        await Assert.That(transactions).Count().IsEqualTo(7);
        var salary = transactions.Single(t => t.Amount == 2000.00m);
        await Assert.That(salary.Date).IsEqualTo(new DateOnly(2026, 10, 23));
        await Assert.That(salary.Type).IsEqualTo("income");
        await Assert.That(salary.Counterparty).IsEqualTo("EXAMPLE EMPLOYER AG");
        await Assert.That(salary.Description).StartsWith("GUTSCHRIFT\nCH0000000000000000003\nABSENDER:");
        await Assert.That(salary.Reference).IsNull();
        await Assert.That(salary.SourceFile!.FileName).IsEqualTo("kontoauszug-2026-10.pdf");
    }

    [Test]
    public async Task Uploading_the_same_kontoauszug_again_imports_nothing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, null, 1000.00m, "2026-09-30");
        await UploadAsync(anna, account.Id, "kontoauszug-2026-10.pdf", PostFinanceTests.October().Build());

        var res = await UploadAsync(anna, account.Id, "kontoauszug-2026-10-again.pdf", PostFinanceTests.October().Build());

        var transactions = (await anna.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;
        await Assert.That(transactions).Count().IsEqualTo(7);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(2911.70m);
        await Assert.That(res.IsSuccessStatusCode).IsTrue();
    }

    [Test]
    public async Task A_camt053_file_is_rejected_by_a_postfinance_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, null, 0m, "2026-09-30");

        var res = await UploadAsync(anna, account.Id, "camt053-2026-10-31.xml",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Tkb", "camt053-2026-10-31.xml")));

        await Assert.That(res.IsSuccessStatusCode).IsFalse();
        await Assert.That(await res.Content.ReadAsStringAsync()).Contains("Kontoauszug");
        await Assert.That((await anna.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!).IsEmpty();
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid accountId, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/accounts/{accountId}/imports", form);
    }

    private static async Task<Account> CreateAccountAsync(HttpClient client, string? identifier, decimal openingBalance,
        string openingDate)
    {
        var me = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!;
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "PostFinance", owner = new { kind = "member", memberId = me.Id }, integrationKey = "postfinance",
            currency = "CHF", openingBalance, openingDate, identifiers = identifier is null ? null : new[] { identifier },
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private record Me(Guid Id);

    private record IntegrationDto(string Key, string Name, bool CanProvideIdentifier);

    private record SourceFileDto(Guid Id, string FileName);

    private record ImportResult(SourceFileDto SourceFile, int ImportedCount, List<string> Warnings);

    private record BalanceWarning(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance);

    private record Account(Guid Id, decimal Balance, BalanceWarning? BalanceWarning);

    private record TransactionDto(Guid Id, DateOnly Date, decimal Amount, string Type, string Description,
        string? Counterparty, string? Reference, SourceFileDto? SourceFile);
}
