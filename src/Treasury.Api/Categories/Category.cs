namespace Treasury.Api.Categories;

/// A node in the Family's shared Category tree (GLOSSARY: Category).
public class Category
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// Null for a top-level Category.
    public Guid? ParentId { get; set; }

    /// Stable key of a built-in Category (see <see cref="BuiltInCategories"/>); null for the Family's own ones.
    /// Built-in Categories can't be renamed, moved or removed, and get no children.
    public string? BuiltInKey { get; init; }

    public bool IsBuiltIn => BuiltInKey is not null;
}
