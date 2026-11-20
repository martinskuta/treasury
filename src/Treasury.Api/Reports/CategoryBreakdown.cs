using Treasury.Api.Categories;
using Treasury.Api.Transactions;

namespace Treasury.Api.Reports;

/// Totals of one side (Income or Expense) of a report, in CHF, as positive amounts, rolled up through the
/// Category tree. Reused by #19 (Tag report) with rows filtered by a Tag.
///
/// Amounts without a CHF rate yet are NOT in any total; they are counted in <see cref="PendingConversion"/> so the
/// screen can say "n amounts pending conversion" instead of silently showing a smaller number.
public sealed record CategoryBreakdown(
    decimal Total,
    int PendingConversion,
    decimal Uncategorised,
    decimal Contributions,
    IReadOnlyList<CategoryTotal> Categories)
{
    /// Builds the breakdown of the rows of type <paramref name="type"/>.
    public static CategoryBreakdown Build(IEnumerable<ReportRow> rows, TransactionType type, CategoryTree tree)
    {
        var side = rows.Where(r => r.Type == type).ToList();
        var converted = side.Where(r => r.Chf is not null).Select(r => (r.CategoryId, Amount: Math.Abs(r.Chf!.Value))).ToList();

        var direct = converted.Where(r => r.CategoryId is not null)
            .GroupBy(r => r.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        // Tree order (each Category followed by its subtree); only branches that hold amounts.
        var categories = new List<CategoryTotal>();
        foreach (var category in tree.DepthFirst())
        {
            var subtree = tree.SelfAndDescendants(category.Id);
            if (!subtree.Any(direct.ContainsKey)) continue;
            categories.Add(new CategoryTotal(category.Id, category.Name, category.ParentId, tree.PathOf(category.Id),
                category.BuiltInKey,
                Total: subtree.Sum(id => direct.GetValueOrDefault(id)),
                Direct: direct.GetValueOrDefault(category.Id)));
        }

        var contributionsRoot = tree.FindBuiltIn(BuiltInCategories.Contributions);
        var contributions = contributionsRoot is null
            ? 0m
            : tree.SelfAndDescendants(contributionsRoot.Id).Sum(id => direct.GetValueOrDefault(id));

        return new CategoryBreakdown(
            Total: converted.Sum(r => r.Amount),
            PendingConversion: side.Count(r => r.Chf is null),
            Uncategorised: converted.Where(r => r.CategoryId is null).Sum(r => r.Amount),
            Contributions: contributions,
            Categories: categories);
    }
}

/// A Category's amount: <see cref="Total"/> includes every Category below it; <see cref="Direct"/> is only what is
/// assigned to the Category itself (e.g. "Food" without a subcategory).
public sealed record CategoryTotal(Guid Id, string Name, Guid? ParentId, string Path, string? BuiltInKey,
    decimal Total, decimal Direct);
