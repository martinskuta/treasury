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
/// - same Owner on both legs = Transfer (no Category); different Owners = a Reimbursement (no Category) when the
///   Family pays a Member, otherwise a Contribution (built-in Contribution Categories, unless a Member set a
///   Category by hand). A Member can switch a pair between the two (<see cref="SwitchKindAsync"/>, ADR 0011);
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
            // A Category set by hand says "this is Income/Expense": don't silently turn it into a Transfer or a
            // Reimbursement, which have none.
            if (kind != LinkKind.Contribution && (HasManualCategory(transaction) || HasManualCategory(other))) continue;

            tree ??= await CategoryTree.LoadAsync(db);
            Link(transaction, other, kind, tree);
        }
    }

    /// Links two Transactions a Member chose. Returns an error message (for the `transactionId` field) when they
    /// can't form a pair. Transfers and Reimbursements lose their Category; Contributions get the built-in Categories
    /// unless set by hand.
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

    /// Brings the kind of each linked pair among <paramref name="reowned"/> (Transactions whose Owner just changed: by
    /// hand, by bulk edit or by a Rule) in line with the Owners: same Owner on both legs = Transfer (no Category),
    /// different Owners = Contribution or Reimbursement. A kind a Member chose (<see cref="SwitchKindAsync"/>) is
    /// kept while the Owners differ; otherwise the default applies (<see cref="KindOf"/>). Contribution Categories
    /// follow the other leg's new Owner, unless a Member set the leg's Category by hand. Unlinked Transactions are
    /// left alone.
    public async Task RefreshLinksAsync(IReadOnlyCollection<Transaction> reowned, CancellationToken ct = default)
    {
        var linked = reowned.Where(t => t.LinkedTransactionId is not null).ToList();
        if (linked.Count == 0) return;
        var tree = await CategoryTree.LoadAsync(db);
        foreach (var transaction in linked)
        {
            var other = await db.Transactions.FindAsync([transaction.LinkedTransactionId!.Value], ct);
            if (other is null) continue;
            var kind = KindOf(transaction, other);
            if (kind == LinkKind.Transfer) ForgetChosenKind(transaction, other);
            else if (HasChosenKind(transaction) && transaction.LinkKind is { } chosen and not LinkKind.Transfer) kind = chosen;
            Link(transaction, other, kind, tree);
        }
    }

    /// Makes the pair <paramref name="transaction"/> belongs to a Contribution or a Reimbursement, as a Member chose.
    /// Returns an error message (for the `kind` field) when the pair can't be switched. A Contribution gets the
    /// built-in Categories (unless set by hand); a Reimbursement loses its Categories.
    public async Task<string?> SwitchKindAsync(Transaction transaction, LinkKind kind, CancellationToken ct = default)
    {
        if (kind == LinkKind.Transfer) return "Choose Contribution or Reimbursement.";
        var other = transaction.LinkedTransactionId is { } otherId ? await db.Transactions.FindAsync([otherId], ct) : null;
        if (other is null) return "This Transaction isn't linked to another one.";
        if (transaction.OwnerMemberId == other.OwnerMemberId)
            return "Both sides have the same Owner, so this is a Transfer. Change the Owner of one side first.";

        Link(transaction, other, kind, await CategoryTree.LoadAsync(db));
        transaction.ManuallySet |= ManuallySetFields.LinkKind;
        other.ManuallySet |= ManuallySetFields.LinkKind;
        return null;
    }

    /// Breaks the link of <paramref name="transaction"/> (no-op when unlinked). Both legs become plain Income or
    /// Expense again; Contribution Categories that matching assigned are removed (back to Uncategorised), and a kind
    /// a Member chose is forgotten.
    /// Returns the other leg (tracked), or null when there was none.
    public async Task<Transaction?> UnlinkAsync(Transaction transaction, CancellationToken ct = default)
    {
        if (transaction.LinkedTransactionId is not { } otherId) return null;
        var other = await db.Transactions.FindAsync([otherId], ct);
        Clear(transaction);
        if (other is not null) Clear(other);
        return other;
    }

    /// Breaks every link the Source file's Transactions are part of. Call it before deleting the Source file
    /// (undo, #10) so the remaining legs also lose the Contribution Categories matching gave them. Without the call,
    /// the database still breaks the links (FK set null), but Contribution legs keep their Category.
    /// Returns the ids of the former legs outside the Source file (they stay; see <see cref="FormerLegs"/>).
    public async Task<List<Guid>> BreakLinksOfSourceFileAsync(Guid sourceFileId, CancellationToken ct = default)
    {
        var linked = await db.Transactions
            .Where(t => t.SourceFileId == sourceFileId && t.LinkedTransactionId != null).ToListAsync(ct);
        var survivors = new List<Guid>();
        foreach (var transaction in linked)
            if (await UnlinkAsync(transaction, ct) is { } other && other.SourceFileId != sourceFileId) survivors.Add(other.Id);
        return survivors;
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

    /// THE default kind of a pair (ADR 0011): same Owner = Transfer; the Family paying a Member = Reimbursement
    /// (the Family pays back what the Member spent for it); anything else between different Owners = Contribution
    /// (a Member's share paid into a Family Account, money between two Members).
    private static LinkKind KindOf(Transaction a, Transaction b)
    {
        if (a.OwnerMemberId == b.OwnerMemberId) return LinkKind.Transfer;
        var (sender, receiver) = a.Amount < 0 ? (a, b) : (b, a);
        return sender.Owner.IsFamily && !receiver.Owner.IsFamily ? LinkKind.Reimbursement : LinkKind.Contribution;
    }

    private static bool HasManualCategory(Transaction t) => t.ManuallySet.HasFlag(ManuallySetFields.Category);

    private static bool HasChosenKind(Transaction t) => t.ManuallySet.HasFlag(ManuallySetFields.LinkKind);

    private static void ForgetChosenKind(params Transaction[] legs)
    {
        foreach (var leg in legs) leg.ManuallySet &= ~ManuallySetFields.LinkKind;
    }

    private static void Link(Transaction a, Transaction b, LinkKind kind, CategoryTree tree)
    {
        a.LinkedTransactionId = b.Id;
        b.LinkedTransactionId = a.Id;
        a.LinkKind = b.LinkKind = kind;
        if (kind != LinkKind.Contribution)
        {
            foreach (var leg in new[] { a, b })
            {
                leg.CategoryId = null; // Transfers and Reimbursements have no Category
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
        ForgetChosenKind(leg);
    }
}
