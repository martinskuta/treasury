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

    /// Only Transactions that count as Income or Expense, i.e. not Transfers. THE hook for "Transfers never count":
    /// the Uncategorised queue and every report (#18 monthly overview, #19 Tag report) go through it.
    /// TODO(#16): exclude Transfers here once they exist (e.g. `t.TransferId == null`). Today every Transaction is
    /// Income or Expense, so this is the identity filter.
    public static IQueryable<Transaction> CountsAsIncomeOrExpense(this IQueryable<Transaction> query) => query;

    /// Income and Expense without a Category (GLOSSARY: Uncategorised). Compose with <see cref="VisibleTo"/>.
    /// Transfers have no Category but are not Uncategorised; <see cref="CountsAsIncomeOrExpense"/> leaves them out.
    public static IQueryable<Transaction> Uncategorised(this IQueryable<Transaction> query) =>
        query.CountsAsIncomeOrExpense().Where(t => t.CategoryId == null);

    /// Transactions carrying the Tag (from any source). Compose with <see cref="VisibleTo"/>; reused by the
    /// Transactions list filter, bulk edit (#17) and the Tag report (#19).
    public static IQueryable<Transaction> TaggedWith(this IQueryable<Transaction> query, Guid tagId) =>
        query.Where(t => t.TagLinks.Any(l => l.TagId == tagId));
}
