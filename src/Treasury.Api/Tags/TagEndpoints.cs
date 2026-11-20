using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Tags;

public static class TagEndpoints
{
    public const int MaxNameLength = 100;

    /// Create or replace a Tag. DateFrom/DateTo: both or neither (inclusive). Owners: the Owner limit (only with a
    /// date range); null or empty = every Owner.
    public record TagRequest(string? Name, DateOnly? DateFrom, DateOnly? DateTo, OwnerRequest[]? Owners);

    public record TagResponse(Guid Id, string Name, DateOnly? DateFrom, DateOnly? DateTo, OwnerResponse[] Owners);

    /// /api/tags: the Family's shared Tags. Every Member may create, change and delete them.
    public static void MapTags(this RouteGroupBuilder api)
    {
        var tags = api.MapGroup("/tags");

        tags.MapGet("/", async (TreasuryDbContext db) =>
        {
            var list = await db.Tags.OrderBy(t => t.Name).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, list));
        });

        tags.MapPost("/", async (TagRequest request, TreasuryDbContext db) =>
        {
            var tag = new Tag { Name = "" };
            var errors = await ApplyRequestAsync(db, tag, request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            db.Tags.Add(tag);
            await Tagging.ApplyDateRangeAsync(db, tag);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tags/{tag.Id}", (await ToResponsesAsync(db, [tag]))[0]);
        });

        // Replaces name, date range and Owner limit (a missing range or limit clears it).
        tags.MapPut("/{id:guid}", async (Guid id, TagRequest request, TreasuryDbContext db) =>
        {
            var tag = await db.Tags.FindAsync(id);
            if (tag is null) return Results.NotFound();
            var errors = await ApplyRequestAsync(db, tag, request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            await Tagging.ApplyDateRangeAsync(db, tag);
            await db.SaveChangesAsync();
            return Results.Ok((await ToResponsesAsync(db, [tag]))[0]);
        });

        // Takes the Tag off every Transaction.
        tags.MapDelete("/{id:guid}", async (Guid id, TreasuryDbContext db) =>
            await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync() == 0 ? Results.NotFound() : Results.NoContent());
    }

    /// Validates the request and copies it onto the Tag; returns the validation errors (camelCase keys).
    private static async Task<Dictionary<string, string[]>> ApplyRequestAsync(TreasuryDbContext db, Tag tag, TagRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Enter a name."];
        else if (name.Length > MaxNameLength) errors["name"] = [$"Use at most {MaxNameLength} characters."];
        else if (await db.Tags.AnyAsync(t => t.Id != tag.Id && t.Name.ToLower() == name.ToLower()))
            errors["name"] = ["Another Tag already has this name."];

        if (request.DateFrom is null != request.DateTo is null)
            errors[request.DateFrom is null ? "dateFrom" : "dateTo"] = ["Enter both ends of the date range, or neither."];
        else if (request.DateFrom > request.DateTo) errors["dateTo"] = ["The range must end on or after its first day."];

        var owners = new List<Owner>();
        foreach (var requested in request.Owners ?? [])
        {
            if (requested.ToOwner() is not { } owner
                || (owner.MemberId is { } memberId && !await db.Users.AnyAsync(m => m.Id == memberId)))
            {
                errors["owners"] = ["Choose the Family or existing Members."];
                break;
            }
            if (!owners.Contains(owner)) owners.Add(owner);
        }
        if (owners.Count > 0 && request.DateFrom is null && !errors.ContainsKey("owners"))
            errors["owners"] = ["An Owner limit needs a date range."];

        if (errors.Count > 0) return errors;
        tag.Name = name!;
        tag.DateFrom = request.DateFrom;
        tag.DateTo = request.DateTo;
        tag.LimitToFamily = owners.Contains(Owner.Family);
        tag.LimitToMemberIds = owners.Where(o => !o.IsFamily).Select(o => o.MemberId!.Value).ToArray();
        return errors;
    }

    private static async Task<List<TagResponse>> ToResponsesAsync(TreasuryDbContext db, IReadOnlyList<Tag> tags)
    {
        var names = await db.MemberNamesAsync(tags.SelectMany(t => t.LimitToMemberIds).Select(id => (Guid?)id));
        return tags.Select(t => new TagResponse(t.Id, t.Name, t.DateFrom, t.DateTo,
            t.LimitedToOwners.Select(o => OwnerResponse.From(o, names)).ToArray())).ToList();
    }
}
