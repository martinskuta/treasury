using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// Transfers and Contributions: pairs of Transactions on two of the Family's Accounts (GLOSSARY).
public class MatchingTests : ApiTestBase
{
    [Test]
    public async Task Imported_same_currency_pair_between_accounts_of_the_same_owner_becomes_a_transfer()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var checking = await CreateAccountAsync(anna, "Checking", me);
        var savings = await CreateAccountAsync(anna, "Savings", me);
        await UploadOkAsync(anna, checking.Id, new TestStatement().Row("2026-11-16", -500m, "TO SAVINGS"));

        await UploadOkAsync(anna, savings.Id, new TestStatement().Row("2026-11-18", 500m, "FROM CHECKING"));

        var list = await ListAsync(anna);
        var outgoing = list.Single(t => t.Description == "TO SAVINGS");
        var incoming = list.Single(t => t.Description == "FROM CHECKING");
        await Assert.That(outgoing.Type).IsEqualTo("transfer");
        await Assert.That(incoming.Type).IsEqualTo("transfer");
        await Assert.That(outgoing.Link!.Kind).IsEqualTo("transfer");
        await Assert.That(outgoing.Link.TransactionId).IsEqualTo(incoming.Id);
        await Assert.That(incoming.Link!.TransactionId).IsEqualTo(outgoing.Id);
        await Assert.That(outgoing.Category).IsNull();
        await Assert.That(incoming.Category).IsNull();
        await Assert.That(await UncategorisedAsync(anna)).IsEmpty();
    }

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner,
        string currency = "CHF")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency, openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<ImportResult> UploadOkAsync(HttpClient client, Guid accountId, TestStatement statement)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", $"statement-{Guid.NewGuid():N}.txt");
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount, string date,
        string description, object? owner = null, Guid? categoryId = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description, owner, categoryId });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Transaction> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Transaction>($"/api/transactions/{id}"))!;

    private static async Task<List<Transaction>> UncategorisedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions/uncategorised"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record ImportResult(SourceFile SourceFile, int ImportedCount);

    private record SourceFile(Guid Id);

    private record CategoryRef(Guid Id, string Name, string Path);

    private record LinkDto(string Kind, Guid? TransactionId);

    private record Transaction(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string Currency,
        decimal? ChfAmount, string Type, string Description, CategoryRef? Category, LinkDto? Link);
}
