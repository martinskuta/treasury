namespace Treasury.Api.Transactions;

public static class TransactionQueries
{
    /// The Transaction visibility rule (spec #1), translated to SQL. Every endpoint and report that reads
    /// Transactions starts from this filter. A Member sees a Transaction when
    /// - they own it, or the Family owns it, or
    /// - they can see its Account (they own it, or the Family owns it): an Account's full contents are visible to
    ///   whoever can see the Account, so its balance never includes Transactions its viewers can't see.
    /// A Transaction reassigned to Bob from Alice's personal Account is therefore visible to Bob (Owner) and Alice
    /// (Account Owner), and to nobody else unless it is Family-owned.
    public static IQueryable<Transaction> VisibleTo(this IQueryable<Transaction> query, Guid viewerId) =>
        query.Where(t => t.OwnerMemberId == null || t.OwnerMemberId == viewerId
            || t.Account.OwnerMemberId == null || t.Account.OwnerMemberId == viewerId);

    /// Legs of a Transfer (SQL form of <see cref="Transaction.IsTransfer"/>).
    public static IQueryable<Transaction> Transfers(this IQueryable<Transaction> query) =>
        query.Where(t => t.LinkedTransactionId != null && t.LinkKind == LinkKind.Transfer);

    /// Income and Expense only: Transfers never count in totals, reports or the Uncategorised queue.
    public static IQueryable<Transaction> CountsAsIncomeOrExpense(this IQueryable<Transaction> query) =>
        query.Where(t => t.LinkedTransactionId == null || t.LinkKind != LinkKind.Transfer);

    /// Income and Expense without a Category (GLOSSARY: Uncategorised). Compose with <see cref="VisibleTo"/>.
    /// Transfers have no Category but are not Uncategorised.
    public static IQueryable<Transaction> Uncategorised(this IQueryable<Transaction> query) =>
        query.CountsAsIncomeOrExpense().Where(t => t.CategoryId == null);
}
