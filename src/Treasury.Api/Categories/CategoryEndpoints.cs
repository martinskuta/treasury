using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Transactions;

namespace Treasury.Api.Categories;

public static class CategoryEndpoints
{
    public const int MaxNameLength = 100;

    public record CreateCategoryRequest(string? Name, Guid? ParentId);

    public record RenameCategoryRequest(string? Name);

    /// ParentId null = move to the top level.
    public record MoveCategoryRequest(Guid? ParentId);

    public record CategoryResponse(Guid Id, string Name, Guid? ParentId, string Path, bool IsBuiltIn, string? BuiltInKey);

    /// /api/categories: the Family's shared Category tree. Every Member may change it (except built-in Categories).
    public static void MapCategories(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories");

        // Flat list in tree order (each Category followed by its subtree); the client nests by ParentId.
        categories.MapGet("/", async (TreasuryDbContext db) =>
        {
            var tree = await CategoryTree.LoadAsync(db);
            return Results.Ok(tree.DepthFirst().Select(c => ToResponse(tree, c)));
        });

        categories.MapPost("/", async (CreateCategoryRequest request, TreasuryDbContext db) =>
        {
            var tree = await CategoryTree.LoadAsync(db);
            var errors = new Dictionary<string, string[]>();
            ValidateParent(tree, request.ParentId, errors);
            var name = ValidateName(tree, request.Name, request.ParentId, null, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var category = new Category { Name = name!, ParentId = request.ParentId };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            var updated = await CategoryTree.LoadAsync(db);
            return Results.Created($"/api/categories/{category.Id}", ToResponse(updated, category));
        });

        categories.MapPatch("/{id:guid}", async (Guid id, RenameCategoryRequest request, TreasuryDbContext db) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            if (category.IsBuiltIn) return BuiltInProblem();

            var tree = await CategoryTree.LoadAsync(db);
            var errors = new Dictionary<string, string[]>();
            var name = ValidateName(tree, request.Name, category.ParentId, category.Id, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            category.Name = name!;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(await CategoryTree.LoadAsync(db), category));
        });

        // Moves the Category with its whole subtree; parentId null = to the top level.
        categories.MapPost("/{id:guid}/move", async (Guid id, MoveCategoryRequest request, TreasuryDbContext db) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            if (category.IsBuiltIn) return BuiltInProblem();

            var tree = await CategoryTree.LoadAsync(db);
            var errors = new Dictionary<string, string[]>();
            ValidateParent(tree, request.ParentId, errors);
            if (request.ParentId is { } parentId && tree.IsSelfOrDescendant(parentId, category.Id))
                errors["parentId"] = ["A Category can't be moved into itself or one of its subcategories."];
            if (errors.Count == 0) ValidateName(tree, category.Name, request.ParentId, category.Id, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            category.ParentId = request.ParentId;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(await CategoryTree.LoadAsync(db), category));
        });

        // A Category in use needs a decision for ALL its Transactions (also those the remover can't see, since the tree
        // is shared): ?moveTo={categoryId} or ?uncategorise=true. A Category with subcategories can't be removed.
        categories.MapDelete("/{id:guid}", async (Guid id, Guid? moveTo, bool? uncategorise, TreasuryDbContext db) =>
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null) return Results.NotFound();
            if (category.IsBuiltIn) return BuiltInProblem();
            if (await db.Categories.AnyAsync(c => c.ParentId == id))
                return Problem("category", "Move or remove its subcategories first.");

            if (moveTo is { } target)
            {
                if (uncategorise == true)
                    return Problem("moveTo", "Either move the Transactions or make them Uncategorised.");
                if (target == id || !await db.Categories.AnyAsync(c => c.Id == target))
                    return Problem("moveTo", "Choose another existing Category.");
            }

            var used = db.Transactions.Where(t => t.CategoryId == id);
            if (moveTo is null && uncategorise != true && await used.AnyAsync())
                return Problem("transactions",
                    "Transactions use this Category: move them to another Category or make them Uncategorised.");

            // (The FK is Restrict, so a Transaction categorised meanwhile makes the delete fail instead of vanishing.)
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                if (moveTo is { } to)
                    // A bulk move keeps whether each Category was set by hand.
                    await used.ExecuteUpdateAsync(s => s.SetProperty(t => t.CategoryId, to));
                else if (uncategorise == true)
                    // Back in the Uncategorised queue, where Rules (#15) may categorise them again.
                    await used.ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.CategoryId, (Guid?)null)
                        .SetProperty(t => t.ManuallySet, t => t.ManuallySet & ~ManuallySetFields.Category));
                await db.Categories.Where(c => c.Id == id).ExecuteDeleteAsync();
                await tx.CommitAsync();
            });
            return Results.NoContent();
        });
    }

    private static IResult BuiltInProblem() => Problem("category", "Built-in Categories can't be changed.");

    private static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// A parent must exist and must not be built-in (built-in Categories get no children).
    private static void ValidateParent(CategoryTree tree, Guid? parentId, Dictionary<string, string[]> errors)
    {
        if (parentId is not { } id) return;
        var parent = tree.Find(id);
        if (parent is null) errors["parentId"] = ["Choose an existing Category."];
        else if (parent.IsBuiltIn) errors["parentId"] = ["Built-in Categories can't get subcategories."];
    }

    /// Trimmed name; required, at most MaxNameLength, unique (ignoring case) among the other children of the parent.
    private static string? ValidateName(CategoryTree tree, string? requested, Guid? parentId, Guid? self,
        Dictionary<string, string[]> errors)
    {
        var name = requested?.Trim();
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Enter a name."];
        else if (name.Length > MaxNameLength) errors["name"] = [$"Use at most {MaxNameLength} characters."];
        else if (tree.ChildrenOf(parentId).Any(c => c.Id != self && string.Equals(c.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            errors["name"] = ["Another Category here already has this name."];
        return name;
    }

    private static CategoryResponse ToResponse(CategoryTree tree, Category c) =>
        new(c.Id, c.Name, c.ParentId, tree.PathOf(c.Id), c.IsBuiltIn, c.BuiltInKey);
}
