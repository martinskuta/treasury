using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Ownership;

public static class Viewer
{
    /// The id of the logged-in Member making the request (every /api endpoint requires login).
    public static Guid MemberId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Endpoint requires a logged-in Member."));

    /// User names of the Members among the given Owners, in one query (for <see cref="OwnerResponse.From"/>).
    public static async Task<Dictionary<Guid, string>> MemberNamesAsync(this TreasuryDbContext db, IEnumerable<Guid?> ownerIds) =>
        (await db.MembersAsync(ownerIds)).ToDictionary(m => m.Key, m => m.Value.Name);

    /// User name and active state of the given Members, in one query.
    public static async Task<Dictionary<Guid, MemberSummary>> MembersAsync(this TreasuryDbContext db, IEnumerable<Guid?> memberIds)
    {
        var ids = memberIds.OfType<Guid>().Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Users.Where(m => ids.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => new MemberSummary(m.UserName!, m.DeactivatedAt == null));
    }
}

public sealed record MemberSummary(string Name, bool Active);
