using System.Net;
using System.Net.Http.Json;

namespace Treasury.Api.Tests;

public class CategoryTests : ApiTestBase
{
    [Test]
    public async Task A_new_installation_contains_the_starter_tree_and_the_built_in_contribution_categories()
    {
        using var anna = await LoginAsNewMemberAsync("anna");

        var categories = await ListCategoriesAsync(anna);

        await Assert.That(categories.Select(c => c.Path)).IsEquivalentTo([
            "Food", "Food › Groceries", "Food › Eating out", "Housing", "Transport", "Health", "Kids", "Leisure",
            "Income", "Income › Salary",
            "Contributions", "Contributions › To Family", "Contributions › From Family",
            "Contributions › To Members", "Contributions › From Members",
        ]);
        await Assert.That(categories.Where(c => c.IsBuiltIn).Select(c => c.Path))
            .IsEquivalentTo(categories.Where(c => c.Path.StartsWith("Contributions")).Select(c => c.Path));
        var food = categories.Single(c => c.Path == "Food");
        await Assert.That(categories.Single(c => c.Path == "Food › Eating out").ParentId).IsEqualTo(food.Id);
        await Assert.That(food.ParentId).IsNull();
    }

    [Test]
    public async Task A_member_can_add_top_level_categories_and_subcategories()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        using var bob = await LoginAsNewMemberAsync("bob");

        var res = await anna.PostAsJsonAsync("/api/categories", new { name = " Pets " });
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var pets = (await res.Content.ReadFromJsonAsync<Category>())!;
        var vet = await AddCategoryAsync(anna, "Vet", pets.Id);

