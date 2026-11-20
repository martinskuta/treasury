using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// #10: undoing an import = deleting its Source file.
public class UndoImportTests : ApiTestBase
{
    [Test]
    public async Task Deleting_a_source_file_removes_its_transactions_and_updates_the_account_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = account.Id, date = "2026-11-17", amount = -5m, description = "Ice cream" });
        var import = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement()
            .Row("2026-11-18", -12.50m, "Coffee")
            .Row("2026-11-19", 5000m, "Salary"));
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(4982.50m);

        var res = await anna.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var left = await ListAsync(anna);
        await Assert.That(left.Select(t => t.Description)).IsEquivalentTo(["Ice cream"]);
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(-5m);
        await Assert.That((await anna.GetAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await SourceFilesAsync(anna, account.Id)).IsEmpty();
    }

    [Test]
    public async Task After_undoing_an_import_the_same_file_can_be_imported_again()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var statement = new TestStatement()
            .Row("2026-11-18", -12.50m, "Coffee", reference: "R-1")
            .Row("2026-11-19", 5000m, "Salary", reference: "R-2");
        var first = await UploadOkAsync(anna, account.Id, "november.txt", statement);
        await anna.DeleteAsync($"/api/source-files/{first.SourceFile.Id}");

        var again = await UploadOkAsync(anna, account.Id, "november.txt", statement);

        await Assert.That(again.ImportedCount).IsEqualTo(2);
        await Assert.That(again.SkippedCount).IsEqualTo(0);
        var list = await ListAsync(anna);
        await Assert.That(list).Count().IsEqualTo(2);
        await Assert.That(list.All(t => t.SourceFile!.Id == again.SourceFile.Id)).IsTrue();
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(4987.50m);
        await Assert.That((await SourceFilesAsync(anna, account.Id)).Select(f => f.Id)).IsEquivalentTo([again.SourceFile.Id]);
        await Assert.That(await anna.GetStringAsync($"/api/source-files/{again.SourceFile.Id}/content"))
            .IsEqualTo(statement.ToString());
    }

    [Test]
    public async Task Undoing_an_import_deletes_the_kept_copy_but_leaves_the_members_copy_in_processed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var import = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement().Row("2026-11-18", -1m, "X"));
        var keptFolder = Path.Combine(App.DataRoot, "source-files", account.Slug);
        await Assert.That(Directory.GetFiles(keptFolder, "*", SearchOption.AllDirectories)).Count().IsEqualTo(1);

        await anna.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        await Assert.That(Directory.GetFileSystemEntries(keptFolder)).IsEmpty();
        await Assert.That((await anna.GetAsync($"/api/source-files/{import.SourceFile.Id}/content")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(File.Exists(Path.Combine(App.IngestRoot, account.Slug, "processed", "november.txt"))).IsTrue();
    }

    [Test]
    public async Task Undoing_one_import_leaves_the_other_imports_of_the_account_alone()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var october = await UploadOkAsync(anna, account.Id, "october.txt", new TestStatement().Row("2026-10-18", -10m, "October"));
        var november = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement().Row("2026-11-18", -20m, "November"));

        await anna.DeleteAsync($"/api/source-files/{november.SourceFile.Id}");

        await Assert.That((await ListAsync(anna)).Select(t => t.Description)).IsEquivalentTo(["October"]);
        await Assert.That((await SourceFilesAsync(anna, account.Id)).Select(f => f.Id)).IsEquivalentTo([october.SourceFile.Id]);
        await Assert.That(await anna.GetStringAsync($"/api/source-files/{october.SourceFile.Id}/content")).Contains("October");
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(-10m);
    }

    [Test]
    public async Task A_member_who_cannot_manage_the_account_cannot_delete_its_source_files()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var import = await UploadOkAsync(anna, account.Id, "november.txt", new TestStatement().Row("2026-11-18", -1m, "X"));

        var res = await bob.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(1);
        await Assert.That(await SourceFilesAsync(anna, account.Id)).Count().IsEqualTo(1);
        await Assert.That((await NewClient().DeleteAsync($"/api/source-files/{import.SourceFile.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Any_member_can_undo_an_import_into_a_family_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var import = await UploadOkAsync(anna, joint.Id, "joint.txt", new TestStatement().Row("2026-11-18", -80m, "MIGROS"));

        var res = await bob.DeleteAsync($"/api/source-files/{import.SourceFile.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task Deleting_an_unknown_source_file_is_not_found()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.DeleteAsync($"/api/source-files/{Guid.NewGuid()}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    private static async Task<List<SourceFile>> SourceFilesAsync(HttpClient client, Guid accountId) =>
        (await client.GetFromJsonAsync<List<SourceFile>>($"/api/accounts/{accountId}/source-files"))!;

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

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner)
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey = "test", currency = "CHF", openingBalance = 0m, openingDate = "2026-01-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private record Me(Guid Id, string UserName);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name, string Slug, OwnerDto Owner, decimal Balance);

    private record SourceFileRef(Guid Id, string FileName);

    private record SourceFile(Guid Id, Guid AccountId, string FileName, DateTimeOffset ImportedAt, int TransactionCount);

    private record ImportResult(SourceFile SourceFile, int ImportedCount, int SkippedCount);

    private record Transaction(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string Description,
        OwnerDto Owner, string Origin, SourceFileRef? SourceFile);
}
