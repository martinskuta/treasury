using Microsoft.AspNetCore.Identity;

namespace Treasury.Api.Auth;

/// An adult of the Family with their own login (see GLOSSARY.md). Logs in with UserName + password; no email.
/// Members are never deleted: an Admin deactivates them, which keeps their data but stops their login.
public class Member : IdentityUser<Guid>
{
    public Member() { }

    public Member(string userName) : base(userName) => Id = Guid.CreateVersion7();

    /// When an Admin deactivated this Member; null = active. A deactivated Member can't log in and their
    /// sessions end (see <see cref="TreasurySignInManager"/>).
    public DateTimeOffset? DeactivatedAt { get; set; }

    public bool IsActive => DeactivatedAt is null;
}

public static class Roles
{
    public const string Admin = "Admin";
}
