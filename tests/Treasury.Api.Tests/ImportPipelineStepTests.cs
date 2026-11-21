using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Api.Imports;

namespace Treasury.Api.Tests;

/// An import that fails AFTER its Transactions were stored is rolled back completely, observed through the upload
/// endpoint. No real step fails at that point today (the identifier check rejects before storing; the closing
/// balance check only warns), so a step that always fails stands in for one. Skipping rows before storing and
/// classifying stored rows are covered by the real steps' tests (DuplicateDetectionTests, TaggingTests, RuleTests).
public class ImportPipelineStepTests : ApiTestBase
{
    private sealed class FailingStep(Exception failure) : IImportStep
    {
        public ImportStage Stage => ImportStage.Verify;

        public Task RunAsync(ImportContext context, CancellationToken ct) => throw failure;
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task A_step_failing_after_the_transactions_were_stored_rolls_the_whole_import_back(bool expected)
    {
        Exception failure = expected
            ? new ImportFailedException("The closing balance doesn't add up.")
            : new InvalidOperationException("Something broke.");
        await using var app = new TreasuryApiFactory(ConnectionString)
        {
            ExtraServices = services => services.AddScoped<IImportStep>(_ => new FailingStep(failure)),
        };
        using var admin = app.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        await LoginAsync(admin, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        var account = await CreateAccountAsync(admin);

        var res = await UploadAsync(admin, account,
            new TestStatement().Row("2026-11-18", -1m, "Fine").Row("2026-11-18", -2m, "Also fine"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await admin.GetFromJsonAsync<List<Transaction>>("/api/transactions")).IsEmpty();
        await Assert.That(await admin.GetFromJsonAsync<List<object>>($"/api/accounts/{account}/source-files")).IsEmpty();
        var failed = Directory.GetFiles(Path.Combine(app.IngestRoot, "pipeline", "failed")).Select(f => Path.GetFileName(f)!);
        await Assert.That(failed).IsEquivalentTo(["statement.txt", "statement.txt.error.txt"]);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(app.IngestRoot, "pipeline", "failed", "statement.txt.error.txt")))
            .Contains(expected ? "closing balance" : "Unexpected error while importing");
        await Assert.That(Directory.EnumerateFiles(Path.Combine(app.DataRoot, "source-files"), "*", SearchOption.AllDirectories))
            .IsEmpty();
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name = "Pipeline", owner = new { kind = "family" }, integrationKey = "test", currency = "CHF",
            openingBalance = 0m, openingDate = "2026-01-01",
        });
        return (await res.Content.ReadFromJsonAsync<IdOnly>())!.Id;
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid accountId, TestStatement statement)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "statement.txt");
        return client.PostAsync($"/api/accounts/{accountId}/imports", form);
    }

    private record IdOnly(Guid Id);

    private record Transaction(Guid Id, string Description);
}
