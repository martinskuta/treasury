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
            if (!ReportQueries.TryParseOwner(owner, out var reportOwner))
                errors["owner"] = ["Use \"me\" or \"family\"."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var rows = await db.Transactions.InReportScope(user.MemberId(), reportOwner)
                .InPeriod(first, first.AddMonths(1).AddDays(-1))
                .ToReportRowsAsync(chf, ct);
            var tree = await CategoryTree.LoadAsync(db);
            var income = CategoryBreakdown.Build(rows, TransactionType.Income, tree);
            var expense = CategoryBreakdown.Build(rows, TransactionType.Expense, tree);

            return Results.Ok(new MonthlyReportResponse(first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                reportOwner.ToApiValue(), ChfConverter.ReportingCurrency, income, expense, income.Total - expense.Total));
        });

        // ?owner=me|family (default: me) &from=yyyy-MM-dd &to=yyyy-MM-dd (inclusive, either may be left open)
        // &allTime=true. Without from/to/allTime the period is the Tag's date range, or all time when it has none.
        reports.MapGet("/tag/{tagId:guid}", async (Guid tagId, string? owner, string? from, string? to, bool? allTime,
            ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!ReportQueries.TryParseOwner(owner, out var reportOwner))
                errors["owner"] = ["Use \"me\" or \"family\"."];
            var fromDate = ParseDate(from, "from", errors);
            var toDate = ParseDate(to, "to", errors);
            if (fromDate is not null && toDate is not null && fromDate > toDate)
                errors["to"] = ["The end of the period must not be before its start."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var tag = await db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tagId, ct);
            if (tag is null) return Results.NotFound();

            if (fromDate is null && toDate is null && allTime != true)
                (fromDate, toDate) = (tag.DateFrom, tag.DateTo);

            var rows = await db.Transactions.InReportScope(user.MemberId(), reportOwner)
                .TaggedWith(tag.Id)
                .InPeriod(fromDate, toDate)
                .ToReportRowsAsync(chf, ct);
            var tree = await CategoryTree.LoadAsync(db);
            var income = CategoryBreakdown.Build(rows, TransactionType.Income, tree);
            var expense = CategoryBreakdown.Build(rows, TransactionType.Expense, tree);

            return Results.Ok(new TagReportResponse(new TagSummary(tag.Id, tag.Name, tag.DateFrom, tag.DateTo),
                reportOwner.ToApiValue(), fromDate, toDate, ChfConverter.ReportingCurrency, income, expense,
                income.Total - expense.Total));
        });
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
