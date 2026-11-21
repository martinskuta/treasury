using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class AccountTests : ApiTestBase
{
    [Test]
    public async Task A_member_can_create_a_personal_account_with_every_field()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);

        var res = await anna.PostAsJsonAsync("/api/accounts", new
        {
            name = "TKB Private",
            owner = new { kind = "member", memberId = annaId },
            integrationKey = "test",
            currency = "CHF",
            openingBalance = 1234.50m,
            openingDate = "2026-01-01",
            identifier = "CH93 0076 2011 6238 5295 7",
        });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = await res.Content.ReadFromJsonAsync<Account>();
        var account = await anna.GetFromJsonAsync<Account>($"/api/accounts/{created!.Id}");
        await Assert.That(account!.Name).IsEqualTo("TKB Private");
        await Assert.That(account.Owner.Kind).IsEqualTo("member");
        await Assert.That(account.Owner.MemberId).IsEqualTo(annaId);
        await Assert.That(account.Owner.Name).IsEqualTo("anna");
        await Assert.That(account.IntegrationKey).IsEqualTo("test");
        await Assert.That(account.Currency).IsEqualTo("CHF");
        await Assert.That(account.OpeningBalance).IsEqualTo(1234.50m);
        await Assert.That(account.OpeningDate).IsEqualTo(new DateOnly(2026, 1, 1));
        await Assert.That(account.Identifier).IsEqualTo("CH93 0076 2011 6238 5295 7");
    }

    [Test]
    public async Task A_member_can_create_a_family_account_that_every_member_sees()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var res = await anna.PostAsJsonAsync("/api/accounts", NewAccount("Joint card", Family, currency: "EUR"));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var seenByBob = await ListAsync(bob);
        await Assert.That(seenByBob.Select(a => a.Name)).IsEquivalentTo(["Joint card"]);
        await Assert.That(seenByBob[0].Owner.Kind).IsEqualTo("family");
        await Assert.That(seenByBob[0].Owner.Name).IsEqualTo("Family");
        await Assert.That(seenByBob[0].Currency).IsEqualTo("EUR");
    }

    [Test]
    [Arguments("CHF")]
    [Arguments("CZK")]
    [Arguments("EUR")]
    [Arguments("USD")]
    [Arguments("GBP")]
    [Arguments("PLN")]
    [Arguments("JPY")]
    [Arguments("AUD")]
    [Arguments("ZAR")]
    public async Task Each_currency_with_an_exchange_rate_is_accepted(string currency)
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/accounts", NewAccount("Wallet", Family, currency: currency));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
    }

    [Test]
    [Arguments("RUB")] // no ECB reference rate
    [Arguments("BGN")] // no longer published since Bulgaria adopted the euro
    [Arguments("gbp")]
    public async Task Currencies_without_an_exchange_rate_are_rejected(string currency)
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/accounts", NewAccount("Wallet", Family, currency: currency));

        await AssertValidationErrorAsync(res, "currency");
        await Assert.That(await ListAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task An_unknown_integration_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/accounts", NewAccount("Wallet", Family, integrationKey: "nope"));

        await AssertValidationErrorAsync(res, "integrationKey");
    }

    [Test]
    public async Task Creating_an_account_creates_its_ingest_folder_with_processed_and_failed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var account = await CreateAsync(anna, NewAccount("Revolut EUR (Anna)", await MeAsync(anna)));

        await Assert.That(account.Slug).IsEqualTo("revolut-eur-anna");
        var folder = Path.Combine(App.IngestRoot, "revolut-eur-anna");
        await Assert.That(Directory.Exists(Path.Combine(folder, "processed"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(folder, "failed"))).IsTrue();
    }

    [Test]
    public async Task Ingest_folders_are_flat_and_unique_even_for_accounts_with_the_same_name()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var annas = await CreateAsync(anna, NewAccount("Cash", await MeAsync(anna)));
        var bobs = await CreateAsync(bob, NewAccount("Cash", await MeAsync(bob)));

        await Assert.That(annas.Slug).IsEqualTo("cash");
        await Assert.That(bobs.Slug).IsEqualTo("cash-2");
        await Assert.That(Directory.GetDirectories(App.IngestRoot).Select(d => Path.GetFileName(d)!))
            .IsEquivalentTo(["cash", "cash-2"]);
    }

    [Test]
    public async Task An_account_can_be_renamed_and_keeps_its_ingest_folder()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAsync(anna, NewAccount("TKB", await MeAsync(anna)));

        var res = await anna.PatchAsJsonAsync($"/api/accounts/{account.Id}", new { name = "TKB Salary" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var renamed = await anna.GetFromJsonAsync<Account>($"/api/accounts/{account.Id}");
        await Assert.That(renamed!.Name).IsEqualTo("TKB Salary");
        await Assert.That(renamed.Slug).IsEqualTo("tkb");
        await Assert.That(Directory.Exists(Path.Combine(App.IngestRoot, "tkb"))).IsTrue();
    }

    [Test]
    public async Task Renaming_to_an_empty_name_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var account = await CreateAsync(anna, NewAccount("TKB", Family));

        var res = await anna.PatchAsJsonAsync($"/api/accounts/{account.Id}", new { name = "  " });

        await AssertValidationErrorAsync(res, "name");
    }

    [Test]
    public async Task A_member_sees_their_own_and_the_family_accounts_but_never_another_members()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        await CreateAsync(anna, NewAccount("Anna private", await MeAsync(anna)));
        var bobsPrivate = await CreateAsync(bob, NewAccount("Bob private", await MeAsync(bob)));
        await CreateAsync(bob, NewAccount("Family joint", Family));

        var annasView = await ListAsync(anna);

        await Assert.That(annasView.Select(a => a.Name)).IsEquivalentTo(["Anna private", "Family joint"]);
        var direct = await anna.GetAsync($"/api/accounts/{bobsPrivate.Id}");
        await Assert.That(direct.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task A_member_cannot_create_an_account_for_another_member()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var res = await anna.PostAsJsonAsync("/api/accounts", NewAccount("For Bob", await MeAsync(bob)));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await ListAsync(bob)).IsEmpty();
        await Assert.That(Directory.GetDirectories(App.IngestRoot)).IsEmpty();
    }

    [Test]
    public async Task A_member_cannot_rename_another_members_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobsPrivate = await CreateAsync(bob, NewAccount("Bob private", await MeAsync(bob)));

        var res = await anna.PatchAsJsonAsync($"/api/accounts/{bobsPrivate.Id}", new { name = "Hacked" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var unchanged = await bob.GetFromJsonAsync<Account>($"/api/accounts/{bobsPrivate.Id}");
        await Assert.That(unchanged!.Name).IsEqualTo("Bob private");
    }

    [Test]
    public async Task Any_member_can_manage_a_family_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAsync(bob, NewAccount("Joint", Family));

        var res = await anna.PatchAsJsonAsync($"/api/accounts/{joint.Id}", new { name = "Joint card" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ListAsync(bob)).Single().Name).IsEqualTo("Joint card");
    }

    [Test]
    public async Task The_balance_starts_at_the_opening_balance()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        await CreateAsync(anna, NewAccount("Savings", Family, openingBalance: -42.15m));

        await Assert.That((await ListAsync(anna)).Single().Balance).IsEqualTo(-42.15m);
    }

    [Test]
    public async Task The_identifier_is_optional()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var account = await CreateAsync(anna, NewAccount("TKB", Family, identifier: null));

        await Assert.That(account.Identifier).IsNull();
    }

    [Test]
    public async Task An_identifier_is_rejected_when_the_integration_cannot_check_one()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/accounts",
            NewAccount("Cash", Family, integrationKey: "test-noid", identifier: "1234"));

        await AssertValidationErrorAsync(res, "identifier");
    }

    [Test]
    public async Task Integrations_say_whether_they_can_check_an_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var integrations = await anna.GetFromJsonAsync<List<Integration>>("/api/integrations");

        await Assert.That(integrations!).Contains(new Integration("test", "Test bank", true));
        await Assert.That(integrations!).Contains(new Integration("test-noid", "Test bank without identifier", false));
    }

    [Test]
    public async Task Accounts_require_login()
    {
        var res = await Client.GetAsync("/api/accounts");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static readonly object Family = new { kind = "family" };

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

    private static object NewAccount(string name, object owner, string currency = "CHF", string integrationKey = "test",
        decimal openingBalance = 100m, string? identifier = null) => new
    {
        name,
        owner,
        integrationKey,
        currency,
        openingBalance,
        openingDate = "2026-01-01",
        identifier,
    };

    private static async Task<Account> CreateAsync(HttpClient client, object request)
    {
        var res = await client.PostAsJsonAsync("/api/accounts", request);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create account failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Account>())!;
    }

    private static async Task<List<Account>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Account>>("/api/accounts"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Integration(string Key, string Name, bool CanProvideIdentifier);

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private record Me(Guid Id, string UserName);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name, string Slug, OwnerDto Owner, string IntegrationKey, string Currency,
        decimal OpeningBalance, DateOnly OpeningDate, string? Identifier, decimal Balance);
}
