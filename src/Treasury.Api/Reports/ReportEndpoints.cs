using System.Globalization;
using System.Security.Claims;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Ownership;
using Treasury.Api.Tags;
using Treasury.Api.Transactions;

namespace Treasury.Api.Reports;

public static class ReportEndpoints
{
    /// Income and Expense are positive CHF amounts; Net = Income - Expense.
    public record MonthlyReportResponse(string Month, string Owner, string Currency, CategoryBreakdown Income,
        CategoryBreakdown Expense, decimal Net);

    /// The six months ending at an Owner filter's end month, oldest first. EndMonth = the month of the Owner filter's
    /// latest Transaction, never later than the last complete calendar month (which it is without Transactions).
    public record RecentMonthsResponse(string Owner, string Currency, string EndMonth, IReadOnlyList<MonthTotals> Months);

    /// One month's totals as the Monthly overview counts them: Income and Expense are positive CHF amounts,
    /// Net = Income - Expense. PendingConversion = how many amounts (Income and Expense together) still wait for a
    /// CHF rate and are in no total.
    public record MonthTotals(string Month, decimal Income, decimal Expense, decimal Net, int PendingConversion);

    public record TagSummary(Guid Id, string Name, DateOnly? DateFrom, DateOnly? DateTo);

    /// From/To = the period counted (inclusive; null = open-ended, so both null = all time).
    public record TagReportResponse(TagSummary Tag, string Owner, DateOnly? From, DateOnly? To, string Currency,
        CategoryBreakdown Income, CategoryBreakdown Expense, decimal Net);

    /// /api/reports: reports computed in CHF from the viewer's point of view.
    public static void MapReports(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports");

        // ?month=yyyy-MM (default: the current month) &owner=me|family (default: me)
        reports.MapGet("/monthly", async (string? month, string? owner, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var first = new DateOnly(today.Year, today.Month, 1);
            if (!string.IsNullOrEmpty(month) && !DateOnly.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out first))
                errors["month"] = ["Use a month like 2026-11."];
            var reportOwner = ParseOwner(owner, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var (income, expense, net) = await SidesAsync(db, chf,
                db.Transactions.InReportScope(user.MemberId(), reportOwner).InPeriod(first, LastDayOf(first)), ct);

            return Results.Ok(new MonthlyReportResponse(MonthKey(first), reportOwner.ToApi(),
                ChfConverter.ReportingCurrency, income, expense, net));
        });

        // ?owner=me|family (default: me). The Dashboard's chart and welcome strip: the Owner filter's end month and
        // the six months ending at it, each counted exactly like /monthly.
        reports.MapGet("/recent-months", async (string? owner, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var reportOwner = ParseOwner(owner, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var scoped = db.Transactions.InReportScope(user.MemberId(), reportOwner);
            // Transfers deliberately don't count: the chart would end on a month with nothing to show.
            var end = LastCompleteMonth.FirstDay(clock);
            if (await scoped.MaxAsync(t => (DateOnly?)t.Date, ct) is { } latest && latest < end)
                end = new DateOnly(latest.Year, latest.Month, 1);
            var start = end.AddMonths(1 - RecentMonthCount);

            var rows = await scoped.InPeriod(start, LastDayOf(end)).ToReportRowsAsync(chf, ct);
            var tree = await CategoryTree.LoadAsync(db);
            var months = Enumerable.Range(0, RecentMonthCount).Select(i => start.AddMonths(i)).Select(first =>
            {
                var (income, expense, net) = Sides(
                    rows.Where(r => r.Date.Year == first.Year && r.Date.Month == first.Month).ToList(), tree);
                return new MonthTotals(MonthKey(first), income.Total, expense.Total, net,
                    income.PendingConversion + expense.PendingConversion);
            }).ToList();

            return Results.Ok(new RecentMonthsResponse(reportOwner.ToApi(), ChfConverter.ReportingCurrency,
                MonthKey(end), months));
        });

        // ?owner=me|family (default: me) &from=yyyy-MM-dd &to=yyyy-MM-dd (inclusive, either may be left open)
        // &allTime=true. Without from/to/allTime the period is the Tag's date range, or all time when it has none.
        reports.MapGet("/tag/{tagId:guid}", async (Guid tagId, string? owner, string? from, string? to, bool? allTime,
            ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var reportOwner = ParseOwner(owner, errors);
            var fromDate = ParseDate(from, "from", errors);
            var toDate = ParseDate(to, "to", errors);
            if (fromDate is not null && toDate is not null && fromDate > toDate)
                errors["to"] = ["The end of the period must not be before its start."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var tag = await db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tagId, ct);
            if (tag is null) return Results.NotFound();

            if (fromDate is null && toDate is null && allTime != true)
                (fromDate, toDate) = (tag.DateFrom, tag.DateTo);

            var (income, expense, net) = await SidesAsync(db, chf,
                db.Transactions.InReportScope(user.MemberId(), reportOwner).TaggedWith(tag.Id).InPeriod(fromDate, toDate), ct);

            return Results.Ok(new TagReportResponse(new TagSummary(tag.Id, tag.Name, tag.DateFrom, tag.DateTo),
                reportOwner.ToApi(), fromDate, toDate, ChfConverter.ReportingCurrency, income, expense, net));
        });
    }

    /// How many months /recent-months returns.
    private const int RecentMonthCount = 6;

    /// "yyyy-MM" of the month starting on <paramref name="first"/>.
    private static string MonthKey(DateOnly first) => first.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static DateOnly LastDayOf(DateOnly first) => first.AddMonths(1).AddDays(-1);

    /// The `owner` query value ("me" default, or "family"); anything else adds an `owner` error.
    private static ReportOwner ParseOwner(string? owner, Dictionary<string, string[]> errors)
    {
        if (!ReportQueries.TryParseOwner(owner, out var reportOwner)) errors["owner"] = ["Use \"me\" or \"family\"."];
        return reportOwner;
    }

    /// The Income and Expense breakdowns of the scoped Transactions, and Net = Income - Expense.
    private static async Task<(CategoryBreakdown Income, CategoryBreakdown Expense, decimal Net)> SidesAsync(
        TreasuryDbContext db, ChfConverter chf, IQueryable<Transaction> scoped, CancellationToken ct)
    {
        var rows = await scoped.ToReportRowsAsync(chf, ct);
        return Sides(rows, await CategoryTree.LoadAsync(db));
    }

    private static (CategoryBreakdown Income, CategoryBreakdown Expense, decimal Net) Sides(
        IReadOnlyCollection<ReportRow> rows, CategoryTree tree)
    {
        var income = CategoryBreakdown.Build(rows, TransactionType.Income, tree);
        var expense = CategoryBreakdown.Build(rows, TransactionType.Expense, tree);
        return (income, expense, income.Total - expense.Total);
    }

    private static DateOnly? ParseDate(string? value, string key, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        errors[key] = ["Use a date like 2026-11-20."];
        return null;
    }
}
