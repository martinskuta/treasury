using Microsoft.EntityFrameworkCore;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Transactions;

namespace Treasury.Api.Matching;

/// Pairs the two legs of money moving between the Family's own Accounts (spec #1 "Matching"):
/// - a candidate has the opposite sign, sits on a different Account and is dated at most 3 days apart;
/// - same currency: the amounts are equal; across currencies: the CHF values are within 2% and the dates at most
///   1 day apart (no CHF rate yet = no match);
/// - same Owner on both legs = Transfer (no Category); different Owners = Contribution (built-in Contribution
///   Categories, unless a Member set a Category by hand);
/// - a pair is linked only when each leg is the other's ONLY candidate; anything ambiguous stays unmatched.
/// Works on tracked entities of the scoped DbContext; callers save.
public sealed class TransactionMatcher(TreasuryDbContext db, ChfConverter chf)
{
    public const int MaxDaysApart = 3;
    public const int MaxDaysApartAcrossCurrencies = 1;
    public const decimal CrossCurrencyTolerance = 0.02m;

    /// Matches each given (saved, tracked) unlinked Transaction with its single candidate, if it has exactly one.
    public async Task MatchAsync(IReadOnlyCollection<Transaction> transactions, CancellationToken ct = default)
    {
        var open = transactions.Where(t => t.LinkedTransactionId == null).ToList();
        if (open.Count == 0) return;

        // A candidate's own candidates are needed too (ambiguity check), so load twice the window.
        var from = open.Min(t => t.Date).AddDays(-2 * MaxDaysApart);
        var to = open.Max(t => t.Date).AddDays(2 * MaxDaysApart);
        var pool = await db.Transactions.Include(t => t.Account)
            .Where(t => t.LinkedTransactionId == null && t.Date >= from && t.Date <= to)
            .ToListAsync(ct);
        var rates = await chf.LoadAsync(from, to, ct);
        CategoryTree? tree = null;

        foreach (var transaction in open)
        {
            if (transaction.LinkedTransactionId is not null) continue; // linked earlier in this loop
            var candidates = CandidatesFor(transaction, pool, rates);
            if (candidates.Count != 1) continue;
            var other = candidates[0];
            if (CandidatesFor(other, pool, rates).Count != 1) continue;

            var kind = KindOf(transaction, other);
            // A Category set by hand says "this is Income/Expense": don't silently turn it into a Transfer.
            if (kind == LinkKind.Transfer && (HasManualCategory(transaction) || HasManualCategory(other))) continue;

            tree ??= await CategoryTree.LoadAsync(db);
            Link(transaction, other, kind, tree);
        }
    }

    /// Links two Transactions a Member chose. Returns an error message (for the `transactionId` field) when they
    /// can't form a pair. Transfers lose their Category; Contributions get the built-in Categories unless set by hand.
    public async Task<string?> LinkByHandAsync(Transaction a, Transaction b, CancellationToken ct = default)
    {
        if (a.Id == b.Id) return "Choose another Transaction.";
        if (a.AccountId == b.AccountId) return "Both Transactions are in the same Account; choose one from another Account.";
        if (Math.Sign(a.Amount) == Math.Sign(b.Amount)) return "Link money going out of one Account with money coming into another.";
        if (a.LinkedTransactionId is not null || b.LinkedTransactionId is not null)
            return "Unlink the existing pair first.";

        Link(a, b, KindOf(a, b), await CategoryTree.LoadAsync(db));
        return null;
    }

    /// Breaks the link of <paramref name="transaction"/> (no-op when unlinked). Both legs become plain Income or
    /// Expense again; Contribution Categories that matching assigned are removed (back to Uncategorised).
    public async Task UnlinkAsync(Transaction transaction, CancellationToken ct = default)
    {
        if (transaction.LinkedTransactionId is not { } otherId) return;
        var other = await db.Transactions.FindAsync([otherId], ct);
        Clear(transaction);
        if (other is not null) Clear(other);
    }

    /// Breaks every link the Source file's Transactions are part of. Call it before deleting the Source file
    /// (undo, #10) so the remaining legs also lose the Contribution Categories matching gave them. Without the call,
    /// the database still breaks the links (FK set null), but Contribution legs keep their Category.
    public async Task BreakLinksOfSourceFileAsync(Guid sourceFileId, CancellationToken ct = default)
    {
        var linked = await db.Transactions
            .Where(t => t.SourceFileId == sourceFileId && t.LinkedTransactionId != null).ToListAsync(ct);
        foreach (var transaction in linked) await UnlinkAsync(transaction, ct);
    }

    private static List<Transaction> CandidatesFor(Transaction transaction, List<Transaction> pool, ChfRates rates) =>
        pool.Where(c => c.Id != transaction.Id
                && c.LinkedTransactionId is null
                && c.AccountId != transaction.AccountId
                && Math.Sign(c.Amount) == -Math.Sign(transaction.Amount)
                && AmountsAndDatesFit(transaction, c, rates))
            .ToList();

    private static bool AmountsAndDatesFit(Transaction a, Transaction b, ChfRates rates)
    {
        var daysApart = Math.Abs(a.Date.DayNumber - b.Date.DayNumber);
        if (daysApart > MaxDaysApart) return false;
        if (a.Account.Currency == b.Account.Currency) return a.Amount == -b.Amount;
        if (daysApart > MaxDaysApartAcrossCurrencies) return false;
        if (rates.ToChf(a.Amount, a.Account.Currency, a.Date) is not { } chfA
            || rates.ToChf(b.Amount, b.Account.Currency, b.Date) is not { } chfB) return false;
        var larger = Math.Max(Math.Abs(chfA), Math.Abs(chfB));
        return Math.Abs(Math.Abs(chfA) - Math.Abs(chfB)) <= CrossCurrencyTolerance * larger;
    }

    private static LinkKind KindOf(Transaction a, Transaction b) =>
        a.OwnerMemberId == b.OwnerMemberId ? LinkKind.Transfer : LinkKind.Contribution;

    private static bool HasManualCategory(Transaction t) => t.ManuallySet.HasFlag(ManuallySetFields.Category);

    private static void Link(Transaction a, Transaction b, LinkKind kind, CategoryTree tree)
    {
        a.LinkedTransactionId = b.Id;
        b.LinkedTransactionId = a.Id;
        a.LinkKind = b.LinkKind = kind;
        if (kind == LinkKind.Transfer)
        {
            foreach (var leg in new[] { a, b })
            {
                leg.CategoryId = null; // Transfers have no Category
                leg.ManuallySet &= ~ManuallySetFields.Category;
            }
            return;
        }

        var (sender, receiver) = a.Amount < 0 ? (a, b) : (b, a);
        if (!HasManualCategory(sender))
            sender.CategoryId = tree.FindBuiltIn(BuiltInCategories.ForSender(receiver.Owner))!.Id;
        if (!HasManualCategory(receiver))
            receiver.CategoryId = tree.FindBuiltIn(BuiltInCategories.ForReceiver(sender.Owner))!.Id;
    }

    private static void Clear(Transaction leg)
    {
        if (leg.LinkKind == LinkKind.Contribution && !HasManualCategory(leg)) leg.CategoryId = null;
        leg.LinkedTransactionId = null;
        leg.LinkKind = null;
    }
}
