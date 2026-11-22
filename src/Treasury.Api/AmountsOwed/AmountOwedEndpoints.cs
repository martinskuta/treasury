using System.Security.Claims;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Settings;

namespace Treasury.Api.AmountsOwed;

public static class AmountOwedEndpoints
{
    /// StartDate = the Family's start date (null = every Transaction counts). Amounts: pairs that include the
    /// Family first, then by the two names.
    public record AmountsOwedResponse(string Currency, DateOnly? StartDate, IReadOnlyList<AmountOwedResponse> Amounts);

    /// Debtor owes Creditor Amount (CHF, 0 = nothing either way). PendingConversion = Transactions between the two
    /// that still wait for a CHF rate and are not in Amount.
    public record AmountOwedResponse(OwnerResponse Debtor, OwnerResponse Creditor, decimal Amount, int PendingConversion);

    /// GET /api/amounts-owed: what Owners owe each other (GLOSSARY: Amount owed), as far as the viewer may see:
    /// every pair that includes the Family, and pairs of two Members only when the viewer is one of them.
    /// Pairs with nothing between them are left out.
    public static void MapAmountsOwed(this RouteGroupBuilder api)
    {
        api.MapGet("/amounts-owed", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf,
            CancellationToken ct) =>
        {
            var ledger = await AmountOwedLedger.LoadAsync(db, chf, ct);
            var visible = ledger.VisibleTo(user.MemberId()).ToList();
            var names = await db.MemberNamesAsync(visible.SelectMany(a => new[] { a.Debtor.MemberId, a.Creditor.MemberId }));
            var amounts = visible
                .Select(a => new AmountOwedResponse(OwnerResponse.From(a.Debtor, names),
                    OwnerResponse.From(a.Creditor, names), a.Amount, a.PendingConversion))
                .OrderBy(a => a.Debtor.MemberId is null || a.Creditor.MemberId is null ? 0 : 1)
                .ThenBy(a => a.Debtor.MemberId is null ? a.Creditor.Name : a.Debtor.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Creditor.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Results.Ok(new AmountsOwedResponse(ChfConverter.ReportingCurrency,
                await db.AmountOwedStartDateAsync(ct), amounts));
        });
    }
}
