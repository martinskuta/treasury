using Treasury.Api.Accounts;
using Treasury.Api.Ownership;

namespace Treasury.Api.Transactions;

/// A single movement of money in one Account (GLOSSARY: Transaction).
public class Transaction : IOwned
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid AccountId { get; init; }

    public Account Account { get; init; } = null!;

    /// Where the Transaction came from. Imported facts (date, amount, description) are read-only; manual ones aren't.
    public TransactionOrigin Origin { get; init; }

    // ---- Facts: editable only for manual Transactions ----

    public DateOnly Date { get; set; }

    /// In the Account's currency. Positive = money in (Income), negative = money out (Expense).
    public decimal Amount { get; set; }

    /// The original description (as entered, or as the bank wrote it). A later Display name doesn't replace it.
    public required string Description { get; set; }

    // ---- Classification: editable on every Transaction ----

    /// Null = the Family. Defaults to the Account's Owner. Use <see cref="Owner"/> in code.
    public Guid? OwnerMemberId { get; set; }

    public Owner Owner
    {
        get => new(OwnerMemberId);
        set => OwnerMemberId = value.MemberId;
    }

    public string? Note { get; set; }

    /// Classification fields a Member set by hand; Rules (#15) must leave these alone.
    public ManuallySetFields ManuallySet { get; set; }

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
    // Category = 2, Tags = 4, DisplayName = 8 — added by #13/#14/#15 and the Display name ticket.
}
