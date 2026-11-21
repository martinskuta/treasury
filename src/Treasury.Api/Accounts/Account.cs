using Treasury.Api.Ownership;

namespace Treasury.Api.Accounts;

/// A place where the Family holds money in one currency (GLOSSARY: Account).
public class Account : IOwned, IImportTarget
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// Name of the Ingest folder (ingest/&lt;slug&gt;/). Fixed at creation; renaming keeps it.
    public required string Slug { get; init; }

    /// Null = the Family. Use <see cref="Owner"/> in code.
    public Guid? OwnerMemberId { get; set; }

    public Owner Owner
    {
        get => new(OwnerMemberId);
        set => OwnerMemberId = value.MemberId;
    }

    /// Key of the Integration that reads this Account's statements.
    public required string IntegrationKey { get; init; }

    /// One of <see cref="Currencies.All"/>.
    public required string Currency { get; init; }

    public decimal OpeningBalance { get; init; }

    /// The date of the opening balance; the balance is the opening balance plus the Transactions after it (spec #1).
    public DateOnly OpeningDate { get; init; }

    /// Optional IBAN / last card digits, only when the Integration can provide one to check against.
    public string? Identifier { get; init; }

    /// The Multi-currency account this Account belongs to, if any (ADR-0009). Fixed: an Account is never moved
    /// into, out of or between groups. Such an Account has no Ingest folder of its own (its Slug is reserved only
    /// for its kept Source files) and takes its Owner from the group.
    public Guid? MultiCurrencyAccountId { get; init; }
}

public static class Currencies
{
    public static readonly IReadOnlyList<string> All = ["CHF", "CZK", "EUR", "USD"];
}
