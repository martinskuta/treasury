using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Changing Category, Tags or Owner on several Transactions at once (POST /api/transactions/bulk-edit).
public class BulkEditTests : ApiTestBase
{
    [Test]
    public async Task Several_transactions_get_a_category_in_one_action_recorded_as_a_manual_edit()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var a = await AddAsync(anna, cash.Id, -10m);
        var b = await AddAsync(anna, cash.Id, -20m);
        var untouched = await AddAsync(anna, cash.Id, -30m);
        var groceries = await CategoryIdAsync(anna, "Food › Groceries");

        var edited = await BulkOkAsync(anna, new { transactionIds = new[] { a.Id, b.Id }, categoryId = groceries });

        await Assert.That(edited.Select(t => t.Id)).IsEquivalentTo([a.Id, b.Id]);
        foreach (var id in new[] { a.Id, b.Id })
        {
            var t = await GetAsync(anna, id);
            await Assert.That(t.Category!.Id).IsEqualTo(groceries);
            await Assert.That(t.ManuallySet).IsEquivalentTo(["category"]);
        }
        await Assert.That((await GetAsync(anna, untouched.Id)).Category).IsNull();
    }

    [Test]
    public async Task Tags_can_be_added_to_and_removed_from_several_transactions_and_removals_stick()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var wedding = await CreateTagAsync(anna, new { name = "wedding" });
        var a = await AddAsync(anna, cash.Id, -10m, date: "2026-07-03");
        var b = await AddAsync(anna, cash.Id, -20m, date: "2026-07-04", tagIds: [wedding]);
        var holiday = await CreateTagAsync(anna, new { name = "holiday", dateFrom = "2026-07-01", dateTo = "2026-07-14" });

        var edited = await BulkOkAsync(anna, new
        {
            transactionIds = new[] { a.Id, b.Id }, addTagIds = new[] { wedding }, removeTagIds = new[] { holiday },
        });

        foreach (var t in edited)
        {
            await Assert.That(t.Tags.Select(x => x.Name)).IsEquivalentTo(["wedding"]);
            await Assert.That(t.Tags.Single().Sources).IsEquivalentTo(["manual"]);
            await Assert.That(t.ManuallySet).Contains("tags");
        }

        // Re-applying the date-range Tag leaves it off both: the removal by hand sticks, as with PATCH.
        await anna.PutAsJsonAsync($"/api/tags/{holiday}", new { name = "holiday", dateFrom = "2026-06-01", dateTo = "2026-07-31" });
        await Assert.That((await GetAsync(anna, a.Id)).Tags.Select(x => x.Name)).IsEquivalentTo(["wedding"]);
        await Assert.That((await GetAsync(anna, b.Id)).Tags.Select(x => x.Name)).IsEquivalentTo(["wedding"]);
    }

    [Test]
    public async Task Owner_of_several_transactions_changes_in_one_action_and_date_range_tags_follow_the_owner()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var bobs = await MeAsync(bob);
        var a = await AddAsync(anna, joint.Id, -10m, date: "2026-07-03");
        var b = await AddAsync(anna, joint.Id, -20m, date: "2026-07-04");
        await CreateTagAsync(anna, new { name = "bob's trip", dateFrom = "2026-07-01", dateTo = "2026-07-14", owners = new[] { bobs } });
        await Assert.That((await GetAsync(anna, a.Id)).Tags).IsEmpty();

        var edited = await BulkOkAsync(anna, new { transactionIds = new[] { a.Id, b.Id }, owner = bobs });

        var bobId = await MemberIdAsync(bob);
        foreach (var t in edited)
        {
            await Assert.That(t.Owner.MemberId).IsEqualTo(bobId);
            await Assert.That(t.ManuallySet).IsEquivalentTo(["owner"]);
            await Assert.That(t.Tags.Select(x => x.Name)).IsEquivalentTo(["bob's trip"]);
        }
    }

    [Test]
    public async Task A_deactivated_member_cannot_be_made_the_owner()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var gone = await AddMemberAsync("gone");
        await LoginAsAdminAsync();
        await Client.PostAsJsonAsync($"/api/admin/members/{gone}/deactivate", new { handover = Array.Empty<object>() });
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var a = await AddAsync(anna, cash.Id, -10m);

        await AssertValidationErrorAsync(await BulkAsync(anna, new
        {
            transactionIds = new[] { a.Id }, owner = new { kind = "member", memberId = gone },
        }), "owner");
        await Assert.That((await GetAsync(anna, a.Id)).ManuallySet).IsEmpty();
    }

    [Test]
    public async Task Nothing_changes_when_one_selected_transaction_is_a_transfer_given_a_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var savings = await CreateAccountAsync(anna, "Savings", await MeAsync(anna));
        var lunch = await AddAsync(anna, cash.Id, -12m, description: "Lunch");
        var transferOut = await AddAsync(anna, cash.Id, -500m, date: "2026-11-10");
        await AddAsync(anna, savings.Id, 500m, date: "2026-11-10");
        await Assert.That((await GetAsync(anna, transferOut.Id)).Type).IsEqualTo("transfer");

        var problem = await AssertValidationErrorAsync(await BulkAsync(anna, new
        {
            transactionIds = new[] { lunch.Id, transferOut.Id }, categoryId = await CategoryIdAsync(anna, "Leisure"),
        }), $"transactions[{transferOut.Id}]");

        await Assert.That(problem.Errors.Keys).DoesNotContain($"transactions[{lunch.Id}]");
        await Assert.That((await GetAsync(anna, lunch.Id)).Category).IsNull();
    }

    [Test]
    public async Task Transactions_outside_the_members_visibility_cannot_be_bulk_edited()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasCash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var bobsCard = await CreateAccountAsync(bob, "Card", await MeAsync(bob));
        var annas = await AddAsync(anna, annasCash.Id, -10m);
        var bobs = await AddAsync(bob, bobsCard.Id, -20m);
        var unknown = Guid.NewGuid();
        var leisure = await CategoryIdAsync(anna, "Leisure");

        var problem = await AssertValidationErrorAsync(await BulkAsync(anna, new
        {
            transactionIds = new[] { annas.Id, bobs.Id, unknown }, categoryId = leisure,
        }), $"transactions[{bobs.Id}]");

        // Bob's Transaction looks exactly like one that doesn't exist.
        await Assert.That(problem.Errors[$"transactions[{bobs.Id}]"]).IsEquivalentTo(problem.Errors[$"transactions[{unknown}]"]);
        await Assert.That((await GetAsync(anna, annas.Id)).Category).IsNull();
        await Assert.That((await GetAsync(bob, bobs.Id)).Category).IsNull();
    }

    [Test]
    public async Task A_bulk_edit_needs_transactions_and_a_change()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var a = await AddAsync(anna, cash.Id, -10m);

        await AssertValidationErrorAsync(await BulkAsync(anna, new
        {
            transactionIds = Array.Empty<Guid>(), categoryId = await CategoryIdAsync(anna, "Leisure"),
        }), "transactionIds");
        await AssertValidationErrorAsync(await BulkAsync(anna, new { transactionIds = new[] { a.Id } }), "change");
        await AssertValidationErrorAsync(await BulkAsync(anna, new
        {
            transactionIds = new[] { a.Id }, addTagIds = new[] { Guid.NewGuid() },
        }), "addTagIds");
    }

    [Test]
    public async Task Several_transactions_can_be_made_uncategorised_again()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var cash = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var leisure = await CategoryIdAsync(anna, "Leisure");
        var a = await AddAsync(anna, cash.Id, -10m, categoryId: leisure);
        var b = await AddAsync(anna, cash.Id, -20m, categoryId: leisure);

        var edited = await BulkOkAsync(anna, new { transactionIds = new[] { a.Id, b.Id }, uncategorise = true });

        foreach (var t in edited)
        {
            await Assert.That(t.Category).IsNull();
            await Assert.That(t.ManuallySet).IsEmpty();
        }
    }

    // ---- helpers ----

    private static readonly object Family = new { kind = "family" };

    private static async Task<Guid> MemberIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

    private static async Task<object> MeAsync(HttpClient client) =>
        new { kind = "member", memberId = await MemberIdAsync(client) };

    private static async Task<Guid> CategoryIdAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!.Single(c => c.Path == path).Id;

    private static async Task<Guid> CreateTagAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/tags", body);
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Create tag failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Tag>())!.Id;
    }

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
        Guid[]? tagIds = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date, amount, description, owner, categoryId, tagIds });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static Task<HttpResponseMessage> BulkAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/transactions/bulk-edit", body);

    private static async Task<List<Transaction>> BulkOkAsync(HttpClient client, object body)
    {
        var res = await BulkAsync(client, body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Bulk edit failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<List<Transaction>>())!;
    }

    private static async Task<Transaction> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<Transaction>($"/api/transactions/{id}"))!;

    private static async Task<ValidationProblem> AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = (await res.Content.ReadFromJsonAsync<ValidationProblem>())!;
        await Assert.That(problem.Errors.Keys).Contains(field);
        return problem;
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record Category(Guid Id, string Path);

    private record Tag(Guid Id);

    private record CategoryRef(Guid Id, string Path);

    private record TagRef(Guid Id, string Name, string[] Sources);

    private record OwnerDto(string Kind, Guid? MemberId);

    private record Transaction(Guid Id, decimal Amount, string Type, OwnerDto Owner, CategoryRef? Category,
        List<TagRef> Tags, string[] ManuallySet);
}
