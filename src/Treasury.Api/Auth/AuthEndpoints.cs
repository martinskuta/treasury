using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace Treasury.Api.Auth;

public static class AuthEndpoints
{
    public record LoginRequest(string UserName, string Password);

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public record MeResponse(Guid Id, string UserName, IReadOnlyList<string> Roles);

    /// /api/auth/*: login, logout, the current Member, changing one's own password.
    public static void MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");

        auth.MapPost("/login", async (LoginRequest request, SignInManager<Member> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(request.UserName, request.Password,
                isPersistent: true, lockoutOnFailure: true);
            if (!result.Succeeded) return Results.Unauthorized();

            var member = await signIn.UserManager.FindByNameAsync(request.UserName);
            return Results.Ok(await ToMeAsync(member!, signIn.UserManager));
        }).AllowAnonymous();

        auth.MapPost("/logout", async (SignInManager<Member> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        auth.MapPost("/password", async (ChangePasswordRequest request, ClaimsPrincipal user,
            SignInManager<Member> signIn) =>
        {
            var member = await signIn.UserManager.GetUserAsync(user);
            if (member is null) return Results.Unauthorized();

            var result = await signIn.UserManager.ChangePasswordAsync(member, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
                return Results.ValidationProblem(result.Errors
                    .GroupBy(e => e.Code.Contains("Mismatch") ? "currentPassword" : "newPassword")
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));

            await signIn.RefreshSignInAsync(member); // new security stamp: keep this session valid
            return Results.NoContent();
        });

        auth.MapGet("/me", async (ClaimsPrincipal user, UserManager<Member> members) =>
        {
            var member = await members.GetUserAsync(user);
            return member is null ? Results.Unauthorized() : Results.Ok(await ToMeAsync(member, members));
        });
    }

    private static async Task<MeResponse> ToMeAsync(Member member, UserManager<Member> members) =>
        new(member.Id, member.UserName!, [.. await members.GetRolesAsync(member)]);
}
