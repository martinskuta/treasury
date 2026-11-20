using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Transactions;

public static class TransactionEndpoints
{
    public const int MaxDescriptionLength = 500;
    public const int MaxNoteLength = 2000;

    public record CreateTransactionRequest(Guid? AccountId, DateOnly? Date, decimal? Amount, string? Description,
        string? Note, OwnerRequest? Owner);

    /// Partial update: a missing (null) field stays as it is; an empty note clears the note.
    public record UpdateTransactionRequest(DateOnly? Date, decimal? Amount, string? Description, string? Note,
        OwnerRequest? Owner);

    public record TransactionResponse(Guid Id, Guid AccountId, string AccountName, DateOnly Date, decimal Amount,
        string Currency, string Type, string Description, string? Note, OwnerResponse Owner, string Origin);

    /// /api/transactions: the Transactions the current Member can see, and manual Transactions.
    public static void MapTransactions(this RouteGroupBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        transactions.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var list = await db.Transactions.VisibleTo(user.MemberId()).Include(t => t.Account)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, list));
        });

        transactions.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var transaction = await FindVisibleAsync(db, id, user.MemberId());
            return transaction is null ? Results.NotFound() : Results.Ok((await ToResponsesAsync(db, [transaction]))[0]);
        });

        transactions.MapPost("/", async (CreateTransactionRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var errors = new Dictionary<string, string[]>();

            var account = request.AccountId is { } accountId
                ? await db.Accounts.OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(a => a.Id == accountId)
                : null;
            if (account is null) errors["accountId"] = ["Choose an Account."];

            if (request.Date is null) errors["date"] = ["Enter the date."];
            ValidateAmount(request.Amount, errors);
            var description = request.Description?.Trim();
            ValidateDescription(description, errors);
            var note = NormalizeNote(request.Note, errors);
            var owner = request.Owner is null ? null : await ResolveOwnerAsync(db, request.Owner, errors);

            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var transaction = new Transaction
            {
                AccountId = account!.Id,
                Account = account,
                Origin = TransactionOrigin.Manual,
                Date = request.Date!.Value,
                Amount = request.Amount!.Value,
                Description = description!,
                Note = note,
                Owner = owner ?? account.Owner,
                ManuallySet = owner is null ? ManuallySetFields.None : ManuallySetFields.Owner,
                CreatedAt = clock.GetUtcNow(),
            };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();

            return Results.Created($"/api/transactions/{transaction.Id}", (await ToResponsesAsync(db, [transaction]))[0]);
        });

        // Classification (Owner, note) can be changed by anyone who sees the Transaction; the facts (date, amount,
        // description) only on manual Transactions, by Members who can manage the Account.
        transactions.MapPatch("/{id:guid}", async (Guid id, UpdateTransactionRequest request, ClaimsPrincipal user,
            TreasuryDbContext db) =>
        {
            var viewer = user.MemberId();
            var transaction = await FindVisibleAsync(db, id, viewer);
            if (transaction is null) return Results.NotFound();

            var errors = new Dictionary<string, string[]>();
            var changesFacts = request.Date is not null || request.Amount is not null || request.Description is not null;
            if (changesFacts)
            {
                if (!transaction.Account.Owner.IsManageableBy(viewer)) return Results.Forbid();
                if (!transaction.FactsEditable)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["transaction"] = ["The date, amount and description of an imported Transaction can't be changed."],
                    });
                if (request.Amount is not null) ValidateAmount(request.Amount, errors);
            }
            var description = request.Description?.Trim();
            if (request.Description is not null) ValidateDescription(description, errors);
            var note = request.Note is null ? transaction.Note : NormalizeNote(request.Note, errors);
            var owner = request.Owner is null ? null : await ResolveOwnerAsync(db, request.Owner, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (request.Date is { } date) transaction.Date = date;
            if (request.Amount is { } amount) transaction.Amount = amount;
            if (description is not null) transaction.Description = description;
            transaction.Note = note;
            if (owner is { } newOwner)
            {
                transaction.Owner = newOwner;
                transaction.ManuallySet |= ManuallySetFields.Owner;
            }
            await db.SaveChangesAsync();
            return Results.Ok((await ToResponsesAsync(db, [transaction]))[0]);
        });

        // Manual Transactions only (imported ones go away by undoing their import), by Members who manage the Account.
        transactions.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var viewer = user.MemberId();
            var transaction = await FindVisibleAsync(db, id, viewer);
            if (transaction is null) return Results.NotFound();
            if (!transaction.Account.Owner.IsManageableBy(viewer)) return Results.Forbid();
            if (!transaction.FactsEditable)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["transaction"] = ["An imported Transaction is removed by deleting its Source file."],
                });

            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Task<Transaction?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.Transactions.VisibleTo(viewer).Include(t => t.Account).SingleOrDefaultAsync(t => t.Id == id);

    /// The requested Owner: the Family or any existing Member; null (with an `owner` error) otherwise.
    private static async Task<Owner?> ResolveOwnerAsync(TreasuryDbContext db, OwnerRequest request,
        Dictionary<string, string[]> errors)
    {
        var owner = request.ToOwner();
        if (owner is { MemberId: { } memberId } && !await db.Users.AnyAsync(m => m.Id == memberId)) owner = null;
        if (owner is null) errors["owner"] = ["Choose the Family or a Member as the Owner."];
        return owner;
    }

    private static void ValidateAmount(decimal? amount, Dictionary<string, string[]> errors)
    {
        if (amount is null) errors["amount"] = ["Enter the amount."];
        else if (amount == 0) errors["amount"] = ["Enter a positive amount for Income or a negative one for an Expense."];
    }

    private static void ValidateDescription(string? description, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(description)) errors["description"] = ["Enter a description."];
        else if (description.Length > MaxDescriptionLength)
            errors["description"] = [$"Use at most {MaxDescriptionLength} characters."];
    }

    /// Trimmed note, null when empty.
    private static string? NormalizeNote(string? note, Dictionary<string, string[]> errors)
    {
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxNoteLength) errors["note"] = [$"Use at most {MaxNoteLength} characters."];
        return trimmed;
    }

    private static async Task<List<TransactionResponse>> ToResponsesAsync(TreasuryDbContext db, IReadOnlyList<Transaction> list)
    {
        var names = await db.MemberNamesAsync(list.Select(t => t.OwnerMemberId));
        return list.Select(t => new TransactionResponse(t.Id, t.AccountId, t.Account.Name, t.Date, t.Amount,
            t.Account.Currency, t.Type == TransactionType.Income ? "income" : "expense", t.Description, t.Note,
            OwnerResponse.From(t.Owner, names), t.Origin == TransactionOrigin.Manual ? "manual" : "imported")).ToList();
    }
}
