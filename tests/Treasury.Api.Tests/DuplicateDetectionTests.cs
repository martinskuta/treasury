using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// Duplicate detection (#9): importing overlapping statements, or the same file twice, never creates duplicates.
public class DuplicateDetectionTests : ApiTestBase
{
    [Test]
    public async Task Re_importing_the_same_file_adds_no_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);
        var statement = new TestStatement()
            .Row("2026-11-18", -12.50m, "COOP-1234 ZURICH")
            .Row("2026-11-19", 5000m, "SALARY NOVEMBER", reference: "R-2");
        await UploadOkAsync(anna, account.Id, "november.txt", statement);

        var again = await UploadOkAsync(anna, account.Id, "november.txt", statement);

        await Assert.That(again.ImportedCount).IsEqualTo(0);
        await Assert.That(again.SkippedCount).IsEqualTo(2);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(2);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(4987.50m);
    }

    [Test]
    public async Task Importing_an_overlapping_statement_adds_only_the_new_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);
        await UploadOkAsync(anna, account.Id, "1-15.txt", new TestStatement()
            .Row("2026-11-01", -20m, "Migros")
            .Row("2026-11-10", -4.50m, "Coffee"));

        var result = await UploadOkAsync(anna, account.Id, "10-30.txt", new TestStatement()
            .Row("2026-11-10", -4.5m, "Coffee")
            .Row("2026-11-20", -60m, "Books")
            .Row("2026-11-25", 100m, "Refund"));

        await Assert.That(result.ImportedCount).IsEqualTo(2);
        await Assert.That(result.SkippedCount).IsEqualTo(1);
        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => t.Description)).IsEquivalentTo(["Migros", "Coffee", "Books", "Refund"]);
    }

    [Test]
    public async Task Two_identical_rows_in_one_file_are_both_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);

        var result = await UploadOkAsync(anna, account.Id, "coffees.txt", new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee")
            .Row("2026-11-10", -4.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(2);
        await Assert.That(result.SkippedCount).IsEqualTo(0);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(-9m);
    }

    [Test]
    public async Task Identical_rows_are_counted_across_files()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);
        await UploadOkAsync(anna, account.Id, "first.txt", new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee"));

        var result = await UploadOkAsync(anna, account.Id, "second.txt", new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee")
            .Row("2026-11-10", -4.50m, "Coffee"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That(result.SkippedCount).IsEqualTo(1);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(2);
    }

    [Test]
    public async Task Rows_that_differ_in_date_amount_or_description_are_not_duplicates()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);
        await UploadOkAsync(anna, account.Id, "first.txt", new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee"));

        var result = await UploadOkAsync(anna, account.Id, "second.txt", new TestStatement()
            .Row("2026-11-11", -4.50m, "Coffee")
            .Row("2026-11-10", -5.50m, "Coffee")
            .Row("2026-11-10", -4.50m, "Tea"));

        await Assert.That(result.ImportedCount).IsEqualTo(3);
        await Assert.That(result.SkippedCount).IsEqualTo(0);
    }

    [Test]
    public async Task The_same_row_in_another_account_is_not_a_duplicate()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var tkb = await CreateAccountAsync(anna, "TKB");
        var ubs = await CreateAccountAsync(anna, "UBS");
        var statement = new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee")
            .Row("2026-11-11", -10m, "Lunch", reference: "R-1");
        await UploadOkAsync(anna, tkb.Id, "tkb.txt", statement);

        var result = await UploadOkAsync(anna, ubs.Id, "ubs.txt", statement);

        await Assert.That(result.ImportedCount).IsEqualTo(2);
    }

    [Test]
    public async Task Rows_with_a_bank_reference_are_matched_by_account_and_reference()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna);
        await UploadOkAsync(anna, account.Id, "first.txt", new TestStatement()
            .Row("2026-11-10", -4.50m, "Coffee", reference: "R-1")
            .Row("2026-11-12", -30m, "Books", reference: "R-2"));

        // The bank later books R-1 with a different date and description: still the same Transaction.
        // R-3 has the same facts as R-2 but its own reference: a new Transaction.
        var result = await UploadOkAsync(anna, account.Id, "second.txt", new TestStatement()
            .Row("2026-11-11", -4.50m, "COFFEE BAR ZURICH", reference: "R-1")
            .Row("2026-11-12", -30m, "Books", reference: "R-3"));

        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That(result.SkippedCount).IsEqualTo(1);
        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => t.Reference ?? "")).IsEquivalentTo(["R-1", "R-2", "R-3"]);
        await Assert.That(list.Single(t => t.Reference == "R-1").Description).IsEqualTo("Coffee");
    }

    private static async Task<ImportResult> UploadOkAsync(HttpClient client, Guid accountId, string fileName, object content)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content.ToString()!));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        var res = await client.PostAsync($"/api/accounts/{accountId}/imports", form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name = "TKB")
    {
        var me = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!;
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner = new { kind = "member", memberId = me.Id }, integrationKey = "test", currency = "CHF",
            openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private record Me(Guid Id);

    private record Account(Guid Id, decimal Balance);

    private record ImportResult(int ImportedCount, int SkippedCount);

    private record Transaction(Guid Id, Guid? AccountId, DateOnly Date, decimal Amount, string Description, string? Reference);
}
