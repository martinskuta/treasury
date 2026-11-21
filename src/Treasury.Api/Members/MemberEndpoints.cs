using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Auth;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Members;

public static class MemberEndpoints
{
    public record MemberResponse(Guid Id, string UserName, string FirstName, string? LastName, bool IsAdmin, bool IsActive)
    {
        public static MemberResponse From(Member m, bool isAdmin) =>
            new(m.Id, m.UserName!, m.FirstName, m.LastName, isAdmin, m.IsActive);
    }

    /// /api/admin/members: the Admin manages the Members' logins. Admin role only (403 for other Members).
    /// Nothing here exposes a Member's personal finances.
    public static void MapMembers(this RouteGroupBuilder api)
    {
        var members = api.MapGroup("/admin/members").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        members.MapGet("/", async (UserManager<Member> users) =>
        {
            var admins = (await users.GetUsersInRoleAsync(Roles.Admin)).Select(m => m.Id).ToHashSet();
            var all = await users.Users.OrderBy(m => m.FirstName).ThenBy(m => m.LastName).ThenBy(m => m.UserName).ToListAsync();
            return Results.Ok(all.Select(m => MemberResponse.From(m, admins.Contains(m.Id))));
        });

        members.MapPost("/", async (AddMemberRequest request, UserManager<Member> users) =>
        {
            var userName = request.UserName?.Trim();
            if (string.IsNullOrEmpty(userName))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["Enter a user name."] });
            if (MemberNames.Validate(request.FirstName, request.LastName, out var firstName, out var lastName) is { } nameErrors)
                return Results.ValidationProblem(nameErrors);
            if (string.IsNullOrEmpty(request.Password))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Enter an initial password."] });

            var member = new Member(userName, firstName, lastName);
            var created = await users.CreateAsync(member, request.Password);
            if (!created.Succeeded) return IdentityProblem(created, "password");

            return Results.Created($"/api/admin/members/{member.Id}", MemberResponse.From(member, false));
        });

        // Any Member's name, deactivated ones included (their name still labels their data).
        members.MapPut("/{id:guid}/name", async (Guid id, NameRequest request, UserManager<Member> users) =>
        {
            var member = await users.FindByIdAsync(id.ToString());
            if (member is null) return Results.NotFound();
            if (MemberNames.Validate(request.FirstName, request.LastName, out var firstName, out var lastName) is { } errors)
                return Results.ValidationProblem(errors);

            member.FirstName = firstName;
            member.LastName = lastName;
            await users.UpdateAsync(member);
            return Results.Ok(MemberResponse.From(member, await users.IsInRoleAsync(member, Roles.Admin)));
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
            return Results.Ok(MemberResponse.From(member, isAdmin));
        });

        // The handover lookup (ADR 0007): only what the Admin needs to choose a new Owner for each Account while
        // deactivating an active Member — id, name and currency; no balances, identifiers or Transactions (the Admin
        // role gives no view into personal finances). Deactivated (or unknown) Members: 404.
        members.MapGet("/{id:guid}/accounts", async (Guid id, UserManager<Member> users, TreasuryDbContext db) =>
        {
            if (await users.FindByIdAsync(id.ToString()) is not { IsActive: true }) return Results.NotFound();
            // A Multi-currency account is handed over as a whole (ADR-0009), so it is listed instead of its Accounts.
            var accounts = await db.Accounts.Where(a => a.OwnerMemberId == id && a.MultiCurrencyAccountId == null)
                .Select(a => new HandoverAccountResponse(a.Id, a.Name, a.Currency, false)).ToListAsync();
            var multiCurrencyAccounts = await db.MultiCurrencyAccounts.Where(m => m.OwnerMemberId == id)
                .Select(m => new HandoverAccountResponse(m.Id, m.Name, m.MainCurrency, true)).ToListAsync();
            return Results.Ok(accounts.Concat(multiCurrencyAccounts).OrderBy(a => a.Name, StringComparer.CurrentCulture));
        });

        // Deactivate, never delete: the Member's data stays, their login and open sessions stop working.
        // Each listed Account is handed to its new Owner; Accounts not listed stay with the deactivated Member.
        members.MapPost("/{id:guid}/deactivate", async (Guid id, DeactivateRequest request, ClaimsPrincipal user,
            UserManager<Member> users, TreasuryDbContext db, TimeProvider clock) =>
        {
            var member = await users.FindByIdAsync(id.ToString());
            if (member is null) return Results.NotFound();
            if (users.GetUserId(user) == id.ToString())
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["member"] = ["You can't deactivate yourself."] });
            if (!member.IsActive) return Results.NoContent();

            var handover = request.Handover ?? [];
            var accounts = await db.Accounts.Where(a => a.OwnerMemberId == id).ToDictionaryAsync(a => a.Id);
            var multiCurrencyAccounts = await db.MultiCurrencyAccounts.Include(m => m.Accounts)
                .Where(m => m.OwnerMemberId == id).ToDictionaryAsync(m => m.Id);
            var newOwnerIds = handover.Select(h => h.Owner?.ToOwner()?.MemberId).OfType<Guid>().Distinct().ToList();
            var activeTargets = await db.Users.Where(m => newOwnerIds.Contains(m.Id) && m.Id != id && m.DeactivatedAt == null)
                .Select(m => m.Id).ToListAsync();

            // What each item hands over: a plain Account, or a Multi-currency account with all its Accounts.
            var errors = new List<string>();
            var moves = new List<(string Name, Action<Owner> Apply, OwnerRequest? Owner)>();
            foreach (var item in handover)
            {
                if ((item.AccountId is null) == (item.MultiCurrencyAccountId is null))
                    errors.Add("Name either an Account or a Multi-currency account for each handover.");
                else if (item.MultiCurrencyAccountId is { } multiCurrencyAccountId)
                {
                    if (multiCurrencyAccounts.TryGetValue(multiCurrencyAccountId, out var multiCurrencyAccount))
                        moves.Add((multiCurrencyAccount.Name, multiCurrencyAccount.SetOwner, item.Owner));
                    else errors.Add("Only the Member's own Accounts can be handed over.");
                }
                else if (!accounts.TryGetValue(item.AccountId!.Value, out var account))
                    errors.Add("Only the Member's own Accounts can be handed over.");
                else if (account.MultiCurrencyAccountId is { } itsId
                         && multiCurrencyAccounts.TryGetValue(itsId, out var its))
                    errors.Add($"\"{account.Name}\" belongs to \"{its.Name}\"; hand over \"{its.Name}\" as a whole.");
                else moves.Add((account.Name, owner => account.Owner = owner, item.Owner));
            }
            if (handover.Select(h => h.AccountId ?? h.MultiCurrencyAccountId).Distinct().Count() != handover.Count)
                errors.Add("Each Account can be handed over only once.");
            foreach (var move in moves)
                if (move.Owner?.ToOwner() is not { } owner || owner.MemberId is { } to && !activeTargets.Contains(to))
                    errors.Add($"Hand \"{move.Name}\" to the Family or another active Member.");
            if (errors.Count > 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["handover"] = [.. errors.Distinct()] });

            foreach (var move in moves) move.Apply(move.Owner!.ToOwner()!.Value);
            member.DeactivatedAt = clock.GetUtcNow();
            // UserManager's store shares this request's DbContext, so this one save stores the handover, the
            // deactivation and the new security stamp (which ends the Member's sessions) together.
            await users.UpdateSecurityStampAsync(member);
            return Results.NoContent();
        });
    }

    public record AddMemberRequest(string? UserName, string? Password, string? FirstName = null, string? LastName = null);

    public record NameRequest(string? FirstName, string? LastName);

    public record ResetPasswordRequest(string? NewPassword);

    public record SetAdminRequest(bool? IsAdmin);

    /// One plain Account, or one Multi-currency account (Currency = its main currency), to choose a new Owner for.
    public record HandoverAccountResponse(Guid Id, string Name, string Currency, bool IsMultiCurrencyAccount);

    public record DeactivateRequest(IReadOnlyList<HandoverRequest>? Handover);

    /// Give one of the deactivated Member's plain Accounts (AccountId), or one of their Multi-currency accounts with
    /// all its Accounts (MultiCurrencyAccountId), to a new Owner (the Family or another active Member).
    public record HandoverRequest(Guid? AccountId, OwnerRequest? Owner, Guid? MultiCurrencyAccountId = null);

    /// Identity errors as a ValidationProblem: user-name errors under "userName", the rest under passwordField.
    private static IResult IdentityProblem(IdentityResult result, string passwordField) =>
        Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code.Contains("UserName") ? "userName" : passwordField)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
