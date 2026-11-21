using Microsoft.EntityFrameworkCore;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Transactions;

/// Query string of GET /api/transactions. Every filter is optional and they combine with AND; the result always
/// starts from <see cref="TransactionQueries.VisibleTo"/>, so no filter can reach beyond what the viewer may see.
/// <param name="AccountId">Transactions in this Account.</param>
/// <param name="Owner">"me", "family" or a Member id: the Transaction's Owner (not the Account's).</param>
/// <param name="CategoryId">This Category or any of its subcategories.</param>
/// <param name="Uncategorised">true: only Uncategorised Income and Expense (no Transfers). Not together with CategoryId.</param>
/// <param name="TagId">Transactions carrying this Tag (any source).</param>
/// <param name="From">First date, inclusive.</param>
/// <param name="To">Last date, inclusive.</param>
/// <param name="Q">Text, case-insensitive, found anywhere in the description, Display name, counterparty, reference or note.</param>
/// <param name="Sort">"date-desc" (default, newest first), "date-asc", "amount-asc" (largest Expense first), "amount-desc".</param>
/// <param name="Offset">Rows to skip (paging), default 0.</param>
/// <param name="Limit">Page size, default <see cref="TransactionList.DefaultLimit"/>, at most <see cref="TransactionList.MaxLimit"/>.</param>
public record TransactionListRequest(Guid? AccountId, string? Owner, Guid? CategoryId, bool? Uncategorised, Guid? TagId,
    DateOnly? From, DateOnly? To, string? Q, string? Sort, int? Offset, int? Limit);

public static class TransactionList
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    /// Response header with the number of Transactions matching the filters (all pages).
    public const string TotalCountHeader = "X-Total-Count";

    public static readonly string[] Sorts = ["date-desc", "date-asc", "amount-asc", "amount-desc"];

    /// The filtered (not yet sorted or paged) query, or validation errors keyed by query parameter.
    public static async Task<(IQueryable<Transaction>? Query, Dictionary<string, string[]> Errors)> FilterAsync(
        TreasuryDbContext db, Guid viewerId, TransactionListRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var query = db.Transactions.VisibleTo(viewerId);

        if (request.AccountId is { } accountId) query = query.Where(t => t.AccountId == accountId);

        query = query.OwnedAs(request.Owner, viewerId, errors);

        if (request.CategoryId is { } categoryId)
        {
            if (request.Uncategorised == true)
                errors["uncategorised"] = ["Filter either by a Category or by Uncategorised."];
            var tree = await CategoryTree.LoadAsync(db);
            if (tree.Find(categoryId) is null) errors["categoryId"] = ["Choose an existing Category."];
            else
            {
                var ids = tree.SelfAndDescendants(categoryId).ToList();
                query = query.Where(t => t.CategoryId != null && ids.Contains(t.CategoryId.Value));
            }
        }
        else if (request.Uncategorised == true) query = query.Uncategorised();

        if (request.TagId is { } tagId) query = query.TaggedWith(tagId);

        if (request.From is { } from) query = query.Where(t => t.Date >= from);
        if (request.To is { } to) query = query.Where(t => t.Date <= to);
        if (request is { From: { } f, To: { } tt } && f > tt) errors["to"] = ["The end date is before the start date."];

        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var pattern = "%" + EscapeLike(request.Q.Trim()) + "%";
            query = query.Where(t => EF.Functions.ILike(t.Description, pattern, "\\")
                || (t.DisplayName != null && EF.Functions.ILike(t.DisplayName, pattern, "\\"))
                || (t.Counterparty != null && EF.Functions.ILike(t.Counterparty, pattern, "\\"))
                || (t.Reference != null && EF.Functions.ILike(t.Reference, pattern, "\\"))
                || (t.Note != null && EF.Functions.ILike(t.Note, pattern, "\\")));
        }

        if (request.Sort is { } sort && !Sorts.Contains(sort)) errors["sort"] = [$"Use one of: {string.Join(", ", Sorts)}."];
        if (request.Offset is < 0) errors["offset"] = ["Use 0 or more."];
        if (request.Limit is < 1 or > MaxLimit) errors["limit"] = [$"Use 1 to {MaxLimit}."];

        return (errors.Count > 0 ? null : query, errors);
    }

    /// The `owner` filter value meaning the viewer.
    public const string Me = "me";

    /// The `owner` filter shared by the list and the Uncategorised queue: "me" (the viewer), "family", or a Member id,
    /// matched against the Transaction's Owner. Blank = no filter; anything else adds an `owner` error.
    public static IQueryable<Transaction> OwnedAs(this IQueryable<Transaction> query, string? owner, Guid viewerId,
        Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(owner)) return query;
        var value = owner.Trim();
        if (value.Equals(Me, StringComparison.OrdinalIgnoreCase)) return query.Where(t => t.OwnerMemberId == viewerId);
        if (value.Equals(OwnerKinds.Family, StringComparison.OrdinalIgnoreCase)) return query.Where(t => t.OwnerMemberId == null);
        if (Guid.TryParse(value, out var memberId)) return query.Where(t => t.OwnerMemberId == memberId);
        errors["owner"] = ["Use \"me\", \"family\" or a Member id."];
        return query;
    }

    /// Sorted (ties: newest created first, then id, so pages are stable) and paged.
    public static IQueryable<Transaction> SortAndPage(IQueryable<Transaction> query, TransactionListRequest request)
    {
        var sorted = request.Sort switch
        {
            "date-asc" => query.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id),
            "amount-asc" => query.OrderBy(t => t.Amount).ThenByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
            "amount-desc" => query.OrderByDescending(t => t.Amount).ThenByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
            _ => query.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id),
        };
        return sorted.Skip(request.Offset ?? 0).Take(request.Limit ?? DefaultLimit);
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
