using Treasury.Api.Accounts;
using Treasury.Api.Imports;
using Treasury.Api.Ownership;
using Treasury.Api.Tags;

namespace Treasury.Api.Transactions;

/// A single movement of money in one Account (GLOSSARY: Transaction).
public class Transaction : IOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid AccountId { get; init; }

    public Account Account { get; init; } = null!;

    /// Where the Transaction came from. Imported facts (date, amount, description, original amount) are read-only;
    /// manual ones aren't.
    public TransactionOrigin Origin { get; init; }

    /// The Source file an imported Transaction came from (null for manual ones). Deleting it deletes the Transaction.
    public Guid? SourceFileId { get; init; }

    public SourceFile? SourceFile { get; init; }

    // ---- Facts: editable only for manual Transactions ----

    public DateOnly Date { get; set; }

    /// In the Account's currency. Positive = money in (Income), negative = money out (Expense).
    public decimal Amount { get; set; }

    /// The original description (as entered, or as the bank wrote it). A later Display name doesn't replace it.
    public required string Description { get; set; }

    /// The other party as the bank names it (imported only).
    public string? Counterparty { get; init; }

    /// The bank's own reference for the movement, when the format has one (imported only; duplicate key, #9).
    public string? Reference { get; init; }

    /// For a foreign-currency purchase: what was paid in the original currency (same sign as Amount).
    public decimal? OriginalAmount { get; init; }

    /// ISO code of <see cref="OriginalAmount"/> (any currency, not just the Account currencies).
    public string? OriginalCurrency { get; init; }

    // ---- Classification: editable on every Transaction ----

    /// Null = the Family. Defaults to the Account's Owner. Use <see cref="Owner"/> in code.
    public Guid? OwnerMemberId { get; set; }

    public Owner Owner
    {
        get => new(OwnerMemberId);
        set => OwnerMemberId = value.MemberId;
    }

    /// Friendly name shown instead of the original description, which stays unchanged (GLOSSARY: Display name).
    public string? DisplayName { get; set; }

    public string? Note { get; set; }

    /// Null = Uncategorised (for Income and Expense; Transfers never have a Category, #16).
    public Guid? CategoryId { get; set; }

    /// Classification fields a Member set by hand; Rules (#15) must leave these alone.
    public ManuallySetFields ManuallySet { get; set; }

    /// Why each Tag is on this Transaction (one link per Tag and source; see <see cref="TransactionTag"/>).
    public List<TransactionTag> TagLinks { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    /// Income or Expense from the sign (Transfers come with matching, #8).
    public TransactionType Type => Amount >= 0 ? TransactionType.Income : TransactionType.Expense;

    public bool FactsEditable => Origin == TransactionOrigin.Manual;
}

public enum TransactionOrigin
{
    Manual = 0,
    Imported = 1,
}

public enum TransactionType
{
    Income,
    Expense,
}

/// Which classification fields were set manually (stored as a bit mask, so new fields need no new column).
[Flags]
public enum ManuallySetFields
{
    None = 0,
    Owner = 1,
    Category = 2,
    /// A Member added or removed a Tag by hand (per-Tag stickiness lives in TransactionTagRemovals).
    Tags = 4,
    DisplayName = 8,
}
