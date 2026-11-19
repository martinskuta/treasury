namespace Treasury.Api.Ownership;

/// Who something belongs to (GLOSSARY: Owner): one Member, or the Family as a whole.
/// Stored on entities as a nullable OwnerMemberId column (null = the Family), see <see cref="IOwned"/>.
public readonly record struct Owner(Guid? MemberId)
{
    public static Owner Family => new(null);

    public static Owner Member(Guid memberId) => new(memberId);

    public bool IsFamily => MemberId is null;

    /// The base visibility rule: a Member sees what they own and what the Family owns.
    /// (Transactions add "or the viewer owns its Account" on top, see #6.)
    public bool IsVisibleTo(Guid viewerId) => IsFamily || MemberId == viewerId;

    /// A Member may manage what they own and anything the Family owns, never another Member's.
    public bool IsManageableBy(Guid viewerId) => IsFamily || MemberId == viewerId;
}

/// An entity with an Owner. Implement it with a mapped `Guid? OwnerMemberId` (FK to Members, null = Family)
/// so the shared query helpers in <see cref="OwnerQueries"/> work on it.
public interface IOwned
{
    Guid? OwnerMemberId { get; }
}

public static class OwnerQueries
{
    /// Rows whose Owner is the viewer or the Family (translated to SQL).
    public static IQueryable<T> OwnedByViewerOrFamily<T>(this IQueryable<T> query, Guid viewerId) where T : class, IOwned =>
        query.Where(e => e.OwnerMemberId == null || e.OwnerMemberId == viewerId);
}

/// JSON shape of an Owner in API requests: `{ "kind": "family" }` or `{ "kind": "member", "memberId": "..." }`.
public record OwnerRequest(string? Kind, Guid? MemberId)
{
    /// Null when the request doesn't describe a valid Owner (unknown kind, member without id).
    public Owner? ToOwner() => Kind switch
    {
        OwnerKinds.Family when MemberId is null => Owner.Family,
        OwnerKinds.Member when MemberId is { } id => Owner.Member(id),
        _ => null,
    };
}

/// JSON shape of an Owner in API responses; Name is the Member's user name or "Family".
public record OwnerResponse(string Kind, Guid? MemberId, string Name)
{
    public static OwnerResponse From(Owner owner, IReadOnlyDictionary<Guid, string> memberNames) =>
        owner.MemberId is { } id
            ? new(OwnerKinds.Member, id, memberNames.GetValueOrDefault(id, "?"))
            : new(OwnerKinds.Family, null, "Family");
}

public static class OwnerKinds
{
    public const string Family = "family";
    public const string Member = "member";
}
