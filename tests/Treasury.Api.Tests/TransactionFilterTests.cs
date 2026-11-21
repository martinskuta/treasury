using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Filtering, sorting and paging the Transactions list (GET /api/transactions).
public class TransactionFilterTests : ApiTestBase
{
    [Test]
    public async Task Transactions_can_be_filtered_by_account_date_range_and_text_combined()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var card = await CreateAccountAsync(anna, "Card", await MeAsync(anna));
        var match = await AddAsync(anna, cash.Id, -12m, "2026-10-05", "Migros Zurich");
        await AddAsync(anna, cash.Id, -13m, "2026-09-30", "Migros Bern");       // before the range
        await AddAsync(anna, cash.Id, -14m, "2026-10-06", "Coop");              // text doesn't match
        await AddAsync(anna, card.Id, -15m, "2026-10-07", "Migros online");     // other Account

        var found = await ListAsync(anna, $"accountId={cash.Id}&from=2026-10-01&to=2026-10-31&q=migros");

        await Assert.That(found.Select(t => t.Id)).IsEquivalentTo([match.Id]);
    }

    [Test]
    public async Task Transactions_can_be_filtered_by_owner_category_with_subcategories_and_tag_combined()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var food = await CategoryIdAsync(anna, "Food");
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");
        var housing = await CategoryIdAsync(anna, "Housing");
        var holiday = await CreateTagAsync(anna, "holiday");
        var bobs = await MeAsync(bob);

        var match = await AddAsync(anna, joint.Id, -50m, owner: bobs, categoryId: groceries, tagIds: [holiday]);
        var matchToo = await AddAsync(anna, joint.Id, -60m, owner: bobs, categoryId: food, tagIds: [holiday]);
        await AddAsync(anna, joint.Id, -70m, categoryId: groceries, tagIds: [holiday]);              // Family-owned
        await AddAsync(anna, joint.Id, -80m, owner: bobs, categoryId: housing, tagIds: [holiday]);   // other Category
        await AddAsync(anna, joint.Id, -90m, owner: bobs, categoryId: groceries);                    // no Tag

        var found = await ListAsync(anna, $"owner={await MemberIdAsync(bob)}&categoryId={food}&tagId={holiday}");
        await Assert.That(found.Select(t => t.Id)).IsEquivalentTo([match.Id, matchToo.Id]);

        var family = await ListAsync(anna, $"owner=family&categoryId={groceries}");
        await Assert.That(family.Select(t => t.Amount)).IsEquivalentTo([-70m]);
    }

    [Test]
    public async Task Transactions_can_be_filtered_to_uncategorised_ones()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var uncategorised = await AddAsync(anna, cash.Id, -10m);
        await AddAsync(anna, cash.Id, -20m, categoryId: await CategoryIdAsync(anna, "Housing"));

        var found = await ListAsync(anna, "uncategorised=true");

        await Assert.That(found.Select(t => t.Id)).IsEquivalentTo([uncategorised.Id]);
    }

    [Test]
    public async Task The_uncategorised_queue_can_be_filtered_by_owner_like_the_list()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var annas = await AddAsync(anna, joint.Id, -10m, owner: await MeAsync(anna));
        var bobs = await AddAsync(anna, joint.Id, -20m, owner: await MeAsync(bob));
        var family = await AddAsync(anna, joint.Id, -30m);
        await AddAsync(anna, joint.Id, -40m, owner: await MeAsync(anna), categoryId: await CategoryIdAsync(anna, "Housing"));

        await Assert.That((await UncategorisedAsync(anna, "owner=me")).Select(t => t.Id)).IsEquivalentTo([annas.Id]);
        await Assert.That((await UncategorisedAsync(anna, "owner=family")).Select(t => t.Id)).IsEquivalentTo([family.Id]);
        await Assert.That((await UncategorisedAsync(anna, $"owner={await MemberIdAsync(bob)}")).Select(t => t.Id)).IsEquivalentTo([bobs.Id]);
        await Assert.That((await UncategorisedAsync(anna, "")).Count).IsEqualTo(3);
        await Assert.That((await ListAsync(anna, "owner=me")).Select(t => t.Amount)).IsEquivalentTo([-10m, -40m]);
        await AssertValidationErrorAsync(await anna.GetAsync("/api/transactions/uncategorised?owner=nobody"), "owner");
    }

    [Test]
    public async Task The_text_filter_also_finds_display_names_and_notes()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var byNote = await AddAsync(anna, cash.Id, -10m, description: "POS 4711", note: "Birthday present for Eva");
        var byName = await AddAsync(anna, cash.Id, -20m, description: "TWINT 0815");
        await anna.PatchAsJsonAsync($"/api/transactions/{byName.Id}", new { displayName = "Eva's bakery" });
        await AddAsync(anna, cash.Id, -30m, description: "Rent");
        await AddAsync(anna, cash.Id, -40m, description: "100% match_me"); // LIKE wildcards are taken literally

        await Assert.That((await ListAsync(anna, "q=EVA")).Select(t => t.Id)).IsEquivalentTo([byNote.Id, byName.Id]);
        await Assert.That((await ListAsync(anna, "q=%25")).Select(t => t.Amount)).IsEquivalentTo([-40m]);
        await Assert.That(await ListAsync(anna, "q=a_c")).IsEmpty();
    }

    [Test]
    public async Task Filters_never_reveal_transactions_outside_the_viewers_visibility()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var bobsCard = await CreateAccountAsync(bob, "Bob's card", await MeAsync(bob));
        var holiday = await CreateTagAsync(bob, "holiday");
        var hidden = await AddAsync(bob, bobsCard.Id, -99m, description: "Secret gift", tagIds: [holiday],
            categoryId: await CategoryIdAsync(bob, "Leisure"));
        var shared = await AddAsync(bob, bobsCard.Id, -10m, description: "Secret but shared", owner: Family);

        var bobId = await MemberIdAsync(bob);
        foreach (var query in new[]
                 {
                     $"accountId={bobsCard.Id}", $"owner={bobId}", $"tagId={holiday}", "q=secret",
                     $"categoryId={await CategoryIdAsync(anna, "Leisure")}", "from=2026-01-01&to=2026-12-31",
                 })
        {
            var found = await ListAsync(anna, query);
            await Assert.That(found.Select(t => t.Id)).DoesNotContain(hidden.Id);
        }
        // A Family-owned Transaction in Bob's Account is visible to Anna, so filters find it.
        await Assert.That((await ListAsync(anna, $"accountId={bobsCard.Id}")).Select(t => t.Id)).IsEquivalentTo([shared.Id]);
    }

    [Test]
    public async Task The_list_can_be_sorted_and_paged()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        await AddAsync(anna, cash.Id, -30m, "2026-10-01");
        await AddAsync(anna, cash.Id, -10m, "2026-10-03");
        await AddAsync(anna, cash.Id, 50m, "2026-10-02");
        await AddAsync(anna, cash.Id, -20m, "2026-10-04");

        var newest = await anna.GetAsync("/api/transactions?limit=2");
        await Assert.That(newest.Headers.GetValues("X-Total-Count").Single()).IsEqualTo("4");
        await Assert.That((await newest.Content.ReadFromJsonAsync<List<Transaction>>())!.Select(t => t.Amount))
            .IsEquivalentTo([-20m, -10m], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That((await ListAsync(anna, "limit=2&offset=2")).Select(t => t.Amount))
            .IsEquivalentTo([50m, -30m], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That((await ListAsync(anna, "sort=amount-asc")).Select(t => t.Amount))
            .IsEquivalentTo([-30m, -20m, -10m, 50m], TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That((await ListAsync(anna, "sort=date-asc&limit=1")).Select(t => t.Date))
            .IsEquivalentTo([new DateOnly(2026, 10, 1)]);
    }

    [Test]
    public async Task Invalid_filters_are_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        await AssertValidationErrorAsync(await anna.GetAsync("/api/transactions?owner=nobody"), "owner");
        await AssertValidationErrorAsync(await anna.GetAsync($"/api/transactions?categoryId={Guid.NewGuid()}"), "categoryId");
        await AssertValidationErrorAsync(await anna.GetAsync("/api/transactions?sort=random"), "sort");
        await AssertValidationErrorAsync(await anna.GetAsync("/api/transactions?limit=0"), "limit");
        await AssertValidationErrorAsync(await anna.GetAsync("/api/transactions?from=2026-10-05&to=2026-10-01"), "to");
    }

    // ---- helpers ----

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private static async Task<Guid> CreateTagAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/tags", new { name });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Tag>())!.Id;
    }

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Category(Guid Id, string Path);

    private record Tag(Guid Id);

    private static readonly object Family = new { kind = "family" };

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

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

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount,
        string date = "2026-11-18", string description = "Something", object? owner = null, Guid? categoryId = null,
        Guid[]? tagIds = null, string? note = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description, owner, categoryId, tagIds, note });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<List<Transaction>> ListAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/api/transactions?{query}");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"List failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<Transaction>>())!;
    }

    private static async Task<List<Transaction>> UncategorisedAsync(HttpClient client, string query)
    {
        var res = await client.GetAsync($"/api/transactions/uncategorised?{query}");
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Uncategorised failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<Transaction>>())!;
    }

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record Transaction(Guid Id, DateOnly Date, decimal Amount, string Description);
}
