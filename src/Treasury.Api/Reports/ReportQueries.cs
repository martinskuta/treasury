using Microsoft.EntityFrameworkCore;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Transactions;

namespace Treasury.Api.Reports;

/// Whose figures a report shows (spec #1: reports are filtered by Owner, from the viewer's point of view).
public enum ReportOwner
{
    /// Transactions the viewer owns (wherever they sit: a personal or a Family Account).
    Me,

    /// Family-owned Transactions only, so no Member's private spending leaks into shared numbers.
    Family,
}

/// The building blocks every report shares (#18 monthly overview, #19 Tag report):
/// scope (visibility + Owner + "counts as Income/Expense") -> optional extra filters (period, Tag) -> CHF rows.
public static class ReportQueries
{
    /// Parses the `owner` query value: "me" or "family" (case-insensitive). Null/empty means "me".
    public static bool TryParseOwner(string? value, out ReportOwner owner)
    {
        owner = ReportOwner.Me;
        if (string.IsNullOrEmpty(value) || value.Equals("me", StringComparison.OrdinalIgnoreCase)) return true;
        if (!value.Equals("family", StringComparison.OrdinalIgnoreCase)) return false;
        owner = ReportOwner.Family;
        return true;
    }

    public static string ToApiValue(this ReportOwner owner) => owner == ReportOwner.Family ? "family" : "me";

    /// The Transactions a report for <paramref name="viewerId"/> may count: visible to the viewer, Income or
    /// Expense (never Transfers), and owned by the viewer ("me") or by the Family ("family").
    /// Compose further filters (<see cref="InPeriod"/>, a Tag for #19) on the result.
    public static IQueryable<Transaction> InReportScope(this IQueryable<Transaction> query, Guid viewerId,
        ReportOwner owner)
    {
        var scoped = query.VisibleTo(viewerId).CountsAsIncomeOrExpense();
        return owner == ReportOwner.Family
            ? scoped.Where(t => t.OwnerMemberId == null)
            : scoped.Where(t => t.OwnerMemberId == viewerId);
    }

    /// Transactions dated from <paramref name="from"/> to <paramref name="to"/>, both inclusive.
    public static IQueryable<Transaction> InPeriod(this IQueryable<Transaction> query, DateOnly from, DateOnly to) =>
        query.Where(t => t.Date >= from && t.Date <= to);

    /// Loads the scoped Transactions as CHF rows in one query plus one rate query. Chf is null when no rate on
    /// or before the Transaction's date is stored yet (pending conversion).
    public static async Task<List<ReportRow>> ToReportRowsAsync(this IQueryable<Transaction> query, ChfConverter chf,
        CancellationToken ct = default)
    {
        var rows = await query.AsNoTracking()
            .Select(t => new { t.CategoryId, t.Amount, t.Account.Currency, t.Date })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];
        var rates = await chf.LoadAsync(rows.Min(r => r.Date), rows.Max(r => r.Date), ct);
        return rows.Select(r => new ReportRow(r.CategoryId, r.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
            rates.ToChf(r.Amount, r.Currency, r.Date) is { } c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null))
            .ToList();
    }
}

/// One Transaction as a report sees it. <see cref="Chf"/> keeps the sign (Expense negative); null = pending conversion.
public sealed record ReportRow(Guid? CategoryId, TransactionType Type, decimal? Chf);
