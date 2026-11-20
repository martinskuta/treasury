using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Categories;

/// The whole Category tree in memory (it is small): paths, children and subtrees.
/// Reports that roll up through the tree (#18) use <see cref="SelfAndDescendants"/>.
public sealed class CategoryTree
{
    public const string PathSeparator = " › ";

    private readonly Dictionary<Guid, Category> _byId;
    private readonly ILookup<Guid?, Category> _children;

    public CategoryTree(IEnumerable<Category> categories)
    {
        _byId = categories.ToDictionary(c => c.Id);
        _children = _byId.Values.ToLookup(c => c.ParentId);
    }

    public static async Task<CategoryTree> LoadAsync(TreasuryDbContext db) =>
        new(await db.Categories.AsNoTracking().ToListAsync());

    public Category? Find(Guid id) => _byId.GetValueOrDefault(id);

    public Category? FindBuiltIn(string key) => _byId.Values.SingleOrDefault(c => c.BuiltInKey == key);

    /// Direct children of a Category (or the top-level Categories for null), ordered by name.
    public IEnumerable<Category> ChildrenOf(Guid? parentId) =>
        _children[parentId].OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase);

    /// The Category and every Category below it, at any depth.
    public IReadOnlyList<Guid> SelfAndDescendants(Guid id)
    {
        var result = new List<Guid>();
        var pending = new Stack<Guid>([id]);
        while (pending.TryPop(out var next))
        {
            if (!_byId.ContainsKey(next)) continue;
            result.Add(next);
            foreach (var child in _children[next]) pending.Push(child.Id);
        }
        return result;
    }

    /// Whether <paramref name="id"/> is <paramref name="ancestorId"/> or lies below it.
    public bool IsSelfOrDescendant(Guid id, Guid ancestorId)
    {
        for (Guid? current = id; current is { } c; current = Find(c)?.ParentId)
            if (c == ancestorId) return true;
        return false;
    }

    /// "Food › Eating out".
    public string PathOf(Guid id)
    {
        var names = new List<string>();
        for (var current = Find(id); current is not null; current = current.ParentId is { } p ? Find(p) : null)
            names.Add(current.Name);
        names.Reverse();
        return string.Join(PathSeparator, names);
    }

    /// Depth-first in name order: each Category directly followed by its subtree (how the tree is shown).
    public IEnumerable<Category> DepthFirst(Guid? parentId = null)
    {
        foreach (var child in ChildrenOf(parentId))
        {
            yield return child;
            foreach (var descendant in DepthFirst(child.Id)) yield return descendant;
        }
    }
}
