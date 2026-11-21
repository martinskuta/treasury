using Microsoft.EntityFrameworkCore;
using Treasury.Api.Auth;
using Treasury.Api.Data;

namespace Treasury.Api.Ownership;

public static class OwnerEndpoints
{
    /// GET /api/owners: every Owner something can be assigned to (the Family first, then each active Member by name),
    /// e.g. for a Transaction's Owner picker. Any logged-in Member may list them.
    public static void MapOwners(this RouteGroupBuilder api)
    {
        api.MapGet("/owners", async (TreasuryDbContext db) =>
        {
            // Deactivated Members can't be given anything new (their existing data keeps their name).
            var members = await db.Users.Where(m => m.DeactivatedAt == null)
                .OrderBy(m => m.FirstName).ThenBy(m => m.LastName).ThenBy(m => m.UserName)
                .Select(m => new { m.Id, m.UserName, m.FirstName, m.LastName }).ToListAsync();
            return Results.Ok(members
                .Select(m => new OwnerResponse(OwnerKinds.Member, m.Id, Member.Label(m.FirstName, m.LastName, m.UserName!)))
                .Prepend(new OwnerResponse(OwnerKinds.Family, null, "Family")));
        });
    }
}
