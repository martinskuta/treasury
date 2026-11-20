using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Files dropped into an Account's Ingest folder (ingest/&lt;slug&gt;/) are imported by the folder scan. Tests drop
/// files and trigger the scan directly through POST /api/ingest/scan.
public class IngestScanTests : ApiTestBase
{
    [Test]
    public async Task A_file_dropped_into_an_accounts_ingest_folder_is_imported_into_that_account_on_the_next_scan()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var other = await CreateAccountAsync(anna, "Revolut", await MeAsync(anna));
        var statement = new TestStatement()
            .Row("2026-11-18", -12.50m, "COOP-1234 ZURICH")
            .Row("2026-11-19", 5000m, "SALARY NOVEMBER");

        Drop(account, "november.txt", statement);
        await ScanAsync(anna);

        var list = await ListAsync(anna);
        await Assert.That(list.Select(t => t.Description)).IsEquivalentTo(["COOP-1234 ZURICH", "SALARY NOVEMBER"]);
        await Assert.That(list.All(t => t.AccountId == account.Id)).IsTrue();
        await Assert.That(list.All(t => t.Origin == "imported" && t.SourceFile?.FileName == "november.txt")).IsTrue();
        await Assert.That((await GetAccountAsync(anna, account.Id)).Balance).IsEqualTo(4987.50m);
        await Assert.That((await GetAccountAsync(anna, other.Id)).Balance).IsEqualTo(0m);
    }

    [Test]
    public async Task Imported_files_move_to_processed_and_failed_files_to_failed_with_an_error_note()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var good = new TestStatement().Row("2026-11-18", -12.50m, "Coffee");
        var bad = new TestStatement().Line("this is not a statement line");

        Drop(account, "good.txt", good);
        Drop(account, "bad.txt", bad);
        var scanned = await ScanAsync(anna);

        await Assert.That(scanned.Single(f => f.FileName == "good.txt").Succeeded).IsTrue();
        await Assert.That(scanned.Single(f => f.FileName == "bad.txt").Error).Contains("line 1");
        var folder = Path.Combine(App.IngestRoot, account.Slug);
        await Assert.That(Directory.GetFiles(folder)).IsEmpty();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(folder, "processed", "good.txt"))).IsEqualTo(good.ToString());
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(folder, "failed", "bad.txt"))).IsEqualTo(bad.ToString());
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(folder, "failed", "bad.txt.error.txt"))).Contains("line 1");
        await Assert.That((await ListAsync(anna)).Select(t => t.Description)).IsEquivalentTo(["Coffee"]);
    }

    [Test]
    public async Task Files_in_processed_and_failed_are_left_alone_by_later_scans()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        Drop(account, "good.txt", new TestStatement().Row("2026-11-18", -12.50m, "Coffee"));
        Drop(account, "bad.txt", new TestStatement().Line("this is not a statement line"));
        await ScanAsync(anna);

        var again = await ScanAsync(anna);

        await Assert.That(again).IsEmpty();
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(1);
        var folder = Path.Combine(App.IngestRoot, account.Slug);
        await Assert.That(Directory.GetFiles(Path.Combine(folder, "processed")).Select(Path.GetFileName)).IsEquivalentTo(["good.txt"]);
        await Assert.That(Directory.GetFiles(Path.Combine(folder, "failed")).Select(Path.GetFileName))
            .IsEquivalentTo(["bad.txt", "bad.txt.error.txt"]);
    }

    [Test]
    public async Task A_scan_reports_only_files_of_accounts_the_member_can_see()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var ben = await LoginAsNewMemberAsync("ben");
        var annas = await CreateAccountAsync(anna, "Anna TKB", await MeAsync(anna));
        var bens = await CreateAccountAsync(ben, "Ben TKB", await MeAsync(ben));
        Drop(annas, "anna.txt", new TestStatement().Row("2026-11-18", -1m, "Anna's coffee"));
        Drop(bens, "ben.txt", new TestStatement().Row("2026-11-18", -2m, "Ben's coffee"));

        var scanned = await ScanAsync(anna);

        await Assert.That(scanned.Select(f => f.FileName)).IsEquivalentTo(["anna.txt"]);
        await Assert.That((await ListAsync(ben)).Select(t => t.Description)).IsEquivalentTo(["Ben's coffee"]);
    }

    [Test]
    public async Task A_file_that_is_still_being_written_is_not_picked_up_half_finished()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var path = Path.Combine(App.IngestRoot, account.Slug, "november.txt");
        var written = 0;
        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        await using (var writer = new StreamWriter(file))
        {
            async Task WriteRowAsync()
            {
                await writer.WriteAsync(new TestStatement().Row("2026-11-18", -1m, $"Row {++written}").ToString());
                await writer.FlushAsync();
            }

            await WriteRowAsync();
            using var stop = new CancellationTokenSource();
            var writing = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    await WriteRowAsync();
                    await Task.Delay(20);
                }
            });

            var scanned = await ScanAsync(anna);
            await stop.CancelAsync();
            await writing;

            await Assert.That(scanned).IsEmpty();
            await Assert.That(File.Exists(path)).IsTrue();
            await Assert.That(await ListAsync(anna)).IsEmpty();
            await WriteRowAsync();
        }

        var finished = await ScanAsync(anna);

        await Assert.That(finished.Single().ImportedCount).IsEqualTo(written);
        await Assert.That(await ListAsync(anna)).Count().IsEqualTo(written);
    }

    [Test]
    public async Task A_partial_download_is_left_alone_until_it_is_renamed_to_its_final_name()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var partial = Drop(account, "november.txt.crdownload", new TestStatement().Row("2026-11-18", -1m, "Coffee"));

        await Assert.That(await ScanAsync(anna)).IsEmpty();
        await Assert.That(File.Exists(partial)).IsTrue();

        File.Move(partial, Path.Combine(App.IngestRoot, account.Slug, "november.txt"));
        await Assert.That((await ScanAsync(anna)).Single().ImportedCount).IsEqualTo(1);
    }

    [Test]
    public async Task Folders_are_scanned_in_the_background_at_the_configured_interval()
    {
        await using var app = new TreasuryApiFactory(ConnectionString) { Settings = { ["Ingest:ScanInterval"] = "02:00:00" } };
        using var admin = app.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        await LoginAsync(admin, TreasuryApiFactory.AdminUserName, TreasuryApiFactory.AdminPassword);
        var account = await CreateAccountAsync(admin, "TKB", new { kind = "family" });
        File.WriteAllText(Path.Combine(app.IngestRoot, account.Slug, "november.txt"),
            new TestStatement().Row("2026-11-18", -1m, "Coffee").ToString());

        app.Clock.Advance(TimeSpan.FromHours(1)); // more than the default interval, less than the configured one
        await Task.Delay(1000);
        await Assert.That(await ListAsync(admin)).IsEmpty();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await ListAsync(admin)).Count == 0)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The background scan didn't import the file.");
            app.Clock.Advance(TimeSpan.FromHours(1)); // inside the loop: an Advance can race the job's next timer
            await Task.Delay(100);
        }
        await Assert.That((await ListAsync(admin)).Single().Description).IsEqualTo("Coffee");
    }

    // ---- helpers ----

    private string Drop(Account account, string fileName, object content)
    {
        var path = Path.Combine(App.IngestRoot, account.Slug, fileName);
        File.WriteAllText(path, content.ToString());
        return path;
    }

    private static async Task<List<ScannedFile>> ScanAsync(HttpClient client)
    {
        var res = await client.PostAsync("/api/ingest/scan", null);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Scan failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<ScannedFile>>())!;
    }

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id };

    private static async Task<Account> CreateAccountAsync(HttpClient client, string name, object owner,
        string integrationKey = "test", string currency = "CHF")
    {
        var res = await client.PostAsJsonAsync("/api/accounts", new
        {
            name, owner, integrationKey, currency, openingBalance = 0m, openingDate = "2026-01-01",
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

    private record Account(Guid Id, string Name, string Slug, decimal Balance);

    private record SourceFileRef(Guid Id, string FileName);

    private record Transaction(Guid Id, Guid AccountId, DateOnly Date, decimal Amount, string Description, string Origin,
        SourceFileRef? SourceFile);

    private record ScannedFile(Guid AccountId, string FileName, bool Succeeded, int ImportedCount, string? Error);
}
