using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Settings;
using Treasury.Api.Transactions;

namespace Treasury.Api.AmountsOwed;

/// Whether a Transaction made on another Owner's Account has been paid back (GLOSSARY: Amount owed).
public enum PaidBackStatus
{
    PaidBack,
    PartlyPaidBack,
    Outstanding,
}

/// What <paramref name="Debtor"/> owes <paramref name="Creditor"/> in CHF (0 = nothing either way).
/// <param name="PendingConversion">Transactions between the two that still wait for a CHF rate and are not in Amount.</param>
public sealed record AmountOwed(Owner Debtor, Owner Creditor, decimal Amount, int PendingConversion)
{
    public bool Involves(Owner owner) => Debtor == owner || Creditor == owner;
}

/// How far a Transaction is paid back, and the CHF amount still open.
public sealed record PaidBack(PaidBackStatus Status, decimal Outstanding);

/// THE Amount owed calculation (GLOSSARY, ADR 0011), computed on read for every pair of Owners at once:
/// - a Transaction whose Owner differs from its Account's Owner moves the amount by its CHF value: an Expense
///   raises what the Transaction's Owner owes the Account's Owner, an Income raises the reverse;
/// - a Reimbursement raises what its receiver owes its payer by the CHF value of the paying leg, i.e. it pays down
///   what the payer owed;
/// - within a pair, each side's items are covered oldest first by the total of the other side's items, which gives
///   every Transaction its <see cref="PaidBack"/> status; what is left over on one side is the Amount owed;
/// - only Transactions dated on or after the Family's start date count; ones without a CHF rate yet are pending.
/// It covers everything in the database: callers apply visibility (<see cref="VisibleTo"/>, or by asking only about
/// Transactions the viewer can see).
public sealed class AmountOwedLedger
{
    private readonly Dictionary<Guid, PaidBack> _paidBack = [];

    public IReadOnlyList<AmountOwed> Amounts { get; }

    private AmountOwedLedger(IReadOnlyList<AmountOwed> amounts) => Amounts = amounts;

    /// The status of a Transaction made on another Owner's Account; null for any other Transaction, one dated
    /// before the start date, or one without a CHF rate yet.
    public PaidBack? PaidBackOf(Guid transactionId) => _paidBack.GetValueOrDefault(transactionId);

    /// Transactions that are outstanding or only partly paid back.
    public IReadOnlyList<Guid> NotPaidBackIds() =>
        _paidBack.Where(p => p.Value.Status != PaidBackStatus.PaidBack).Select(p => p.Key).ToList();

    /// The amounts a Member may see: every pair that includes the Family (all Transactions behind it are visible
    /// to everyone), and a pair of two Members only for those two.
    public IEnumerable<AmountOwed> VisibleTo(Guid viewerId) =>
        Amounts.Where(a => a.Involves(Owner.Family) || a.Involves(Owner.Member(viewerId)));

    /// True when the Transaction can have a <see cref="PaidBack"/> status at all (cheap check before loading).
    public static bool IsOnAnotherOwnersAccount(Transaction t) =>
        t.OwnerMemberId != t.Account.OwnerMemberId && !t.IsReimbursement;

    public static async Task<AmountOwedLedger> LoadAsync(TreasuryDbContext db, ChfConverter chf, CancellationToken ct = default)
    {
        var start = await db.AmountOwedStartDateAsync(ct) ?? DateOnly.MinValue;

        var charges = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= start && t.OwnerMemberId != t.Account.OwnerMemberId
                && !(t.LinkedTransactionId != null && t.LinkKind == LinkKind.Reimbursement))
            .Select(t => new Row(t.Id, t.Date, t.CreatedAt, t.Amount, t.Account.Currency, t.OwnerMemberId,
                t.Account.OwnerMemberId, true))
            .ToListAsync(ct);
        // One row per Reimbursement: its paying leg, with the Owner of the leg that received the money.
        var reimbursements = await db.Transactions.AsNoTracking()
            .Where(t => t.Date >= start && t.LinkKind == LinkKind.Reimbursement && t.Amount < 0)
            .Join(db.Transactions, t => t.LinkedTransactionId, other => (Guid?)other.Id,
                (t, other) => new Row(t.Id, t.Date, t.CreatedAt, t.Amount, t.Account.Currency, t.OwnerMemberId,
                    other.OwnerMemberId, false))
            .ToListAsync(ct);

