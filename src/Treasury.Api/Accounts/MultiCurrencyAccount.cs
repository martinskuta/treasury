using Treasury.Api.Ownership;

namespace Treasury.Api.Accounts;

/// A group of one Owner's single-currency Accounts that one provider holds together (GLOSSARY: Multi-currency
/// account, ADR-0009). It has the Ingest folder; its Accounts have none of their own. It is not itself an Account.
public class MultiCurrencyAccount : IOwned, IImportTarget
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// Name of the Ingest folder (ingest/&lt;slug&gt;/). Fixed at creation; renaming keeps it. Unique across
    /// Accounts and Multi-currency accounts.
    public required string Slug { get; init; }

    /// Null = the Family. The Owner of every Account in the group too (kept in step, see <see cref="SetOwner"/>).
    public Guid? OwnerMemberId { get; private set; }

    public Owner Owner => new(OwnerMemberId);

    /// Key of an Integration that holds several currencies.
    public required string IntegrationKey { get; init; }

    /// One of <see cref="Currencies.All"/>: the currency the group's total balance is shown in.
    public required string MainCurrency { get; init; }

    public List<Account> Accounts { get; init; } = [];

    /// Sets the Owner of the group and of every Account in it (load <see cref="Accounts"/> first).
    public void SetOwner(Owner owner)
    {
        OwnerMemberId = owner.MemberId;
        foreach (var account in Accounts) account.Owner = owner;
    }
}
