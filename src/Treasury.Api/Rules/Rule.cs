using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Rules;

/// A shared condition on Transactions with the Category, Tags and/or Owner it assigns (GLOSSARY: Rule).
/// Rules run in <see cref="Priority"/> order: the first matching Rule with a Category sets the Category, the first
/// matching Rule with an Owner sets the Owner, and the Tags of every matching Rule are added together.
public class Rule
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// Lower runs first. Kept dense (0, 1, 2, ...) by the endpoints.
    public int Priority { get; set; }

    public RuleField Field { get; set; }

    public RuleMatch Match { get; set; }

    /// Compared ignoring case, after trimming both sides.
    public required string Text { get; set; }

    /// The Category to assign (null = this Rule doesn't set one). Removing the Category clears it.
    public Guid? CategoryId { get; set; }

    /// Whether the Rule sets an Owner; <see cref="OwnerMemberId"/> null then means the Family.
    public bool SetsOwner { get; set; }

    public Guid? OwnerMemberId { get; set; }

    public Owner? Owner => SetsOwner ? new Owner(OwnerMemberId) : null;

    /// The Member who last created or edited the Rule. The Rule only reaches Transactions in Accounts this Member can
    /// see (evaluated when it runs), and stops running while this Member is deactivated.
    public Guid AuthorMemberId { get; set; }

    public List<RuleTag> TagLinks { get; init; } = [];

    /// Whether the condition holds for the Transaction's original description or counterparty.
    public bool Matches(Transaction transaction)
    {
        var value = Field == RuleField.Description ? transaction.Description : transaction.Counterparty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        value = value.Trim();
        return Match == RuleMatch.Equals
            ? string.Equals(value, Text, StringComparison.OrdinalIgnoreCase)
            : value.Contains(Text, StringComparison.OrdinalIgnoreCase);
    }
}

public enum RuleField
{
    Description = 0,
    Counterparty = 1,
}

public enum RuleMatch
{
    Contains = 0,
    Equals = 1,
}

/// A Tag a Rule adds.
public class RuleTag
{
    public Guid RuleId { get; init; }

    public Guid TagId { get; init; }
}
