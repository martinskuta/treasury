using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Auth;

namespace Treasury.Api.Members;

public static class MemberEndpoints
{
    public record MemberResponse(Guid Id, string UserName, bool IsAdmin, bool IsActive);

    /// /api/admin/members: the Admin manages the Members' logins. Admin role only (403 for other Members).
    /// Nothing here exposes a Member's personal finances.
    public static void MapMembers(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/admin/members").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        members.MapGet("/", async (UserManager<Member> users) =>
        {
            var admins = (await users.GetUsersInRoleAsync(Roles.Admin)).Select(m => m.Id).ToHashSet();
            var all = await users.Users.OrderBy(m => m.UserName).ToListAsync();
            return Results.Ok(all.Select(m => new MemberResponse(m.Id, m.UserName!, admins.Contains(m.Id), IsActive: true)));
        });

        members.MapPost("/", async (AddMemberRequest request, UserManager<Member> users) =>
        {
            var userName = request.UserName?.Trim();
            if (string.IsNullOrEmpty(userName))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["Enter a user name."] });
            if (string.IsNullOrEmpty(request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Enter an initial password."] });

            var member = new Member(userName);
            var created = await users.CreateAsync(member, request.Password);
            if (!created.Succeeded) return IdentityProblem(created, "password");

            return Results.Created($"/api/admin/members/{member.Id}", new MemberResponse(member.Id, userName, false, true));
        });
    }

    public record AddMemberRequest(string? UserName, string? Password);

    /// Identity errors as a ValidationProblem: user-name errors under "userName", the rest under passwordField.
    private static IResult IdentityProblem(IdentityResult result, string passwordField) =>
        Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.Contains("UserName") ? "userName" : passwordField)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
