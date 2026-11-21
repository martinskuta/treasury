using Treasury.Api.Ownership;

namespace Treasury.Api.Categories;

/// The locked built-in Contribution Categories, identified by stable keys (names are for display only).
/// Contribution matching (#16) assigns them: the sender's Expense gets a "To ..." Category, the receiver's
/// Income a "From ..." one, depending on whether the other side is the Family or a Member.
public static class BuiltInCategories
{
    public const string Contributions = "contributions";
    public const string ToFamily = "contributions.to-family";
    public const string FromFamily = "contributions.from-family";
    public const string ToMembers = "contributions.to-members";
    public const string FromMembers = "contributions.from-members";

    /// Every built-in Category in creation order: (key, name, parent key).
    public static readonly IReadOnlyList<(string Key, string Name, string? ParentKey)> All =
    [
        (Contributions, "Contributions", null),
        (ToFamily, "To Family", Contributions),
        (FromFamily, "From Family", Contributions),
        (ToMembers, "To Members", Contributions),
        (FromMembers, "From Members", Contributions),
    ];

    /// The Category key for the sender's Expense of a Contribution to <paramref name="receiver"/>.
    public static string ForSender(Owner receiver) => receiver.IsFamily ? ToFamily : ToMembers;

    /// The Category key for the receiver's Income of a Contribution from <paramref name="sender"/>.
    public static string ForReceiver(Owner sender) => sender.IsFamily ? FromFamily : FromMembers;
}
