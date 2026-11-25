using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Seam 1: the tkb Integration end to end, uploading a TKB camt.053 download into a tkb Account.
public class TkbImportTests : ApiTestBase
{
    private const string Iban = "CH1889144434487348535";

    [Test]
    public async Task Uploading_the_tkb_camt053_zip_imports_every_booked_payment_and_matches_the_closing_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var integrations = (await anna.GetFromJsonAsync<List<IntegrationDto>>("/api/integrations"))!;
        await Assert.That(integrations).Contains(new IntegrationDto("tkb", "TKB", true));
        // The statement opens with 999'976.00 at the start of October.
        var account = await CreateTkbAccountAsync(anna, "CH18 8914 4434 4873 4853 5", 999976.00m, "2026-09-30");

        var res = await UploadAsync(anna, account.Id, "camt053-2026-10-31.zip",
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Integrations", "Tkb", "camt053-2026-10-31.zip")));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<ImportResult>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(55);
        await Assert.That(result.Warnings).IsEmpty();
        var after = await GetAccountAsync(anna, account.Id);
        await Assert.That(after.Balance).IsEqualTo(1023381.13m);
        await Assert.That(after.BalanceWarning).IsNull();
        var transactions = (await anna.GetFromJsonAsync<List<TransactionDto>>("/api/transactions"))!;
        await Assert.That(transactions).Count().IsEqualTo(55);
        var card = transactions.Single(t => t.Reference == "BKUZ169C7YTPU0SR");
        await Assert.That(card.Date).IsEqualTo(new DateOnly(2026, 10, 1));
        await Assert.That(card.Amount).IsEqualTo(-4731.46m);
        await Assert.That(card.Type).IsEqualTo("expense");
        await Assert.That(card.Counterparty).IsEqualTo("Kulas - Farrell, Marksfurt");
        await Assert.That(card.Description).StartsWith("Card Payment\nKulas - Farrell, Marksfurt");
        await Assert.That(card.SourceFile!.FileName).IsEqualTo("camt053-2026-10-31.zip");
    }

    // ---- helpers ----

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid accountId, string fileName, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        form.Add(file, "file", fileName);
        return await client.PostAsync($"/api/accounts/{accountId}/imports", form);
    }

    private static async Task<Account> CreateTkbAccountAsync(HttpClient client, string identifier, decimal openingBalance,
        string openingDate)
    {
        var me = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!;
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "TKB", owner = new { kind = "member", memberId = me.Id }, integrationKey = "tkb", currency = "CHF",
            openingBalance, openingDate, identifiers = new[] { identifier },
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
