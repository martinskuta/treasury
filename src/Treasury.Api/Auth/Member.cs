using Microsoft.AspNetCore.Identity;

namespace Treasury.Api.Auth;

/// An adult of the Family with their own login (see GLOSSARY.md). Logs in with UserName + password; no email.
/// Members are never deleted: an Admin deactivates them, which keeps their data but stops their login.
public class Member : IdentityUser<Guid>
{
    public Member() { }

    public Member(string userName, string firstName, string? lastName = null) : base(userName)
    {
        Id = Guid.CreateVersion7();
        FirstName = firstName;
        LastName = lastName;
    }

    public const int MaxNameLength = 100;

    /// Required; what the dashboard greets the Member with.
    public string FirstName { get; set; } = "";

    /// Optional; null when the Member has none.
    public string? LastName { get; set; }

    /// How Treasury names a Member wherever Members are listed (owner pickers, Transactions, reports):
    /// first name, last-name initial and username, e.g. "Martin Š. (martin)" or "Martin (martin)".
    public static string Label(string firstName, string? lastName, string userName) =>
        string.IsNullOrEmpty(lastName)
            ? $"{firstName} ({userName})"
            : $"{firstName} {char.ConvertFromUtf32(char.ConvertToUtf32(lastName, 0))}. ({userName})";

    /// When an Admin deactivated this Member; null = active. A deactivated Member can't log in and their
    /// sessions end (see <see cref="TreasurySignInManager"/>).
    public DateTimeOffset? DeactivatedAt { get; set; }

    public bool IsActive => DeactivatedAt is null;
}

public static class Roles
{
    public const string Admin = "Admin";
}
