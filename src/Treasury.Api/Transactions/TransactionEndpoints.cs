using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Matching;
using Treasury.Api.Ownership;
using Treasury.Api.Tags;

namespace Treasury.Api.Transactions;

public static class TransactionEndpoints
{
    public const int MaxDescriptionLength = 500;
    public const int MaxNoteLength = 2000;
    public const int MaxDisplayNameLength = 200;

    public record CreateTransactionRequest(Guid? AccountId, DateOnly? Date, decimal? Amount, string? Description,
        string? Note, OwnerRequest? Owner, Guid? CategoryId, Guid[]? TagIds);

    /// Partial update: a missing (null) field stays as it is; an empty note or Display name clears it;
    /// `uncategorise: true` makes the Transaction Uncategorised (instead of setting a `categoryId`).
    /// `addTagIds` / `removeTagIds` add or remove Tags by hand (a removal sticks: no date range or Rule re-adds it).
    public record UpdateTransactionRequest(DateOnly? Date, decimal? Amount, string? Description, string? Note,
        OwnerRequest? Owner, Guid? CategoryId, bool? Uncategorise, string? DisplayName, Guid[]? AddTagIds,
        Guid[]? RemoveTagIds);

    public const int MaxBulkEdit = 500;

    /// Bulk edit: the Transactions to change and the change (each part optional, same meaning as in PATCH).
    public record BulkEditRequest(Guid[]? TransactionIds, OwnerRequest? Owner, Guid? CategoryId, bool? Uncategorise,
        Guid[]? AddTagIds, Guid[]? RemoveTagIds);

    /// Validation-problem key for a Transaction of a bulk edit that can't take the change.
    public static string BulkErrorKey(Guid transactionId) => $"transactions[{transactionId}]";

    /// Category null = Uncategorised. ManuallySet lists the classification fields a Member set by hand
    /// ("owner", "category", "tags", "displayName"). Tags lists each Tag with the sources that put it there
    /// ("manual", "dateRange", "rule").
    /// <param name="Description">The original description (never replaced by the Display name).</param>
    /// <param name="OriginalAmount">Foreign-currency purchase: the amount in <paramref name="OriginalCurrency"/>.</param>
    /// <param name="SourceFile">The Source file an imported Transaction came from; null for manual ones.</param>
    public record TransactionResponse(Guid Id, Guid AccountId, string AccountName, DateOnly Date, decimal Amount,
        string Currency, decimal? ChfAmount, string Type, string Description, string? DisplayName,
        string? Counterparty, string? Reference, decimal? OriginalAmount, string? OriginalCurrency, string? Note,
        OwnerResponse Owner, string Origin, CategoryRef? Category, TagRef[] Tags, string[] ManuallySet,
        SourceFileRef? SourceFile, LinkRef? Link);

    /// The other leg of a Transfer or Contribution. Kind "transfer" | "contribution". The other leg may be in an
    /// Account the viewer can't see (then GET on it is 404).
    public record LinkRef(string Kind, Guid TransactionId);

    public record CategoryRef(Guid Id, string Name, string Path);

    public record TagRef(Guid Id, string Name, string[] Sources);

    public record SourceFileRef(Guid Id, string FileName);

