using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Tags;

/// A shared marker that cuts across Categories (GLOSSARY: Tag). With a date range it is applied automatically to
/// every Transaction dated in that range (optionally only to Transactions of certain Owners).
public class Tag
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// Inclusive date range for automatic application; both set or both null.
    public DateOnly? DateFrom { get; set; }

    public DateOnly? DateTo { get; set; }

    /// Owner limit (only with a date range): when <see cref="IsOwnerLimited"/>, only Transactions owned by the
    /// Family (if <see cref="LimitToFamily"/>) or by one of <see cref="LimitToMemberIds"/> get the Tag automatically.
    public bool LimitToFamily { get; set; }

    public Guid[] LimitToMemberIds { get; set; } = [];

    public bool HasDateRange => DateFrom is not null && DateTo is not null;

    public bool IsOwnerLimited => LimitToFamily || LimitToMemberIds.Length > 0;

    public IEnumerable<Owner> LimitedToOwners =>
        (LimitToFamily ? [Owner.Family] : Array.Empty<Owner>()).Concat(LimitToMemberIds.Select(Owner.Member));

    /// Whether the date range (and Owner limit) covers a Transaction with this date and Owner.
    public bool AppliesAutomaticallyTo(DateOnly date, Owner owner) =>
        HasDateRange && date >= DateFrom && date <= DateTo
        && (!IsOwnerLimited || (owner.IsFamily ? LimitToFamily : LimitToMemberIds.Contains(owner.MemberId!.Value)));
}

/// One reason a Tag is on a Transaction. A Transaction carries a Tag when at least one link exists; the same
/// Tag can be linked by several sources at once (by hand, by its date range, by a Rule), so each source can add
/// and withdraw its own link without clobbering the others.
public class TransactionTag : ITransactionTagPair
{
    public Guid TransactionId { get; init; }

    public Guid TagId { get; init; }

    public TransactionTagSource Source { get; init; }
}

public enum TransactionTagSource
{
    /// Added by a Member.
    Manual = 0,

    /// Applied automatically by the Tag's date range (and Owner limit).
    DateRange = 1,

    /// Added by a Rule (#15).
    Rule = 2,
}

/// A Member removed this Tag from this Transaction by hand: no source may add it again automatically (date range,
/// Rules). Adding the Tag by hand deletes the removal.
public class TransactionTagRemoval : ITransactionTagPair
{
    public Guid TransactionId { get; init; }

    public Guid TagId { get; init; }
}

/// A (Transaction, Tag) row: a link or a removal. Lets <see cref="Tagging"/> load either as pairs with one query helper.
public interface ITransactionTagPair
{
    Guid TransactionId { get; }

    Guid TagId { get; }
}