        await Assert.That(pets.Path).IsEqualTo("Pets");
        await Assert.That(vet.Path).IsEqualTo("Pets › Vet");
        await Assert.That(vet.IsBuiltIn).IsFalse();
        // The tree is shared by the whole Family.
        var seenByBob = await ListCategoriesAsync(bob);
        await Assert.That(seenByBob).Contains(vet);
        await Assert.That(seenByBob).Contains(pets);
    }

    [Test]
    [Arguments("")]
    [Arguments("groceries")]
    public async Task A_category_needs_a_name_that_is_unique_among_its_siblings(string name)
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var food = await FindAsync(anna, "Food");

        var res = await anna.PostAsJsonAsync("/api/categories", new { name, parentId = food.Id });

        await AssertValidationErrorAsync(res, "name");
        // The same name elsewhere in the tree is fine.
        await Assert.That((await AddCategoryAsync(anna, "Groceries", (await FindAsync(anna, "Kids")).Id)).Path)
            .IsEqualTo("Kids › Groceries");
    }

    [Test]
    public async Task A_category_cannot_be_added_under_an_unknown_or_built_in_category()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var contributions = await FindAsync(anna, "Contributions");

        await AssertValidationErrorAsync(
            await anna.PostAsJsonAsync("/api/categories", new { name = "Pocket money", parentId = contributions.Id }), "parentId");
        await AssertValidationErrorAsync(
            await anna.PostAsJsonAsync("/api/categories", new { name = "X", parentId = Guid.NewGuid() }), "parentId");
    }

    [Test]
    public async Task A_member_can_rename_a_category_and_its_subtree_follows()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var food = await FindAsync(anna, "Food");

        var res = await anna.PatchAsJsonAsync($"/api/categories/{food.Id}", new { name = "Food & drink" });

        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await res.Content.ReadFromJsonAsync<Category>())!.Path).IsEqualTo("Food & drink");
        await Assert.That((await ListCategoriesAsync(anna)).Select(c => c.Path))
            .Contains("Food & drink › Eating out");
        await AssertValidationErrorAsync(
            await anna.PatchAsJsonAsync($"/api/categories/{food.Id}", new { name = "Housing" }), "name");
    }

    [Test]
    public async Task A_member_can_move_a_category_with_its_subtree_under_another_or_to_the_top()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var food = await FindAsync(anna, "Food");
        var eatingOut = await FindAsync(anna, "Food › Eating out");
        var restaurants = await AddCategoryAsync(anna, "Restaurants", eatingOut.Id);
        var leisure = await FindAsync(anna, "Leisure");

        var moved = await MoveAsync(anna, eatingOut.Id, leisure.Id);
        await Assert.That(moved.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await FindByIdAsync(anna, restaurants.Id)).Path).IsEqualTo("Leisure › Eating out › Restaurants");

        await Assert.That((await MoveAsync(anna, restaurants.Id, null)).StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await FindByIdAsync(anna, restaurants.Id)).Path).IsEqualTo("Restaurants");
        await Assert.That((await FindByIdAsync(anna, food.Id)).Path).IsEqualTo("Food");
    }

    [Test]
    public async Task A_category_cannot_be_moved_into_its_own_subtree_under_a_built_in_or_next_to_a_namesake()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var food = await FindAsync(anna, "Food");
        var groceries = await FindAsync(anna, "Food › Groceries");
        var kidsGroceries = await AddCategoryAsync(anna, "Groceries", (await FindAsync(anna, "Kids")).Id);

        await AssertValidationErrorAsync(await MoveAsync(anna, food.Id, food.Id), "parentId");
        await AssertValidationErrorAsync(await MoveAsync(anna, food.Id, groceries.Id), "parentId");
        await AssertValidationErrorAsync(await MoveAsync(anna, food.Id, (await FindAsync(anna, "Contributions")).Id), "parentId");
        await AssertValidationErrorAsync(await MoveAsync(anna, kidsGroceries.Id, food.Id), "name");
        await Assert.That((await FindByIdAsync(anna, food.Id)).ParentId).IsNull();
    }

    [Test]
    public async Task Built_in_categories_cannot_be_renamed_moved_or_removed()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var before = await ListCategoriesAsync(anna);
        var toFamily = before.Single(c => c.Path == "Contributions › To Family");
        var contributions = before.Single(c => c.Path == "Contributions");

        await AssertValidationErrorAsync(
            await anna.PatchAsJsonAsync($"/api/categories/{toFamily.Id}", new { name = "Pocket money" }), "category");
        await AssertValidationErrorAsync(await MoveAsync(anna, toFamily.Id, null), "category");
        await AssertValidationErrorAsync(await MoveAsync(anna, contributions.Id, (await FindAsync(anna, "Food")).Id), "category");
        await AssertValidationErrorAsync(await anna.DeleteAsync($"/api/categories/{toFamily.Id}"), "category");

        await Assert.That(await ListCategoriesAsync(anna)).IsEquivalentTo(before);
    }

    [Test]
    public async Task Unknown_categories_are_not_found()
    {
        using var anna = await LoginAsNewMemberAsync("anna");
        var id = Guid.NewGuid();

        await Assert.That((await anna.PatchAsJsonAsync($"/api/categories/{id}", new { name = "X" })).StatusCode)
            .IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await MoveAsync(anna, id, null)).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That((await anna.DeleteAsync($"/api/categories/{id}")).StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid id, Guid? parentId) =>
        client.PostAsJsonAsync($"/api/categories/{id}/move", new { parentId });

    private static async Task<Category> FindByIdAsync(HttpClient client, Guid id) =>
        (await ListCategoriesAsync(client)).Single(c => c.Id == id);

    private static async Task<Category> AddCategoryAsync(HttpClient client, string name, Guid? parentId = null)
    {
        var res = await client.PostAsJsonAsync("/api/categories", new { name, parentId });
        if (res.StatusCode != HttpStatusCode.Created)
            throw new InvalidOperationException($"Add category failed: {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<Category>())!;
    }

    private static async Task<Category> FindAsync(HttpClient client, string path) =>
        (await ListCategoriesAsync(client)).Single(c => c.Path == path);

    private static async Task AssertValidationErrorAsync(HttpResponseMessage res, string field)
    {
        await Assert.That(res.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await res.Content.ReadFromJsonAsync<ValidationProblem>();
        await Assert.That(problem!.Errors.Keys).Contains(field);
    }

    private record ValidationProblem(Dictionary<string, string[]> Errors);

    private static async Task<List<Category>> ListCategoriesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<Category>>("/api/categories"))!;

    private record Category(Guid Id, string Name, Guid? ParentId, string Path, bool IsBuiltIn);
}
