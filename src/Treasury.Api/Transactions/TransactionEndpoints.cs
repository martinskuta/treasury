using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Ownership;

namespace Treasury.Api.Transactions;

public static class TransactionEndpoints
{
    public const int MaxDescriptionLength = 500;
    public const int MaxNoteLength = 2000;
    public const int MaxDisplayNameLength = 200;

    public record CreateTransactionRequest(Guid? AccountId, DateOnly? Date, decimal? Amount, string? Description,
        string? Note, OwnerRequest? Owner, Guid? CategoryId);

    /// Partial update: a missing (null) field stays as it is; an empty note or Display name clears it;
    /// `uncategorise: true` makes the Transaction Uncategorised (instead of setting a `categoryId`).
    public record UpdateTransactionRequest(DateOnly? Date, decimal? Amount, string? Description, string? Note,
        OwnerRequest? Owner, Guid? CategoryId, bool? Uncategorise, string? DisplayName);

    /// Category null = Uncategorised. ManuallySet lists the classification fields a Member set by hand
    /// ("owner", "category", "displayName").
    /// <param name="Description">The original description (never replaced by the Display name).</param>
    /// <param name="OriginalAmount">Foreign-currency purchase: the amount in <paramref name="OriginalCurrency"/>.</param>
    /// <param name="SourceFile">The Source file an imported Transaction came from; null for manual ones.</param>
    public record TransactionResponse(Guid Id, Guid AccountId, string AccountName, DateOnly Date, decimal Amount,
        string Currency, decimal? ChfAmount, string Type, string Description, string? DisplayName,
        string? Counterparty, string? Reference, decimal? OriginalAmount, string? OriginalCurrency, string? Note,
        OwnerResponse Owner, string Origin, CategoryRef? Category, string[] ManuallySet, SourceFileRef? SourceFile,
        LinkRef? Link);

    /// The other leg of a Transfer or Contribution. Kind "transfer" | "contribution". The other leg may be in an
    /// Account the viewer can't see (then GET on it is 404).
    public record LinkRef(string Kind, Guid TransactionId);

    public record CategoryRef(Guid Id, string Name, string Path);

    public record SourceFileRef(Guid Id, string FileName);

