using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Ownership;

public static class OwnerEndpoints
{
    /// GET /api/owners: every Owner something can be assigned to (the Family first, then each Member by name),
    /// e.g. for a Transaction's Owner picker. Any logged-in Member may list them.
    public static void MapOwners(this RouteGroupBuilder api)
    {
        api.MapGet("/owners", async (TreasuryDbContext db) =>
        {
            // TODO(#5): leave deactivated Members out once deactivation exists.
            var members = await db.Users.OrderBy(m => m.UserName).Select(m => new { m.Id, m.UserName }).ToListAsync();
            return Results.Ok(members
                .Select(m => new OwnerResponse(OwnerKinds.Member, m.Id, m.UserName!))
                .Prepend(new OwnerResponse(OwnerKinds.Family, null, "Family")));
        });
    }
}
