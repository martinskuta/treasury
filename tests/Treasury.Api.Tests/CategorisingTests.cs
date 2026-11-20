using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

/// Setting Categories on Transactions, the Uncategorised queue, and removing Categories that are in use.
public class CategorisingTests : ApiTestBase
{
    [Test]
    public async Task A_member_can_set_a_category_on_an_expense_or_income_and_it_is_recorded_as_a_manual_edit()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var lunch = await AddAsync(anna, wallet.Id, -25m, description: "Lunch");
        var salary = await AddAsync(anna, wallet.Id, 5000m, description: "Salary");
        var eatingOut = await FindCategoryAsync(anna, "Food › Eating out");
        var salaryCategory = await FindCategoryAsync(anna, "Income › Salary");

        await Assert.That(lunch.Category).IsNull();
        await Assert.That(lunch.ManuallySet).IsEmpty();

        var categorised = await PatchOkAsync(anna, lunch.Id, new { categoryId = eatingOut.Id });
        await PatchOkAsync(anna, salary.Id, new { categoryId = salaryCategory.Id });

        await Assert.That(categorised.Category).IsEqualTo(new CategoryRef(eatingOut.Id, "Eating out", "Food › Eating out"));
        await Assert.That(categorised.ManuallySet).IsEquivalentTo(["category"]);
        var reloaded = await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{salary.Id}");
        await Assert.That(reloaded!.Category!.Path).IsEqualTo("Income › Salary");
    }

    [Test]
    public async Task A_category_can_be_given_when_adding_a_transaction()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var groceries = await FindCategoryAsync(anna, "Food › Groceries");

        var t = await AddAsync(anna, wallet.Id, -80m, categoryId: groceries.Id);

        await Assert.That(t.Category!.Id).IsEqualTo(groceries.Id);
        await Assert.That(t.ManuallySet).IsEquivalentTo(["category"]);
    }

    [Test]
    public async Task An_unknown_category_is_rejected()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -5m);

        await AssertValidationErrorAsync(
            await anna.PatchAsJsonAsync($"/api/transactions/{t.Id}", new { categoryId = Guid.NewGuid() }), "categoryId");
        await AssertValidationErrorAsync(await anna.PostAsJsonAsync("/api/transactions",
            new { accountId = wallet.Id, date = "2026-11-18", amount = -1m, description = "x", categoryId = Guid.NewGuid() }),
            "categoryId");
        await Assert.That((await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}"))!.Category).IsNull();
    }

    [Test]
    public async Task Any_member_who_sees_a_transaction_can_categorise_it_and_others_cannot()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var annasWallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var shared = await AddAsync(anna, joint.Id, -40m);
        var secret = await AddAsync(anna, annasWallet.Id, -40m);
        var health = await FindCategoryAsync(bob, "Health");

        await PatchOkAsync(bob, shared.Id, new { categoryId = health.Id });
        var res = await bob.PatchAsJsonAsync($"/api/transactions/{secret.Id}", new { categoryId = health.Id });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{shared.Id}"))!.Category!.Id)
            .IsEqualTo(health.Id);
        await Assert.That((await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{secret.Id}"))!.Category).IsNull();
    }

    [Test]
    public async Task A_member_can_make_a_transaction_uncategorised_again()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var t = await AddAsync(anna, wallet.Id, -5m, categoryId: (await FindCategoryAsync(anna, "Kids")).Id);

        var cleared = await PatchOkAsync(anna, t.Id, new { uncategorise = true });

        await Assert.That(cleared.Category).IsNull();
        // Uncategorised again means "let Rules decide": no manual Category is left behind.
        await Assert.That(cleared.ManuallySet).IsEmpty();
    }

    [Test]
    public async Task The_uncategorised_queue_lists_every_visible_income_and_expense_without_a_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var annasWallet = await CreateAccountAsync(anna, "Anna cash", await MeAsync(anna));
        var bobsWallet = await CreateAccountAsync(bob, "Bob cash", await MeAsync(bob));
        var joint = await CreateAccountAsync(anna, "Joint", Family);
        var kids = await FindCategoryAsync(anna, "Kids");
        var annasLunch = await AddAsync(anna, annasWallet.Id, -20m, description: "Anna lunch");
        var refund = await AddAsync(anna, annasWallet.Id, 15m, description: "Refund");
        await AddAsync(anna, annasWallet.Id, -30m, description: "Toys", categoryId: kids.Id);
        var groceries = await AddAsync(bob, joint.Id, -60m, description: "Groceries");
        await AddAsync(bob, bobsWallet.Id, -10m, description: "Bob's secret");

        var queue = await UncategorisedAsync(anna);

        await Assert.That(queue.Select(t => t.Id)).IsEquivalentTo([annasLunch.Id, refund.Id, groceries.Id]);
        await Assert.That((await UncategorisedAsync(bob)).Select(t => t.Description)).IsEquivalentTo(["Groceries", "Bob's secret"]);
    }

    [Test]
    public async Task Categorising_from_the_queue_removes_the_transaction_from_it()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var first = await AddAsync(anna, wallet.Id, -20m);
        var second = await AddAsync(anna, wallet.Id, -30m);

        await PatchOkAsync(anna, first.Id, new { categoryId = (await FindCategoryAsync(anna, "Transport")).Id });

        await Assert.That((await UncategorisedAsync(anna)).Select(t => t.Id)).IsEquivalentTo([second.Id]);
        await Assert.That((await anna.GetFromJsonAsync<List<Transaction>>("/api/transactions"))!).Count().IsEqualTo(2);
    }

    [Test]
    public async Task An_unused_category_can_be_removed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var housing = await FindCategoryAsync(anna, "Housing");

        var res = await anna.DeleteAsync($"/api/categories/{housing.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await CategoriesAsync(anna)).Select(c => c.Id)).DoesNotContain(housing.Id);
    }

    [Test]
    public async Task A_category_with_subcategories_cannot_be_removed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var food = await FindCategoryAsync(anna, "Food");

        await AssertValidationErrorAsync(await anna.DeleteAsync($"/api/categories/{food.Id}"), "category");
        await Assert.That((await CategoriesAsync(anna)).Select(c => c.Id)).Contains(food.Id);
    }

    [Test]
    public async Task A_category_in_use_cannot_be_removed_without_deciding_what_happens_to_its_transactions()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var leisure = await FindCategoryAsync(anna, "Leisure");
        var t = await AddAsync(anna, wallet.Id, -50m, categoryId: leisure.Id);

        // Bob can't see Anna's Transaction, but the shared Category is still in use.
        await AssertValidationErrorAsync(await bob.DeleteAsync($"/api/categories/{leisure.Id}"), "transactions");

        await Assert.That((await CategoriesAsync(anna)).Select(c => c.Id)).Contains(leisure.Id);
        await Assert.That((await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{t.Id}"))!.Category!.Id).IsEqualTo(leisure.Id);
    }

    [Test]
    public async Task Removing_a_category_can_move_all_its_transactions_to_another_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var eatingOut = await FindCategoryAsync(anna, "Food › Eating out");
        var groceries = await FindCategoryAsync(anna, "Food › Groceries");
        var annas = await AddAsync(anna, wallet.Id, -50m, categoryId: eatingOut.Id);

        var res = await bob.DeleteAsync($"/api/categories/{eatingOut.Id}?moveTo={groceries.Id}");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var moved = (await anna.GetFromJsonAsync<Transaction>($"/api/transactions/{annas.Id}"))!;
        await Assert.That(moved.Category!.Id).IsEqualTo(groceries.Id);
        await Assert.That((await CategoriesAsync(anna)).Select(c => c.Id)).DoesNotContain(eatingOut.Id);
    }

    [Test]
    public async Task Removing_a_category_can_make_its_transactions_uncategorised()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var kids = await FindCategoryAsync(anna, "Kids");
        var t = await AddAsync(anna, wallet.Id, -50m, categoryId: kids.Id);

        var res = await anna.DeleteAsync($"/api/categories/{kids.Id}?uncategorise=true");

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        var queue = await UncategorisedAsync(anna);
        await Assert.That(queue.Select(x => x.Id)).IsEquivalentTo([t.Id]);
        await Assert.That(queue[0].ManuallySet).IsEmpty();
    }

    [Test]
    public async Task Transactions_cannot_be_moved_to_an_unknown_category_or_the_removed_one()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var wallet = await CreateAccountAsync(anna, "Cash", await MeAsync(anna));
        var kids = await FindCategoryAsync(anna, "Kids");
        await AddAsync(anna, wallet.Id, -50m, categoryId: kids.Id);

        await AssertValidationErrorAsync(await anna.DeleteAsync($"/api/categories/{kids.Id}?moveTo={Guid.NewGuid()}"), "moveTo");
        await AssertValidationErrorAsync(await anna.DeleteAsync($"/api/categories/{kids.Id}?moveTo={kids.Id}"), "moveTo");
        await AssertValidationErrorAsync(
            await anna.DeleteAsync($"/api/categories/{kids.Id}?moveTo={(await FindCategoryAsync(anna, "Health")).Id}&uncategorise=true"),
            "moveTo");
        await Assert.That((await CategoriesAsync(anna)).Select(c => c.Id)).Contains(kids.Id);
    }

    [Test]
    public async Task A_restart_keeps_the_familys_changes_to_the_starter_tree()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        await anna.DeleteAsync($"/api/categories/{(await FindCategoryAsync(anna, "Housing")).Id}");
        await anna.DeleteAsync($"/api/categories/{(await FindCategoryAsync(anna, "Income › Salary")).Id}");
        var before = await CategoriesAsync(anna);

        await using var restarted = new TreasuryApiFactory(ConnectionString);
        using var client = restarted.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        await LoginAsync(client, "anna", MemberPassword);

        await Assert.That(await CategoriesAsync(client)).IsEquivalentTo(before);
    }

    // ---- helpers ----

    private static async Task<List<Category>> CategoriesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!;

    private static async Task<List<Transaction>> UncategorisedAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Transaction>>("/api/transactions/uncategorised"))!;

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

    private static async Task<Transaction> AddAsync(HttpClient client, Guid accountId, decimal amount,
        string description = "Something", Guid? categoryId = null, object? owner = null)
    {
        var res = await client.PostAsJsonAsync("/api/transactions",
            new { accountId, date = "2026-11-18", amount, description, categoryId, owner });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add transaction failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<Transaction> PatchOkAsync(HttpClient client, Guid id, object body)
    {
        var res = await client.PatchAsJsonAsync($"/api/transactions/{id}", body);
        if (res.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Patch failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Transaction>())!;
    }

    private static async Task<Category> FindCategoryAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!.Single(c => c.Path == path);

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private record Me(Guid Id);

    private record Account(Guid Id, string Name);

    private record Category(Guid Id, string Name, Guid? ParentId, string Path, bool IsBuiltIn);

    private record CategoryRef(Guid Id, string Name, string Path);

    private record Transaction(Guid Id, string Description, decimal Amount, string Type, CategoryRef? Category,
        string[] ManuallySet);
}