    /// /api/transactions: the Transactions the current Member can see, and manual Transactions.
    public static void MapTransactions(this RouteGroupBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        transactions.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf) =>
        {
            var list = await db.Transactions.VisibleTo(user.MemberId()).Include(t => t.Account).Include(t => t.SourceFile)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, chf, list));
        });

        // The Uncategorised queue: visible Income and Expense without a Category, newest first.
        transactions.MapGet("/uncategorised", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter chf) =>
        {
            var list = await db.Transactions.VisibleTo(user.MemberId()).Uncategorised().Include(t => t.Account).Include(t => t.SourceFile)
                .OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, chf, list));
        });

        transactions.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf) =>
        {
            var transaction = await FindVisibleAsync(db, id, user.MemberId());
            return transaction is null ? Results.NotFound() : Results.Ok((await ToResponsesAsync(db, chf, [transaction]))[0]);
        });

        transactions.MapPost("/", async (CreateTransactionRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            TimeProvider clock, ChfConverter chf, ExchangeRateSyncSignal rates) =>
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
            await ValidateCategoryAsync(db, request.CategoryId, errors);

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
                CategoryId = request.CategoryId,
                ManuallySet = (owner is null ? ManuallySetFields.None : ManuallySetFields.Owner)
                    | (request.CategoryId is null ? ManuallySetFields.None : ManuallySetFields.Category),
                CreatedAt = clock.GetUtcNow(),
            };
            db.Transactions.Add(transaction);
            await db.SaveChangesAsync();
            await rates.RequestIfNotCoveredAsync(db, transaction.Date);

            return Results.Created($"/api/transactions/{transaction.Id}", (await ToResponsesAsync(db, chf, [transaction]))[0]);
        });

        // Classification (Owner, Category, note, Display name) can be changed by anyone who sees the Transaction; the facts (date, amount,
        // description) only on manual Transactions, by Members who can manage the Account.
        transactions.MapPatch("/{id:guid}", async (Guid id, UpdateTransactionRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, ChfConverter chf, ExchangeRateSyncSignal rates) =>
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
            var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
            if (displayName?.Length > MaxDisplayNameLength)
                errors["displayName"] = [$"Use at most {MaxDisplayNameLength} characters."];
            var owner = request.Owner is null ? null : await ResolveOwnerAsync(db, request.Owner, errors);
            if (request.CategoryId is not null && request.Uncategorise == true)
                errors["categoryId"] = ["Either choose a Category or make the Transaction Uncategorised."];
            else await ValidateCategoryAsync(db, request.CategoryId, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (request.Date is { } date) transaction.Date = date;
            if (request.Amount is { } amount) transaction.Amount = amount;
            if (description is not null) transaction.Description = description;
            transaction.Note = note;
            if (request.DisplayName is not null)
            {
                transaction.DisplayName = displayName;
                transaction.ManuallySet |= ManuallySetFields.DisplayName;
            }
            if (owner is { } newOwner)
            {
                transaction.Owner = newOwner;
                transaction.ManuallySet |= ManuallySetFields.Owner;
            }
            if (request.CategoryId is { } categoryId)
            {
                transaction.CategoryId = categoryId;
                transaction.ManuallySet |= ManuallySetFields.Category;
            }
            else if (request.Uncategorise == true)
            {
                // Back in the Uncategorised queue, where Rules may categorise it again (#15): not a manual Category.
                transaction.CategoryId = null;
                transaction.ManuallySet &= ~ManuallySetFields.Category;
            }
            await db.SaveChangesAsync();
            if (request.Date is not null) await rates.RequestIfNotCoveredAsync(db, transaction.Date);
            return Results.Ok((await ToResponsesAsync(db, chf, [transaction]))[0]);
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
        db.Transactions.VisibleTo(viewer).Include(t => t.Account).Include(t => t.SourceFile).SingleOrDefaultAsync(t => t.Id == id);

    /// The requested Owner: the Family or any active Member; null (with an `owner` error) otherwise.
    private static async Task<Owner?> ResolveOwnerAsync(TreasuryDbContext db, OwnerRequest request,
        Dictionary<string, string[]> errors)
    {
        var owner = request.ToOwner();
        if (owner is { MemberId: { } memberId } && !await db.Users.AnyAsync(m => m.Id == memberId && m.DeactivatedAt == null)) owner = null;
        if (owner is null) errors["owner"] = ["Choose the Family or a Member as the Owner."];
        return owner;
    }

    private static async Task ValidateCategoryAsync(TreasuryDbContext db, Guid? categoryId,
        Dictionary<string, string[]> errors)
    {
        if (categoryId is { } id && !await db.Categories.AnyAsync(c => c.Id == id))
            errors["categoryId"] = ["Choose an existing Category."];
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

    internal static async Task<List<TransactionResponse>> ToResponsesAsync(TreasuryDbContext db, ChfConverter chf,
        IReadOnlyList<Transaction> list)
    {
        var names = await db.MemberNamesAsync(list.Select(t => t.OwnerMemberId));
        var rates = list.Count == 0 ? null : await chf.LoadAsync(list.Min(t => t.Date), list.Max(t => t.Date));
        var categories = list.Any(t => t.CategoryId is not null) ? await CategoryTree.LoadAsync(db) : new CategoryTree([]);
        return list.Select(t => new TransactionResponse(t.Id, t.AccountId, t.Account.Name, t.Date, t.Amount,
            t.Account.Currency, rates!.ToChf(t.Amount, t.Account.Currency, t.Date),
            TypeName(t.Type), t.Description, t.DisplayName,
            t.Counterparty, t.Reference, t.OriginalAmount, t.OriginalCurrency, t.Note, OwnerResponse.From(t.Owner, names),
            t.Origin == TransactionOrigin.Manual ? "manual" : "imported",
            ToCategoryRef(categories, t.CategoryId), ManuallySetNames(t.ManuallySet),
            t.SourceFile is null ? null : new SourceFileRef(t.SourceFile.Id, t.SourceFile.FileName),
            t is { LinkedTransactionId: { } linked, LinkKind: { } kind }
                ? new LinkRef(kind == LinkKind.Transfer ? "transfer" : "contribution", linked)
                : null)).ToList();
    }

    private static string TypeName(TransactionType type) => type switch
    {
        TransactionType.Income => "income",
        TransactionType.Expense => "expense",
        _ => "transfer",
    };

    private static CategoryRef? ToCategoryRef(CategoryTree tree, Guid? categoryId) =>
        categoryId is { } id && tree.Find(id) is { } c ? new(c.Id, c.Name, tree.PathOf(c.Id)) : null;

    private static string[] ManuallySetNames(ManuallySetFields fields) =>
        Enum.GetValues<ManuallySetFields>()
            .Where(f => f != ManuallySetFields.None && fields.HasFlag(f))
            .Select(f => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(f.ToString()))
            .ToArray();
}