        var rows = charges.Concat(reimbursements).ToList();
        if (rows.Count == 0) return new AmountOwedLedger([]);
        var rates = await chf.LoadAsync(rows.Min(r => r.Date), rows.Max(r => r.Date), ct);

        var pending = new Dictionary<(Owner, Owner), int>();
        var items = new List<Item>();
        foreach (var row in rows)
        {
            // Charge: First = the Transaction's Owner, Second = the Account's Owner.
            // Reimbursement: First = the payer, Second = the receiver.
            var (first, second) = (new Owner(row.FirstOwnerId), new Owner(row.SecondOwnerId));
            if (first == second) continue;
            if (rates.ToChf(row.Amount, row.Currency, row.Date) is not { } value)
            {
                var pair = PairOf(first, second);
                pending[pair] = pending.GetValueOrDefault(pair) + 1;
                continue;
            }
            if (value == 0) continue;
            // An Expense on someone else's Account: its Owner owes the Account's Owner. An Income there: the reverse.
            // A Reimbursement (value < 0, the paying leg): the receiver "owes" it back against what the payer owed.
            var (debtor, creditor) = row.IsCharge == value < 0 ? (first, second) : (second, first);
            items.Add(new Item(row.Id, row.Date, row.CreatedAt, debtor, creditor, Math.Abs(value), row.IsCharge));
        }

        var amounts = new List<AmountOwed>();
        var ledger = new AmountOwedLedger(amounts);
        foreach (var group in items.GroupBy(i => PairOf(i.Debtor, i.Creditor)))
        {
            var (a, b) = group.Key;
            var aOwes = group.Where(i => i.Debtor == a).ToList();
            var bOwes = group.Where(i => i.Debtor == b).ToList();
            var (totalA, totalB) = (aOwes.Sum(i => i.Amount), bOwes.Sum(i => i.Amount));
            ledger.Cover(aOwes, totalB);
            ledger.Cover(bOwes, totalA);
            amounts.Add(totalA >= totalB
                ? new AmountOwed(a, b, totalA - totalB, pending.GetValueOrDefault(group.Key))
                : new AmountOwed(b, a, totalB - totalA, pending.GetValueOrDefault(group.Key)));
            pending.Remove(group.Key);
        }
        // Pairs whose every Transaction still waits for a rate.
        amounts.AddRange(pending.Select(p => new AmountOwed(p.Key.Item1, p.Key.Item2, 0, p.Value)));
        return ledger;
    }

    /// Covers one side's items, oldest first, with what the other side adds up to.
    private void Cover(List<Item> side, decimal available)
    {
        foreach (var item in side.OrderBy(i => i.Date).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id))
        {
            var covered = Math.Min(item.Amount, available);
            available -= covered;
            if (!item.IsCharge) continue; // a Reimbursement uses up cover but has no status of its own
            _paidBack[item.Id] = new PaidBack(
                covered == item.Amount ? PaidBackStatus.PaidBack
                : covered > 0 ? PaidBackStatus.PartlyPaidBack
                : PaidBackStatus.Outstanding,
                item.Amount - covered);
        }
    }

    /// The two Owners in a fixed order (the Family first), so both directions land in the same pair.
    private static (Owner, Owner) PairOf(Owner x, Owner y) =>
        Nullable.Compare(x.MemberId, y.MemberId) <= 0 ? (x, y) : (y, x);

    private sealed record Row(Guid Id, DateOnly Date, DateTimeOffset CreatedAt, decimal Amount, string Currency,
        Guid? FirstOwnerId, Guid? SecondOwnerId, bool IsCharge);

    private sealed record Item(Guid Id, DateOnly Date, DateTimeOffset CreatedAt, Owner Debtor, Owner Creditor,
        decimal Amount, bool IsCharge);
}
