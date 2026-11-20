using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Auth;
using Treasury.Api.Ownership;

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
            return Results.Ok(all.Select(m => new MemberResponse(m.Id, m.UserName!, admins.Contains(m.Id), m.IsActive)));
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

        // A new password also changes the security stamp, which ends the Member's existing sessions.
        members.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest request, UserManager<Member> users) =>
        {
            var member = await users.FindByIdAsync(id.ToString());
            if (member is null) return Results.NotFound();
            if (string.IsNullOrEmpty(request.NewPassword))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["Enter a new password."] });

            var token = await users.GeneratePasswordResetTokenAsync(member);
            var result = await users.ResetPasswordAsync(member, token, request.NewPassword);
            return result.Succeeded ? Results.NoContent() : IdentityProblem(result, "newPassword");
        });

        // Open sessions pick the change up on their next request (the cookie principal is rebuilt on every
        // security-stamp check, see AuthSetup).
        members.MapPut("/{id:guid}/admin", async (Guid id, SetAdminRequest request, UserManager<Member> users) =>
        {
            var member = await users.FindByIdAsync(id.ToString());
            if (member is null) return Results.NotFound();
            if (request.IsAdmin is not { } isAdmin)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["isAdmin"] = ["Say whether the Member is an Admin."] });

            var admins = await users.GetUsersInRoleAsync(Roles.Admin);
            var wasAdmin = admins.Any(a => a.Id == id);
            // Deactivated Admins can't log in, so only active ones count.
            if (wasAdmin && !isAdmin && admins.Count(a => a.IsActive && a.Id != id) == 0)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["isAdmin"] = ["The last Admin can't lose the Admin role. Make another Member an Admin first."] });

            if (isAdmin && !wasAdmin) await users.AddToRoleAsync(member, Roles.Admin);
            if (!isAdmin && wasAdmin) await users.RemoveFromRoleAsync(member, Roles.Admin);
            return Results.Ok(new MemberResponse(member.Id, member.UserName!, isAdmin, member.IsActive));
        });

        // Deactivate, never delete: the Member's data stays, their login and open sessions stop working.
        members.MapPost("/{id:guid}/deactivate", async (Guid id, DeactivateRequest request, ClaimsPrincipal user,
            UserManager<Member> users, TimeProvider clock) =>
        {
            var member = await users.FindByIdAsync(id.ToString());
            if (member is null) return Results.NotFound();
            if (users.GetUserId(user) == id.ToString())
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["member"] = ["You can't deactivate yourself."] });
            if (!member.IsActive) return Results.NoContent();

            member.DeactivatedAt = clock.GetUtcNow();
            await users.UpdateAsync(member);
            await users.UpdateSecurityStampAsync(member);
            return Results.NoContent();
        });
    }

    public record AddMemberRequest(string? UserName, string? Password);

    public record ResetPasswordRequest(string? NewPassword);

    public record SetAdminRequest(bool? IsAdmin);

    public record DeactivateRequest(IReadOnlyList<HandoverRequest>? Handover);

    /// Give one of the deactivated Member's Accounts to a new Owner (the Family or another active Member).
    public record HandoverRequest(Guid AccountId, OwnerRequest? Owner);

    /// Identity errors as a ValidationProblem: user-name errors under "userName", the rest under passwordField.
    private static IResult IdentityProblem(IdentityResult result, string passwordField) =>
        Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.Contains("UserName") ? "userName" : passwordField)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
