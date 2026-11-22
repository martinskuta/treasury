using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Treasury.Api.Tests;

/// #37: how up to date an Account or Multi-currency account is. Its reach is the later of its latest Transaction
/// date and its statements' latest closing balance date; its last import date is when its most recent Source file
/// was imported; it lags when the reach is before the end of the last complete calendar month.
/// The test clock starts on 2026-11-20, so the last complete month is October 2026.
public class AccountReachTests : ApiTestBase
{
    private static readonly DateOnly EndOfOctober = new(2026, 10, 31);

    [Test]
    public async Task An_accounts_reach_is_the_date_of_its_latest_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddTransactionAsync(anna, account.Id, "2026-10-12", -5m);
        await AddTransactionAsync(anna, account.Id, "2026-10-05", -7m);

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.Reach).IsEqualTo(new DateOnly(2026, 10, 12));
        await Assert.That((await ListAccountsAsync(anna)).Single().Reach).IsEqualTo(new DateOnly(2026, 10, 12));
    }

    [Test]
    public async Task An_account_kept_only_with_manual_transactions_has_a_reach_but_no_last_import_date()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddTransactionAsync(anna, account.Id, "2026-10-31", -5m);

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(got.LastImportedAt).IsNull();
        await Assert.That(got.Lagging).IsFalse();
    }

    [Test]
    public async Task An_account_with_no_transactions_and_no_source_file_has_no_reach_and_lags()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.Reach).IsNull();
        await Assert.That(got.LastImportedAt).IsNull();
        await Assert.That(got.Lagging).IsTrue();
    }

    [Test]
    public async Task A_statements_later_closing_balance_date_extends_the_reach()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "october.txt", new TestStatement()
            .Closing("2026-10-31", -12.50m)
            .Row("2026-10-20", -12.50m, "Coffee"));

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(got.Lagging).IsFalse();
    }

    [Test]
    public async Task A_transaction_after_the_latest_closing_balance_date_extends_the_reach()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "september.txt", new TestStatement()
            .Closing("2026-09-30", -12.50m)
            .Row("2026-09-20", -12.50m, "Coffee"));
        await AddTransactionAsync(anna, account.Id, "2026-10-02", -5m);

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.Reach).IsEqualTo(new DateOnly(2026, 10, 2));
        await Assert.That(got.Lagging).IsTrue();
    }

    [Test]
    public async Task The_last_import_date_is_when_the_accounts_most_recent_source_file_was_imported()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        // The older statement is imported last: what counts is the import time, not the statement's period.
        await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "october.txt",
            new TestStatement().Row("2026-10-20", -12.50m, "Coffee"));
        App.Clock.Advance(TimeSpan.FromHours(3));
        var imported = App.Clock.GetUtcNow();
        await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "september.txt",
            new TestStatement().Row("2026-09-20", -1m, "Kiosk"));
        App.Clock.Advance(TimeSpan.FromHours(1));

        var got = await GetAccountAsync(anna, account.Id);

        await Assert.That(got.LastImportedAt).IsEqualTo(imported);
        await Assert.That(got.Reach).IsEqualTo(new DateOnly(2026, 10, 20));
        await Assert.That((await ListAccountsAsync(anna)).Single().LastImportedAt).IsEqualTo(imported);
    }

    [Test]
    public async Task A_multi_currency_accounts_reach_is_the_latest_reach_among_its_accounts()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut");
        var imported = App.Clock.GetUtcNow();
        // CHF is quiet after 3.10. but its closing balance is dated 31.10.; EUR's statement period ends on 30.9.
        await UploadOkAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "revolut.txt", new TestStatement()
            .Balances("CHF", "2026-09-01", 1000m, "2026-10-31", 980m)
            .Balances("EUR", "2026-09-01", 50m, "2026-09-30", 40m)
            .Row("2026-10-03", -20m, "Migros")
            .Row("2026-09-10", -10m, "Trattoria", currency: "EUR"));
        App.Clock.Advance(TimeSpan.FromHours(1));

        var got = await GetGroupAsync(anna, group.Id);

        await Assert.That(got.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(got.Lagging).IsFalse();
        await Assert.That(got.LastImportedAt).IsEqualTo(imported);
        var chf = got.Accounts.Single(a => a.Currency == "CHF");
        var eur = got.Accounts.Single(a => a.Currency == "EUR");
        await Assert.That(chf.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(chf.Lagging).IsFalse();
        await Assert.That(eur.Reach).IsEqualTo(new DateOnly(2026, 9, 30));
        await Assert.That(eur.Lagging).IsTrue();
        var listed = (await ListGroupsAsync(anna)).Single();
        await Assert.That(listed.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(listed.LastImportedAt).IsEqualTo(imported);
    }

    [Test]
    public async Task An_account_in_a_multi_currency_account_reports_the_multi_currency_accounts_last_import_date()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut");
        await UploadOkAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "september.txt", new TestStatement()
            .Balances("CHF", "2026-09-01", 1000m, "2026-09-30", 980m)
            .Balances("EUR", "2026-09-01", 50m, "2026-09-30", 40m)
            .Row("2026-09-03", -20m, "Migros")
            .Row("2026-09-10", -10m, "Trattoria", currency: "EUR"));
        App.Clock.Advance(TimeSpan.FromHours(2));
        var imported = App.Clock.GetUtcNow();
        // October's file has nothing in EUR, but it is still the last Source file of the Multi-currency account.
        await UploadOkAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "october.txt", new TestStatement()
            .Balances("CHF", "2026-10-01", 980m, "2026-10-31", 950m)
            .Row("2026-10-05", -30m, "Coop"));

        var got = await GetGroupAsync(anna, group.Id);

        var eur = got.Accounts.Single(a => a.Currency == "EUR");
        await Assert.That(got.LastImportedAt).IsEqualTo(imported);
        await Assert.That(eur.LastImportedAt).IsEqualTo(imported);
        await Assert.That(eur.Reach).IsEqualTo(new DateOnly(2026, 9, 30));
        // The same Account read through /api/accounts.
        var plain = await GetAccountAsync(anna, eur.Id);
        await Assert.That(plain.LastImportedAt).IsEqualTo(imported);
        await Assert.That(plain.Reach).IsEqualTo(new DateOnly(2026, 9, 30));
    }

    [Test]
    public async Task A_multi_currency_account_without_transactions_or_source_files_has_no_reach_and_lags()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut");

        var got = await GetGroupAsync(anna, group.Id);

        await Assert.That(got.Reach).IsNull();
        await Assert.That(got.LastImportedAt).IsNull();
        await Assert.That(got.Lagging).IsTrue();
    }

    [Test]
    public async Task Undoing_an_import_moves_the_reach_and_the_last_import_date_back()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "TKB", await MeAsync(anna));
        var importedOctober = App.Clock.GetUtcNow();
        var october = await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "october.txt", new TestStatement()
            .Closing("2026-10-31", -10m)
            .Row("2026-10-18", -10m, "October"));
        App.Clock.Advance(TimeSpan.FromHours(1));
        var importedNovember = App.Clock.GetUtcNow();
        var november = await UploadOkAsync(anna, $"/api/accounts/{account.Id}/imports", "november.txt", new TestStatement()
            .Closing("2026-11-19", -30m)
            .Row("2026-11-18", -20m, "November"));
        var before = await GetAccountAsync(anna, account.Id);
        await Assert.That(before.Reach).IsEqualTo(new DateOnly(2026, 11, 19));
        await Assert.That(before.LastImportedAt).IsEqualTo(importedNovember);

        await anna.DeleteAsync($"/api/source-files/{november.SourceFile.Id}");

        var afterNovember = await GetAccountAsync(anna, account.Id);
        await Assert.That(afterNovember.Reach).IsEqualTo(EndOfOctober);
        await Assert.That(afterNovember.LastImportedAt).IsEqualTo(importedOctober);
        await Assert.That(afterNovember.Lagging).IsFalse();

        await anna.DeleteAsync($"/api/source-files/{october.SourceFile.Id}");

        var afterOctober = await GetAccountAsync(anna, account.Id);
        await Assert.That(afterOctober.Reach).IsNull();
        await Assert.That(afterOctober.LastImportedAt).IsNull();
        await Assert.That(afterOctober.Lagging).IsTrue();
    }

    [Test]
    public async Task Undoing_an_import_into_a_multi_currency_account_moves_its_reach_and_last_import_date_back()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut");
        var importedSeptember = App.Clock.GetUtcNow();
        await UploadOkAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "september.txt", new TestStatement()
            .Balances("CHF", "2026-09-01", 1000m, "2026-09-30", 980m)
            .Row("2026-09-03", -20m, "Migros"));
        App.Clock.Advance(TimeSpan.FromHours(1));
        var october = await UploadOkAsync(anna, $"/api/multi-currency-accounts/{group.Id}/imports", "october.txt", new TestStatement()
            .Balances("CHF", "2026-10-01", 980m, "2026-10-31", 950m)
            .Row("2026-10-05", -30m, "Coop"));
        await Assert.That((await GetGroupAsync(anna, group.Id)).Reach).IsEqualTo(EndOfOctober);

        await anna.DeleteAsync($"/api/source-files/{october.SourceFile.Id}");

        var got = await GetGroupAsync(anna, group.Id);
        await Assert.That(got.Reach).IsEqualTo(new DateOnly(2026, 9, 30));
        await Assert.That(got.LastImportedAt).IsEqualTo(importedSeptember);
        await Assert.That(got.Lagging).IsTrue();
        await Assert.That(got.Accounts.Single().Reach).IsEqualTo(new DateOnly(2026, 9, 30));
        await Assert.That(got.Accounts.Single().LastImportedAt).IsEqualTo(importedSeptember);
    }

    [Test]
    public async Task An_account_lags_when_its_reach_is_before_the_last_day_of_the_last_complete_month()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var me = await MeAsync(anna);
        var upToDate = await CreateAccountAsync(anna, "Up to date", me);
        var behind = await CreateAccountAsync(anna, "Behind", me);
        await AddTransactionAsync(anna, upToDate.Id, "2026-10-31", -5m);
        await AddTransactionAsync(anna, behind.Id, "2026-10-30", -5m);

        await Assert.That((await GetAccountAsync(anna, upToDate.Id)).Lagging).IsFalse();
        await Assert.That((await GetAccountAsync(anna, behind.Id)).Lagging).IsTrue();
    }

    [Test]
    public async Task An_up_to_date_account_starts_to_lag_when_the_next_month_begins()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddTransactionAsync(anna, account.Id, "2026-10-31", -5m);

        // The last moment of November: October is still the last complete month.
        App.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero));
        await Assert.That((await GetAccountAsync(anna, account.Id)).Lagging).IsFalse();

        // December has begun: November is complete, and the Account has nothing of it.
        App.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        var got = await GetAccountAsync(anna, account.Id);
        await Assert.That(got.Lagging).IsTrue();
        await Assert.That(got.Reach).IsEqualTo(EndOfOctober);
    }

    [Test]
    public async Task A_multi_currency_account_starts_to_lag_when_the_next_month_begins()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, "Revolut");
        await AddTransactionAsync(anna, group.Accounts.Single().Id, "2026-10-31", -5m);

        await Assert.That((await GetGroupAsync(anna, group.Id)).Lagging).IsFalse();

        App.Clock.SetUtcNow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));
        await Assert.That((await GetGroupAsync(anna, group.Id)).Lagging).IsTrue();
    }

    [Test]
    public async Task Another_members_personal_account_stays_invisible_while_a_family_accounts_reach_is_shared()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobs = await CreateAccountAsync(bob, "Bob's TKB", await MeAsync(bob));
        var bobsGroup = await CreateGroupAsync(bob, "Bob's Revolut");
        var joint = await CreateAccountAsync(bob, "Joint", Family);
        await UploadOkAsync(bob, $"/api/accounts/{bobs.Id}/imports", "bob.txt",
            new TestStatement().Closing("2026-10-31", -1m).Row("2026-10-18", -1m, "X"));
        var imported = App.Clock.GetUtcNow();
        await UploadOkAsync(bob, $"/api/accounts/{joint.Id}/imports", "joint.txt",
            new TestStatement().Closing("2026-10-31", -80m).Row("2026-10-18", -80m, "MIGROS"));

        var annas = await ListAccountsAsync(anna);

        await Assert.That(annas.Select(a => a.Id)).IsEquivalentTo([joint.Id]);
        await Assert.That(annas.Single().Reach).IsEqualTo(EndOfOctober);
        await Assert.That(annas.Single().LastImportedAt).IsEqualTo(imported);
        await Assert.That(annas.Single().Lagging).IsFalse();
        await Assert.That((await anna.GetAsync($"/api/accounts/{bobs.Id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
        await Assert.That((await anna.GetAsync($"/api/multi-currency-accounts/{bobsGroup.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

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

    /// A Multi-currency account of the client's Member with main currency CHF, opened with 1'000 CHF on 1.9.2026.
    private static async Task<Group> CreateGroupAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/multi-currency-accounts", new
        {
            name, owner = await MeAsync(client), integrationKey = "test-multi", mainCurrency = "CHF",
            openingBalance = 1000m, openingDate = "2026-09-01",
        });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create Multi-currency account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Group>())!;
    }

    private static async Task AddTransactionAsync(HttpClient client, Guid accountId, string date, decimal amount)
    {
        var res = await client.PostAsJsonAsync("/api/transactions", new { accountId, date, amount, description = "By hand" });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add Transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
    }

    private static async Task<ImportResult> UploadOkAsync(HttpClient client, string url, string fileName, TestStatement statement)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(statement.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", fileName);
        var res = await client.PostAsync(url, form);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Upload failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private static async Task<Account> GetAccountAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Account>($"/api/accounts/{id}"))!;

    private static async Task<List<Account>> ListAccountsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Account>>("/api/accounts"))!;

    private static async Task<Group> GetGroupAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{id}"))!;

    private static async Task<List<Group>> ListGroupsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Group>>("/api/multi-currency-accounts"))!;

    private record Me(Guid Id, string UserName);

    private record Account(Guid Id, string Name, string Currency, DateOnly? Reach, DateTimeOffset? LastImportedAt,
        bool Lagging);

    private record Group(Guid Id, string Name, List<Account> Accounts, DateOnly? Reach, DateTimeOffset? LastImportedAt,
        bool Lagging);

    private record SourceFileRef(Guid Id);

    private record ImportResult(SourceFileRef SourceFile, int ImportedCount);
}