    /// /api/transactions: the Transactions the current Member can see, and manual Transactions.
    public static void MapTransactions(this RouteGroupBuilder api)
    {
        var transactions = api.MapGroup("/transactions");

        // Filters, sorting and paging: see TransactionListRequest. One page as a JSON array; the number of matching
        // Transactions across all pages is in the X-Total-Count header.
        transactions.MapGet("/", async ([AsParameters] TransactionListRequest request, ClaimsPrincipal user,
            HttpResponse response, TreasuryDbContext db, ChfConverter chf) =>
        {
            var (query, errors) = await TransactionList.FilterAsync(db, user.MemberId(), request);
            if (query is null) return Results.ValidationProblem(errors);
            var total = await query.CountAsync();
            var list = await TransactionList.SortAndPage(query, request)
                .Include(t => t.Account).Include(t => t.SourceFile).ToListAsync();
            response.Headers[TransactionList.TotalCountHeader] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            TimeProvider clock, ChfConverter chf, ExchangeRateSyncSignal rates, TransactionMatcher matcher) =>
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
            var owner = request.Owner is null ? null : await TransactionClassification.ResolveOwnerAsync(db, request.Owner, errors);
            await ValidateCategoryAsync(db, request.CategoryId, errors);
            var tagIds = await TransactionClassification.ValidateTagsAsync(db, request.TagIds, "tagIds", errors);

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
                    | (request.CategoryId is null ? ManuallySetFields.None : ManuallySetFields.Category)
                    | (tagIds.Count == 0 ? ManuallySetFields.None : ManuallySetFields.Tags),
                CreatedAt = clock.GetUtcNow(),
            };
            db.Transactions.Add(transaction);
            await Tagging.AddByHandAsync(db, transaction.Id, tagIds);
            await Tagging.ApplyDateRangesAsync(db, [transaction]);
            await db.SaveChangesAsync();
            await matcher.MatchAsync([transaction]);
            await db.SaveChangesAsync();
            await rates.RequestIfNotCoveredAsync(db, transaction.Date);

            return Results.Created($"/api/transactions/{transaction.Id}", (await ToResponsesAsync(db, chf, [transaction]))[0]);
        });

        // Classification (Owner, Category, note, Display name) can be changed by anyone who sees the Transaction; the facts (date, amount,
        // description) only on manual Transactions, by Members who can manage the Account.
        transactions.MapPatch("/{id:guid}", async (Guid id, UpdateTransactionRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, ChfConverter chf, ExchangeRateSyncSignal rates, TransactionMatcher matcher) =>
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
            var change = await TransactionClassification.ParseAsync(db, request.Owner, request.CategoryId,
                request.Uncategorise, request.AddTagIds, request.RemoveTagIds, errors);
            if (!errors.ContainsKey("categoryId") && TransactionClassification.Refusal(transaction, change) is { } refusal)
                errors["categoryId"] = [refusal];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var redated = request.Date is { } newDate && newDate != transaction.Date;

            if (request.Date is { } date) transaction.Date = date;
            if (request.Amount is { } amount) transaction.Amount = amount;
            if (description is not null) transaction.Description = description;
            transaction.Note = note;
            if (request.DisplayName is not null)
            {
                transaction.DisplayName = displayName;
                transaction.ManuallySet |= ManuallySetFields.DisplayName;
            }
            var reowned = TransactionClassification.ApplyFields(transaction, change);

            // New date or amount: the old pair may no longer fit, and a new one may (#16).
            var rematch = request.Date is not null || request.Amount is not null;
            if (rematch) await matcher.UnlinkAsync(transaction);
            else if (reowned) await matcher.RefreshLinksAsync([transaction]); // Transfer <-> Contribution

            // Date-range Tags follow the new date/Owner, then the Member's own Tag changes.
            await TransactionClassification.SaveAsync(db, [transaction], redated || reowned ? [transaction] : [], change);
            if (rematch)
            {
                await matcher.MatchAsync([transaction]);
                await db.SaveChangesAsync();
            }
            if (request.Date is not null) await rates.RequestIfNotCoveredAsync(db, transaction.Date);
            return Results.Ok((await ToResponsesAsync(db, chf, [transaction]))[0]);
        });

        // Bulk edit: the same Owner / Category / Tag change on several Transactions, with exactly the rules of PATCH
        // (TransactionClassification). All or nothing: if any Transaction can't take the change (not visible, or a
        // Transfer given a Category), nothing is changed and the 400 lists each refused id under `transactions[<id>]`.
        transactions.MapPost("/bulk-edit", async (BulkEditRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter chf, TransactionMatcher matcher) =>
        {
            var viewer = user.MemberId();
            var errors = new Dictionary<string, string[]>();
            var ids = (request.TransactionIds ?? []).Distinct().ToList();
            if (ids.Count == 0) errors["transactionIds"] = ["Select at least one Transaction."];
            else if (ids.Count > MaxBulkEdit) errors["transactionIds"] = [$"Select at most {MaxBulkEdit} Transactions."];
            var change = await TransactionClassification.ParseAsync(db, request.Owner, request.CategoryId,
                request.Uncategorise, request.AddTagIds, request.RemoveTagIds, errors);
            if (change.IsEmpty && !errors.Keys.Any(k => k is "owner" or "categoryId" or "addTagIds" or "removeTagIds"))
                errors["change"] = ["Choose an Owner, a Category or Tags to change."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var found = await db.Transactions.VisibleTo(viewer).Where(t => ids.Contains(t.Id))
                .Include(t => t.Account).Include(t => t.SourceFile).ToDictionaryAsync(t => t.Id);
            foreach (var id in ids)
            {
                // Unknown and invisible Transactions get the same answer (no existence leak).
                if (!found.TryGetValue(id, out var t)) errors[BulkErrorKey(id)] = ["This Transaction doesn't exist."];
                else if (TransactionClassification.Refusal(t, change) is { } refusal) errors[BulkErrorKey(id)] = [refusal];
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var list = ids.Select(id => found[id]).ToList();
            var reowned = list.Where(t => TransactionClassification.ApplyFields(t, change)).ToList();
            await matcher.RefreshLinksAsync(reowned); // Transfer <-> Contribution
            await TransactionClassification.SaveAsync(db, list, reowned, change);
            return Results.Ok(await ToResponsesAsync(db, chf, list));
        });

        // Manual Transactions only (imported ones go away by undoing their import), by Members who manage the Account.
        transactions.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            TransactionMatcher matcher, FormerLegs formerLegs) =>
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

            var other = await matcher.UnlinkAsync(transaction); // the other leg becomes Income or Expense again
            db.Transactions.Remove(transaction);
            await db.SaveChangesAsync();
            if (other is not null) await formerLegs.SettleAsync([other.Id]);
            return Results.NoContent();
        });
    }

    internal static Task<Transaction?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.Transactions.VisibleTo(viewer).Include(t => t.Account).Include(t => t.SourceFile).SingleOrDefaultAsync(t => t.Id == id);

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
        var tags = await Tagging.LoadAsync(db, list.Select(t => t.Id).ToList());
        return list.Select(t => new TransactionResponse(t.Id, t.AccountId, t.Account.Name, t.Date, t.Amount,
            t.Account.Currency, rates!.ToChf(t.Amount, t.Account.Currency, t.Date),
            TypeName(t.Type), t.Description, t.DisplayName,
            t.Counterparty, t.Reference, t.OriginalAmount, t.OriginalCurrency, t.Note, OwnerResponse.From(t.Owner, names),
            t.Origin == TransactionOrigin.Manual ? "manual" : "imported",
            ToCategoryRef(categories, t.CategoryId), ToTagRefs(tags.GetValueOrDefault(t.Id)), ManuallySetNames(t.ManuallySet),
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

    private static TagRef[] ToTagRefs(List<TagOnTransaction>? tags) =>
        tags?.Select(t => new TagRef(t.Id, t.Name, t.Sources.Select(CamelCase).ToArray())).ToArray() ?? [];

    private static string CamelCase<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static string[] ManuallySetNames(ManuallySetFields fields) =>
        Enum.GetValues<ManuallySetFields>()
            .Where(f => f != ManuallySetFields.None && fields.HasFlag(f))
            .Select(f => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(f.ToString()))
            .ToArray();
}
