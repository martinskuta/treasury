using System.Net;
using System.Net.Http.Json;

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
            mainCurrency = "GBP",
        });

        await AssertValidationErrorAsync(res, "name", "owner", "integrationKey", "mainCurrency", "openingBalance", "openingDate");
        await Assert.That(await ListGroupsAsync(anna)).IsEmpty();
        await Assert.That(Directory.Exists(App.IngestRoot) ? Directory.GetDirectories(App.IngestRoot) : []).IsEmpty();
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

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static object NewGroup(string name, object owner, string mainCurrency = "CHF",
        string integrationKey = "test-multi", decimal openingBalance = 100m) => new
    {
        name,
        owner,
        integrationKey,
        mainCurrency,
        openingBalance,
        openingDate = "2026-01-01",
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
        decimal OpeningBalance, DateOnly OpeningDate, decimal Balance, Guid? MultiCurrencyAccountId);

    private record Group(Guid Id, string Name, string Slug, OwnerDto Owner, string IntegrationKey, string MainCurrency,
        List<Account> Accounts, decimal? TotalBalance);

    private record Integration(string Key, string Name, bool CanProvideIdentifier, bool HoldsSeveralCurrencies);
}
