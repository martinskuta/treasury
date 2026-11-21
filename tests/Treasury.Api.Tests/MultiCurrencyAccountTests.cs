using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Api.Data;

namespace Treasury.Api.Tests;

/// Multi-currency accounts (#23, ADR-0009): a group of one Owner's single-currency Accounts with one Ingest folder.
public class MultiCurrencyAccountTests : ApiTestBase
{
    [Test]
    public async Task Integrations_say_whether_they_hold_several_currencies()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var integrations = (await anna.GetFromJsonAsync<List<Integration>>("/api/integrations"))!;

        await Assert.That(integrations.Single(i => i.Key == "test-multi").HoldsSeveralCurrencies).IsTrue();
        await Assert.That(integrations.Single(i => i.Key == "test").HoldsSeveralCurrencies).IsFalse();
    }

    [Test]
    public async Task A_member_can_create_a_multi_currency_account_with_its_main_currency_account_and_ingest_folder()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts", new
        {
            name = "Revolut",
            owner = new { kind = "member", memberId = annaId },
            integrationKey = "test-multi",
            mainCurrency = "CHF",
            openingBalance = 250.40m,
            openingDate = "2026-01-01",
        });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = (await res.Content.ReadFromJsonAsync<Group>())!;
        var group = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{created.Id}"))!;
        await Assert.That(group.Name).IsEqualTo("Revolut");
        await Assert.That(group.Owner.MemberId).IsEqualTo(annaId);
        await Assert.That(group.IntegrationKey).IsEqualTo("test-multi");
        await Assert.That(group.MainCurrency).IsEqualTo("CHF");
        await Assert.That(group.Slug).IsEqualTo("revolut");
        var main = group.Accounts.Single();
        await Assert.That(main.Name).IsEqualTo("Revolut CHF");
        await Assert.That(main.Currency).IsEqualTo("CHF");
        await Assert.That(main.OpeningBalance).IsEqualTo(250.40m);
        await Assert.That(main.OpeningDate).IsEqualTo(new DateOnly(2026, 1, 1));
        await Assert.That(main.Owner.MemberId).IsEqualTo(annaId);
        await Assert.That(main.IntegrationKey).IsEqualTo("test-multi");
        await Assert.That(main.MultiCurrencyAccountId).IsEqualTo(group.Id);
        await Assert.That(group.TotalBalance).IsEqualTo(250.40m);

        // The group has the Ingest folder; its Accounts have none of their own.
        var folder = Path.Combine(App.IngestRoot, "revolut");
        await Assert.That(Directory.Exists(Path.Combine(folder, "processed"))).IsTrue();
        await Assert.That(Directory.Exists(Path.Combine(folder, "failed"))).IsTrue();
        await Assert.That(Directory.GetDirectories(App.IngestRoot).Select(d => Path.GetFileName(d)!))
            .IsEquivalentTo(["revolut"]);
    }

    [Test]
    public async Task Creating_a_multi_currency_account_validates_every_field()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts", new
        {
            name = " ",
            owner = new { kind = "bank" },
            integrationKey = "nope",
            mainCurrency = "RUB",
        });

        await AssertValidationErrorAsync(res, "name", "owner", "integrationKey", "mainCurrency", "openingBalance", "openingDate");
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
        await Assert.That(Directory.Exists(App.IngestRoot) ? Directory.GetDirectories(App.IngestRoot) : []).IsEmpty();
    }

    [Test]
    public async Task Any_currency_with_an_exchange_rate_can_be_the_main_currency()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, mainCurrency: "GBP", openingBalance: 80m));

        await Assert.That(group.MainCurrency).IsEqualTo("GBP");
        await Assert.That(group.Accounts.Single().Name).IsEqualTo("Revolut GBP");
        await Assert.That(group.Accounts.Single().Currency).IsEqualTo("GBP");
    }

    [Test]
    public async Task A_member_cannot_create_a_multi_currency_account_for_another_member()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts",
            NewGroup("For Bob", new { kind = "member", memberId = await MemberIdAsync(bob) }));

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await ListGroupsAsync(bob)).IsEmpty();
    }

    [Test]
    public async Task Its_accounts_are_listed_with_it_and_its_total_is_shown_in_the_main_currency()
    {
        App.ExchangeRates.Publish("2026-11-20", chf: 0.93m, usd: 1.10m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, mainCurrency: "CHF", openingBalance: 100m));
        var dollars = await AddCurrencyAccountAsync(group.Id, "USD", openingBalance: 110m);

        // 110 USD = 110 / 1.10 * 0.93 = 93.00 CHF at today's rates.
        var listed = await EventuallyAsync(anna, groups => groups.Single().TotalBalance is not null);

        var revolut = listed.Single();
        await Assert.That(revolut.TotalBalance).IsEqualTo(193.00m);
        await Assert.That(revolut.Accounts.Select(a => (a.Currency, a.Balance)))
            .IsEquivalentTo([("CHF", 100m), ("USD", 110m)]);
        var accounts = await ListAccountsAsync(anna);
        await Assert.That(accounts.Select(a => (a.Id, a.MultiCurrencyAccountId)))
            .IsEquivalentTo([(group.Accounts.Single().Id, (Guid?)group.Id), (dollars, (Guid?)group.Id)]);
    }

    [Test]
    public async Task Its_total_is_also_shown_in_chf_at_todays_rates()
    {
        App.ExchangeRates.Publish("2026-11-20", chf: 0.93m, usd: 1.10m);
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, mainCurrency: "USD", openingBalance: 110m));
        await AddCurrencyAccountAsync(group.Id, "EUR", openingBalance: 50m);

        // 110 USD = 93.00 CHF and 50 EUR = 46.50 CHF at today's rates.
        var listed = await EventuallyAsync(anna, groups => groups.Single().TotalBalanceChf is not null);

        await Assert.That(listed.Single().TotalBalanceChf).IsEqualTo(139.50m);
    }

    [Test]
    public async Task Its_total_in_chf_is_empty_while_an_exchange_rate_is_missing()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, mainCurrency: "CHF", openingBalance: 10m));
        await AddCurrencyAccountAsync(group.Id, "EUR", openingBalance: 50m);

        await Assert.That((await ListGroupsAsync(anna)).Single().TotalBalanceChf).IsNull();
    }

    [Test]
    public async Task A_member_sees_their_own_and_the_family_multi_currency_accounts_but_never_another_members()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        await CreateGroupAsync(anna, NewGroup("Anna Revolut", new { kind = "member", memberId = await MemberIdAsync(anna) }));
        var bobs = await CreateGroupAsync(bob, NewGroup("Bob Revolut", new { kind = "member", memberId = await MemberIdAsync(bob) }));
        await CreateGroupAsync(bob, NewGroup("Family Revolut", Family));

        await Assert.That((await ListGroupsAsync(anna)).Select(g => g.Name)).IsEquivalentTo(["Anna Revolut", "Family Revolut"]);
        await Assert.That((await anna.GetAsync($"/api/multi-currency-accounts/{bobs.Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await anna.GetAsync($"/api/accounts/{bobs.Accounts.Single().Id}")).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Its_accounts_can_be_renamed_but_stay_in_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family));
        var main = group.Accounts.Single();

        var res = await anna.PatchAsJsonAsync($"/api/accounts/{main.Id}",
            new { name = "Revolut Francs", multiCurrencyAccountId = (Guid?)null });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var renamed = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{group.Id}"))!.Accounts.Single();
        await Assert.That(renamed.Name).IsEqualTo("Revolut Francs");
        await Assert.That(renamed.MultiCurrencyAccountId).IsEqualTo(group.Id);
    }

    [Test]
    public async Task A_multi_currency_account_can_be_renamed_and_keeps_its_ingest_folder()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family));

        var res = await anna.PatchAsJsonAsync($"/api/multi-currency-accounts/{group.Id}", new { name = "Revolut Anna" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var renamed = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{group.Id}"))!;
        await Assert.That(renamed.Name).IsEqualTo("Revolut Anna");
        await Assert.That(renamed.Slug).IsEqualTo("revolut");
    }

    [Test]
    public async Task A_multi_currency_account_can_be_created_with_an_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var created = await CreateGroupAsync(anna, NewGroup("Revolut", Family, integrationKey: "test-multi-id",
            identifier: "  GB29 REVO 0099 7012 3456 78 "));

        var group = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{created.Id}"))!;
        await Assert.That(group.Identifier).IsEqualTo("GB29 REVO 0099 7012 3456 78");
        await Assert.That((await ListGroupsAsync(anna)).Single().Identifier).IsEqualTo("GB29 REVO 0099 7012 3456 78");
        // The Accounts in it keep having no identifier of their own.
        await Assert.That(group.Accounts.Single().Identifier).IsNull();
    }

    [Test]
    public async Task The_identifier_of_a_multi_currency_account_is_optional()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, integrationKey: "test-multi-id", identifier: " "));

        await Assert.That(group.Identifier).IsNull();
    }

    [Test]
    public async Task An_identifier_is_refused_when_the_multi_currency_integration_cannot_provide_one()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts",
            NewGroup("Revolut", Family, integrationKey: "test-multi", identifier: "GB29 REVO 0099 7012 3456 78"));

        await AssertValidationErrorAsync(res, "identifier");
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task An_identifier_longer_than_64_characters_is_refused()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts",
            NewGroup("Revolut", Family, integrationKey: "test-multi-id", identifier: new string('1', 65)));

        await AssertValidationErrorAsync(res, "identifier");
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task The_identifier_of_a_multi_currency_account_can_be_set_changed_and_removed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, integrationKey: "test-multi-id"));
        var url = $"/api/multi-currency-accounts/{group.Id}";

        var set = await anna.PatchAsJsonAsync(url, new { name = "Revolut", identifier = " GB29 REVO 0099 7012 3456 78 " });

        await Assert.That(set.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await set.Content.ReadFromJsonAsync<Group>())!.Identifier).IsEqualTo("GB29 REVO 0099 7012 3456 78");
        await Assert.That((await anna.GetFromJsonAsync<Group>(url))!.Identifier).IsEqualTo("GB29 REVO 0099 7012 3456 78");

        await anna.PatchAsJsonAsync(url, new { name = "Revolut", identifier = "GB94 REVO 0099 7098 7654 32" });
        await Assert.That((await anna.GetFromJsonAsync<Group>(url))!.Identifier).IsEqualTo("GB94 REVO 0099 7098 7654 32");

        await anna.PatchAsJsonAsync(url, new { name = "Revolut", identifier = "" });
        await Assert.That((await anna.GetFromJsonAsync<Group>(url))!.Identifier).IsNull();
    }

    [Test]
    public async Task Renaming_a_multi_currency_account_without_an_identifier_in_the_request_keeps_its_identifier()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family, integrationKey: "test-multi-id",
            identifier: "GB29 REVO 0099 7012 3456 78"));

        var res = await anna.PatchAsJsonAsync($"/api/multi-currency-accounts/{group.Id}", new { name = "Revolut Anna" });

        var renamed = (await res.Content.ReadFromJsonAsync<Group>())!;
        await Assert.That(renamed.Name).IsEqualTo("Revolut Anna");
        await Assert.That(renamed.Identifier).IsEqualTo("GB29 REVO 0099 7012 3456 78");
    }

    [Test]
    public async Task Editing_refuses_an_identifier_the_integration_cannot_provide_or_that_is_too_long()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var without = await CreateGroupAsync(anna, NewGroup("Wise", Family, integrationKey: "test-multi"));
        var with = await CreateGroupAsync(anna, NewGroup("Revolut", Family, integrationKey: "test-multi-id"));

        var cannotProvide = await anna.PatchAsJsonAsync($"/api/multi-currency-accounts/{without.Id}",
            new { name = "Wise", identifier = "GB29 REVO 0099 7012 3456 78" });
        var tooLong = await anna.PatchAsJsonAsync($"/api/multi-currency-accounts/{with.Id}",
            new { name = "Renamed", identifier = new string('1', 65) });

        await AssertValidationErrorAsync(cannotProvide, "identifier");
        await AssertValidationErrorAsync(tooLong, "identifier");
        var unchanged = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{with.Id}"))!;
        await Assert.That(unchanged.Name).IsEqualTo("Revolut");
        await Assert.That(unchanged.Identifier).IsNull();
    }

    [Test]
    public async Task Accounts_cannot_be_added_to_a_multi_currency_account_by_hand()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", Family));

        var res = await anna.PostAsJsonAsync("/api/accounts", new
        {
            name = "Revolut EUR",
            owner = Family,
            integrationKey = "test",
            currency = "EUR",
            openingBalance = 0m,
            openingDate = "2026-01-01",
            multiCurrencyAccountId = group.Id,
        });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var created = (await res.Content.ReadFromJsonAsync<Account>())!;
        await Assert.That(created.MultiCurrencyAccountId).IsNull();
        var unchanged = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{group.Id}"))!;
        await Assert.That(unchanged.Accounts.Select(a => a.Currency)).IsEquivalentTo(["CHF"]);
    }

    [Test]
    public async Task A_multi_currency_integration_cannot_be_chosen_for_a_plain_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/accounts", new
        {
            name = "Revolut CHF",
            owner = Family,
            integrationKey = "test-multi",
            currency = "CHF",
            openingBalance = 0m,
            openingDate = "2026-01-01",
        });

        await AssertValidationErrorAsync(res, "integrationKey");
        await Assert.That(await ListAccountsAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task A_single_currency_integration_cannot_be_chosen_for_a_multi_currency_account()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var res = await anna.PostAsJsonAsync("/api/multi-currency-accounts", NewGroup("TKB", Family, integrationKey: "test"));

        await AssertValidationErrorAsync(res, "integrationKey");
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
    }

    [Test]
    public async Task The_handover_list_offers_a_multi_currency_account_as_a_whole()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", new { kind = "member", memberId = annaId }));
        await AddCurrencyAccountAsync(group.Id, "USD", openingBalance: 0m);
        await LoginAsAdminAsync();

        var list = (await Client.GetFromJsonAsync<List<HandoverItem>>($"/api/admin/members/{annaId}/accounts"))!;

        await Assert.That(list).IsEquivalentTo([new HandoverItem(group.Id, "Revolut", "CHF", true)]);
    }

    [Test]
    public async Task Handing_over_a_deactivated_members_multi_currency_account_moves_all_its_accounts()
    {
        var bobId = await AddMemberAsync("bob");
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", new { kind = "member", memberId = annaId }));
        var dollars = await AddCurrencyAccountAsync(group.Id, "USD", openingBalance: 0m);
        await LoginAsAdminAsync();

        var res = await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/deactivate", new
        {
            handover = new[] { new { multiCurrencyAccountId = group.Id, owner = new { kind = "member", memberId = bobId } } },
        });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using var bob = NewClient();
        await LoginAsync(bob, "bob", MemberPassword);
        var bobs = (await bob.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{group.Id}"))!;
        await Assert.That(bobs.Owner.MemberId).IsEqualTo(bobId);
        await Assert.That(bobs.Accounts.Select(a => a.Owner.MemberId)).IsEquivalentTo([(Guid?)bobId, bobId]);
        var bobsDollars = (await bob.GetFromJsonAsync<Account>($"/api/accounts/{dollars}"))!;
        await Assert.That(bobsDollars.Owner.Name).IsEqualTo("Bob (bob)");
    }

    [Test]
    public async Task One_account_of_a_multi_currency_account_cannot_be_handed_over_on_its_own()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var annaId = await MemberIdAsync(anna);
        var group = await CreateGroupAsync(anna, NewGroup("Revolut", new { kind = "member", memberId = annaId }));
        await LoginAsAdminAsync();

        var res = await Client.PostAsJsonAsync($"/api/admin/members/{annaId}/deactivate", new
        {
            handover = new[] { new { accountId = group.Accounts.Single().Id, owner = Family } },
        });

        await AssertValidationErrorAsync(res, "handover");
        var unchanged = (await anna.GetFromJsonAsync<Group>($"/api/multi-currency-accounts/{group.Id}"))!;
        await Assert.That(unchanged.Accounts.Single().Owner.MemberId).IsEqualTo(annaId);
    }

    // ---- helpers ----

    /// Adds an Account in another currency to a group, as importing a statement with a new currency will (#25).
    private async Task<Guid> AddCurrencyAccountAsync(Guid groupId, string currency, decimal openingBalance)
    {
        using var scope = App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TreasuryDbContext>();
        var group = await db.MultiCurrencyAccounts.Include(g => g.Accounts).SingleAsync(g => g.Id == groupId);
        var account = new Api.Accounts.Account
        {
            Name = $"{group.Name} {currency}",
            Slug = $"{group.Slug}-{currency.ToLowerInvariant()}",
            Owner = group.Owner,
            IntegrationKey = group.IntegrationKey,
            Currency = currency,
            OpeningBalance = openingBalance,
            OpeningDate = new DateOnly(2026, 1, 1),
            MultiCurrencyAccountId = group.Id,
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    /// Polls the Multi-currency accounts until the condition holds. Exchange rates are fetched in the background
    /// on a schedule, so the clock is moved past the next run between polls.
    private async Task<List<Group>> EventuallyAsync(HttpClient client, Func<List<Group>, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var list = await ListGroupsAsync(client);
            if (done(list)) return list;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met.");
            await Task.Delay(25);
            App.Clock.Advance(TimeSpan.FromMinutes(30));
        }
    }

    private static readonly object Family = new { kind = "family" };

    private static object NewGroup(string name, object owner, string mainCurrency = "CHF",
        string integrationKey = "test-multi", decimal openingBalance = 100m, string? identifier = null) => new
    {
        name,
        owner,
        integrationKey,
        mainCurrency,
        openingBalance,
        openingDate = "2026-01-01",
        identifier,
    };

    private static async Task<Group> CreateGroupAsync(HttpClient client, object request)
    {
        var res = await client.PostAsJsonAsync("/api/multi-currency-accounts", request);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Group>())!;
    }

    private static async Task<List<Group>> ListGroupsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Group>>("/api/multi-currency-accounts"))!;

    private static async Task<List<Account>> ListAccountsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Account>>("/api/accounts"))!;

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, params string[] fields)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        foreach (var field in fields) await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private record Me(Guid Id);

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record OwnerDto(string Kind, Guid? MemberId, string Name);

    private record Account(Guid Id, string Name, OwnerDto Owner, string IntegrationKey, string Currency,
        decimal OpeningBalance, DateOnly OpeningDate, string? Identifier, decimal Balance, Guid? MultiCurrencyAccountId);

    private record Group(Guid Id, string Name, string Slug, OwnerDto Owner, string IntegrationKey, string MainCurrency,
        string? Identifier, List<Account> Accounts, decimal? TotalBalance, decimal? TotalBalanceChf);

    private record HandoverItem(Guid Id, string Name, string Currency, bool IsMultiCurrencyAccount);

    private record Integration(string Key, string Name, bool CanProvideIdentifier, bool HoldsSeveralCurrencies);
}
