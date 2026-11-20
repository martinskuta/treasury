using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Api.Imports;

namespace Treasury.Api.Tests;

/// The contract later features (#9 duplicates, #11 identifier/closing balance, #14 Tags, #15 Rules, #16 matching)
/// rely on when they plug a step into the import pipeline, observed through the upload endpoint.
public class ImportPipelineStepTests : ApiTestBase
{
    private sealed class SkipRowsStep : IImportStep
    {
        public ImportStage Stage => ImportStage.Deduplicate;

        public Task RunAsync(ImportContext context, CancellationToken ct)
        {
            context.SkippedCount += context.Rows.RemoveAll(r => r.Description.StartsWith("SKIP", StringComparison.Ordinal));
            return Task.CompletedTask;
        }
    }

    private sealed class NameStep : IImportStep
    {
        public ImportStage Stage => ImportStage.Classify;

        public Task RunAsync(ImportContext context, CancellationToken ct)
        {
            foreach (var t in context.Transactions) t.DisplayName = $"Named {t.Description}";
            return Task.CompletedTask;
        }
    }

    private sealed class RejectAfterStoreStep : IImportStep
    {
        public ImportStage Stage => ImportStage.Verify;

        public Task RunAsync(ImportContext context, CancellationToken ct) =>
            context.Transactions.Any(t => t.Description == "REJECT")
                ? throw new ImportFailedException("The closing balance doesn't add up.")
                : Task.CompletedTask;
    }

    private async Task<HttpClient> StartWithStepsAsync(TreasuryApiFactory app)
    {
        var client = app.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        await LoginAsync(client, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        return client;
    }

    private TreasuryApiFactory AppWith(params IImportStep[] steps) => new(ConnectionString)
    {
        ExtraServices = services =>
        {
            foreach (var step in steps) services.AddScoped<IImportStep>(_ => step);
        },
    };

    [Test]
    public async Task Rows_removed_before_storing_are_not_imported_and_are_counted_as_skipped()
    {
        await using var app = AppWith(new SkipRowsStep(), new NameStep());
        using var admin = await StartWithStepsAsync(app);
        var account = await CreateAccountAsync(admin);

        var res = await UploadAsync(admin, account,
            new TestStatement().Row("2026-11-18", -1m, "SKIP me").Row("2026-11-18", -2m, "Keep me"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var result = (await res.Content.ReadFromJsonAsync<Result>())!;
        await Assert.That(result.ImportedCount).IsEqualTo(1);
        await Assert.That(result.SkippedCount).IsEqualTo(1);
        var list = (await admin.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!;
        await Assert.That(list.Single().Description).IsEqualTo("Keep me");
        await Assert.That(list.Single().DisplayName).IsEqualTo("Named Keep me");
    }

    [Test]
    public async Task A_step_failing_after_the_transactions_were_stored_rolls_the_whole_import_back()
    {
        await using var app = AppWith(new RejectAfterStoreStep());
        using var admin = await StartWithStepsAsync(app);
        var account = await CreateAccountAsync(admin);

        var res = await UploadAsync(admin, account,
            new TestStatement().Row("2026-11-18", -1m, "Fine").Row("2026-11-18", -2m, "REJECT"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(await admin.GetFromJsonAsync<List<Transaction>>("/api/transactions")).IsEmpty();
        await Assert.That(await admin.GetFromJsonAsync<List<object>>($"/api/accounts/{account}/source-files")).IsEmpty();
        var failed = Directory.GetFiles(Path.Combine(app.IngestRoot, "pipeline", "failed")).Select(Path.GetFileName);
        await Assert.That(failed).IsEquivalentTo(["statement.txt", "statement.txt.error.txt"]);
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(app.IngestRoot, "pipeline", "failed", "statement.txt.error.txt")))
            .Contains("closing balance");
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

    private record Result(int ImportedCount, int SkippedCount);

    private record Transaction(Guid Id, string Description, string? DisplayName);
}
