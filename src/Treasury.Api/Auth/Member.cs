using Microsoft.AspNetCore.Identity;

namespace Treasury.Api.Auth;

/// An adult of the Family with their own login (see GLOSSARY.md). Logs in with UserName + password; no email.
public class Member : IdentityUser<Guid>
{
    public Member() { }

    public Member(string userName) : base(userName) => Id = Guid.CreateVersion7();
}

public static class Roles
{
    public const string Admin = "Admin";
}
