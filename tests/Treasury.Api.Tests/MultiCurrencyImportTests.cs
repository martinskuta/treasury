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

    // ---- helpers ----

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

    private record Group(Guid Id, string Name, string Slug, OwnerDto Owner, List<Account> Accounts);

    private record SourceFileDto(Guid Id, Guid? AccountId, Guid? MultiCurrencyAccountId, string FileName,
        int TransactionCount);

    private record CreatedAccount(Guid Id, string Name, string Currency);

    private record ImportResult(SourceFileDto SourceFile, int ImportedCount, int SkippedCount, List<string> Warnings,
        List<CreatedAccount> CreatedAccounts);

    private record SourceFileRef(Guid Id, string FileName);

    private record LinkRef(string Kind, Guid TransactionId);

    private record TransactionDto(Guid Id, Guid? AccountId, DateOnly Date, decimal Amount, string Currency,
        string Type, string Description, string? Counterparty, SourceFileRef? SourceFile, LinkRef? Link);
}
