using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Ownership;
using Treasury.Api.Tags;

namespace Treasury.Api.Transactions;

/// A requested change to the classification of Transactions: Owner, Category and Tags. Built (and validated) by
/// <see cref="TransactionClassification.ParseAsync"/>; null / empty parts stay as they are.
/// <param name="Uncategorise">Make the Transactions Uncategorised (and let Rules decide again).</param>
public sealed record ClassificationChange(Owner? Owner, Guid? CategoryId, bool Uncategorise,
    IReadOnlyList<Guid> AddTagIds, IReadOnlyList<Guid> RemoveTagIds)
{
    public bool IsEmpty => Owner is null && CategoryId is null && !Uncategorise && AddTagIds.Count == 0 && RemoveTagIds.Count == 0;
}

/// THE per-Transaction rules for changing Owner, Category and Tags by hand, shared by PATCH /api/transactions/{id}
/// and the bulk edit (#17), so both behave the same:
/// - Owner: the Family or an active Member (deactivated ones are refused); sets the Owner manual-set flag.
/// - Category: an existing Category (sets the Category flag), or Uncategorise (clears Category and flag);
///   a Transfer can't get a Category (<see cref="Refusal"/>).
/// - Tags: manual adds and sticky manual removals (<see cref="Tagging"/>); sets the Tags flag.
/// - A changed Owner (or date, for PATCH) re-applies date-range Tags, whose Owner limit depends on it.
/// Visibility is the caller's job: only pass Transactions the Member can see (anyone who sees one may classify it).
public static class TransactionClassification
{
    /// Validates the request parts; problems are added to <paramref name="errors"/> under the request's field names.
    public static async Task<ClassificationChange> ParseAsync(TreasuryDbContext db, OwnerRequest? owner, Guid? categoryId,
        bool? uncategorise, Guid[]? addTagIds, Guid[]? removeTagIds, Dictionary<string, string[]> errors)
    {
        var resolvedOwner = owner is null ? null : await ResolveOwnerAsync(db, owner, errors);
        if (categoryId is not null && uncategorise == true)
            errors["categoryId"] = ["Either choose a Category or make the Transaction Uncategorised."];
        else await ValidateCategoryAsync(db, categoryId, errors);
        var add = await ValidateTagsAsync(db, addTagIds, "addTagIds", errors);
        var remove = await ValidateTagsAsync(db, removeTagIds, "removeTagIds", errors);
        if (add.Intersect(remove).Any()) errors["removeTagIds"] = ["A Tag can't be added and removed at the same time."];
        return new ClassificationChange(resolvedOwner, categoryId, uncategorise == true, add, remove);
    }

    /// The requested Owner: the Family or any active Member; null (with an `owner` error) otherwise.
    public static async Task<Owner?> ResolveOwnerAsync(TreasuryDbContext db, OwnerRequest request,
        Dictionary<string, string[]> errors)
    {
        var owner = request.ToOwner();
        if (owner is { MemberId: { } memberId } && !await db.Users.AnyAsync(m => m.Id == memberId && m.DeactivatedAt == null)) owner = null;
        if (owner is null) errors["owner"] = ["Choose the Family or a Member as the Owner."];
        return owner;
    }

    /// An unknown Category id adds a `categoryId` error; null is fine (no Category).
    public static async Task ValidateCategoryAsync(TreasuryDbContext db, Guid? categoryId, Dictionary<string, string[]> errors)
    {
        if (categoryId is { } id && !await db.Categories.AnyAsync(c => c.Id == id))
            errors["categoryId"] = ["Choose an existing Category."];
    }

    /// The distinct requested Tag ids; an unknown one adds an error under <paramref name="field"/>.
    public static async Task<List<Guid>> ValidateTagsAsync(TreasuryDbContext db, Guid[]? tagIds, string field,
        Dictionary<string, string[]> errors)
    {
        var ids = (tagIds ?? []).Distinct().ToList();
        if (ids.Count > 0 && await db.Tags.CountAsync(t => ids.Contains(t.Id)) != ids.Count)
            errors[field] = ["Choose existing Tags."];
        return ids;
    }

    /// Why the change can't be made to this Transaction (message for the Member, under `categoryId`), or null.
    public static string? Refusal(Transaction transaction, ClassificationChange change) =>
        change.CategoryId is not null && transaction.IsTransfer
            ? "A Transfer has no Category. Unlink it first to make it an Income or Expense."
            : null;

    /// Sets Owner and Category with their manual-set flags (and the Tags flag when Tags change). Tags themselves are
    /// changed by <see cref="SaveAsync"/>. Returns true when the Owner actually changed.
    public static bool ApplyFields(Transaction transaction, ClassificationChange change)
    {
        var reowned = change.Owner is { } o && o != transaction.Owner;
        if (change.Owner is { } owner)
        {
            transaction.Owner = owner;
            transaction.ManuallySet |= ManuallySetFields.Owner;
        }
        if (change.CategoryId is { } categoryId)
        {
            transaction.CategoryId = categoryId;
            transaction.ManuallySet |= ManuallySetFields.Category;
        }
        else if (change.Uncategorise)
        {
            // Back in the Uncategorised queue, where Rules may categorise it again (#15): not a manual Category.
            transaction.CategoryId = null;
            transaction.ManuallySet &= ~ManuallySetFields.Category;
        }
        if (change.AddTagIds.Count > 0 || change.RemoveTagIds.Count > 0) transaction.ManuallySet |= ManuallySetFields.Tags;
        return reowned;
    }

    /// Saves the tracked changes, then brings date-range Tags of <paramref name="needDateRanges"/> (Transactions
    /// whose date or Owner changed) up to date, then applies the manual Tag removals and adds to
    /// <paramref name="transactions"/> (a removal also takes off a date-range link added just now). Each step reads
    /// what the previous one saved, so all of it runs in one DB transaction (all or nothing).
    public static Task SaveAsync(TreasuryDbContext db, IReadOnlyCollection<Transaction> transactions,
        IReadOnlyCollection<Transaction> needDateRanges, ClassificationChange change) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            if (needDateRanges.Count > 0)
            {
                await Tagging.ApplyDateRangesAsync(db, needDateRanges);
                await db.SaveChangesAsync();
            }
            var ids = transactions.Select(t => t.Id).ToList();
            await Tagging.RemoveByHandAsync(db, ids, change.RemoveTagIds);
            await db.SaveChangesAsync();
            await Tagging.AddByHandAsync(db, ids, change.AddTagIds);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
}
