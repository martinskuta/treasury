using System.Security.Claims;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Matching;

public static class MatchingEndpoints
{
    public record LinkRequest(Guid? TransactionId);

    /// Kind: "contribution" or "reimbursement".
    public record LinkKindRequest(string? Kind);

    /// Manual link and unlink of a Transfer/Contribution pair. Linking is classification: anyone who can see both
    /// Transactions may link them, and anyone who can see one leg may unlink it.
    public static void MapMatching(this RouteGroupBuilder api)
    {
        var link = api.MapGroup("/transactions/{id:guid}/link");

        // Same Owner on both legs = Transfer, otherwise Reimbursement (the Family pays a Member) or Contribution.
        // 200 with the updated Transaction.
        link.MapPost("/", async (Guid id, LinkRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf, TransactionMatcher matcher) =>
        {
            var viewer = user.MemberId();
            var transaction = await TransactionEndpoints.FindVisibleAsync(db, id, viewer);
            if (transaction is null) return Results.NotFound();

            var other = request.TransactionId is { } otherId
                ? await TransactionEndpoints.FindVisibleAsync(db, otherId, viewer)
                : null;
            var error = other is null ? "Choose a Transaction to link with." : await matcher.LinkByHandAsync(transaction, other);
            if (error is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["transactionId"] = [error] });

            await db.SaveChangesAsync();
            return Results.Ok((await TransactionEndpoints.ToResponsesAsync(db, chf, user.MemberId(), [transaction]))[0]);
        });

        // Switches a pair with different Owners between Contribution and Reimbursement (the choice sticks, ADR 0011).
        // 200 with the updated Transaction; anyone who can see one leg may switch, as with unlinking.
        link.MapPut("/kind", async (Guid id, LinkKindRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf, TransactionMatcher matcher) =>
        {
            var transaction = await TransactionEndpoints.FindVisibleAsync(db, id, user.MemberId());
            if (transaction is null) return Results.NotFound();

            var error = ApiEnums.Parse<LinkKind>(request.Kind) is { } kind
                ? await matcher.SwitchKindAsync(transaction, kind)
                : "Choose Contribution or Reimbursement.";
            if (error is not null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = [error] });

            await db.SaveChangesAsync();
            return Results.Ok((await TransactionEndpoints.ToResponsesAsync(db, chf, user.MemberId(), [transaction]))[0]);
        });

        // Both legs become plain Income/Expense again. 200 with the updated Transaction (also when it wasn't linked).
        link.MapDelete("/", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf,
            TransactionMatcher matcher) =>
        {
            var transaction = await TransactionEndpoints.FindVisibleAsync(db, id, user.MemberId());
            if (transaction is null) return Results.NotFound();

            await matcher.UnlinkAsync(transaction);
            await db.SaveChangesAsync();
            return Results.Ok((await TransactionEndpoints.ToResponsesAsync(db, chf, user.MemberId(), [transaction]))[0]);
        });
    }
}
