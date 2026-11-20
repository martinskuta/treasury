using System.Globalization;
using System.Security.Claims;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Reports;

public static class ReportEndpoints
{
    /// Income and Expense are positive CHF amounts; Net = Income - Expense.
    public record MonthlyReportResponse(string Month, string Owner, string Currency, CategoryBreakdown Income,
        CategoryBreakdown Expense, decimal Net);

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
    }
}
