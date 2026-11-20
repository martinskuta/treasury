using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Categories;

public static class CategorySeeding
{
    /// The editable starter tree a new installation gets: (name, children).
    public static readonly IReadOnlyList<(string Name, string[] Children)> StarterTree =
    [
        ("Food", ["Groceries", "Eating out"]),
        ("Housing", []),
        ("Transport", []),
        ("Health", []),
        ("Kids", []),
        ("Leisure", []),
        ("Income", ["Salary"]),
    ];

    /// Runs at every startup (after migrations, next to the first-Admin seeding):
    /// - first startup (no Categories at all): creates the starter tree;
    /// - always: creates any missing built-in Category (by key), so they exist even if added in a later version.
    /// The starter tree is never re-created later, so Categories the Family removed stay removed.
    public static async Task SeedCategoriesAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<TreasuryDbContext>();
        var existing = await db.Categories.ToListAsync();

        if (existing.Count == 0)
        {
            foreach (var (name, children) in StarterTree)
            {
                var parent = new Category { Name = name };
                db.Categories.Add(parent);
                foreach (var child in children) db.Categories.Add(new Category { Name = child, ParentId = parent.Id });
            }
        }

        var byKey = existing.Where(c => c.IsBuiltIn).ToDictionary(c => c.BuiltInKey!);
        foreach (var (key, name, parentKey) in BuiltInCategories.All)
        {
            if (byKey.ContainsKey(key)) continue;
            var category = new Category { Name = name, BuiltInKey = key, ParentId = parentKey is null ? null : byKey[parentKey].Id };
            db.Categories.Add(category);
            byKey[key] = category;
        }

        await db.SaveChangesAsync();
    }
}
