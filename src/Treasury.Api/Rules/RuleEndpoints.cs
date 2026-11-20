using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Categories;
using Treasury.Api.Data;
using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Rules;

public static class RuleEndpoints
{
    public const int MaxTextLength = 200;

    /// Create or replace a Rule. Field "description" | "counterparty", match "contains" | "equals". At least one action
    /// (categoryId, tagIds, owner) is required; owner null = the Rule doesn't set an Owner.
    public record RuleRequest(string? Field, string? Match, string? Text, Guid? CategoryId, Guid[]? TagIds,
        OwnerRequest? Owner);

    /// Active = the author is still an active Member (a deactivated author's Rules don't run until someone edits them).
    public record RuleResponse(Guid Id, int Priority, string Field, string Match, string Text,
        CategoryRef? Category, RuleTagRef[] Tags, OwnerResponse? Owner, AuthorRef Author, bool Active);

    public record RuleTagRef(Guid Id, string Name);

    public record AuthorRef(Guid Id, string Name);

    public record ReorderRequest(Guid[]? RuleIds);

    /// A pre-filled Rule built from a Transaction (not saved): same fields as <see cref="RuleRequest"/>.
    public record RuleDraft(string Field, string Match, string Text, Guid? CategoryId, Guid[] TagIds, OwnerRequest? Owner);

    /// /api/rules: the Family's shared Rules, in priority order. Every Member may create, change, reorder and delete
    /// them; saving a Rule makes the saving Member its author (which decides the Accounts it can reach).
    public static void MapRules(this RouteGroupBuilder api)
    {
        var rules = api.MapGroup("/rules");

        rules.MapGet("/", async (TreasuryDbContext db) => Results.Ok(await ToResponsesAsync(db, await LoadAllAsync(db))));

        rules.MapPost("/", async (RuleRequest request, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var rule = new Rule { Text = "" };
            var errors = await ApplyRequestAsync(db, rule, request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            rule.AuthorMemberId = user.MemberId();
            rule.Priority = await db.Rules.AnyAsync() ? await db.Rules.MaxAsync(r => r.Priority) + 1 : 0;
            db.Rules.Add(rule);
            await db.SaveChangesAsync();
            return Results.Created($"/api/rules/{rule.Id}", (await ToResponsesAsync(db, [rule]))[0]);
        });

        // Replaces condition and actions (keeps the priority); the editing Member becomes the author.
        rules.MapPut("/{id:guid}", async (Guid id, RuleRequest request, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var rule = await db.Rules.Include(r => r.TagLinks).SingleOrDefaultAsync(r => r.Id == id);
            if (rule is null) return Results.NotFound();
            var errors = await ApplyRequestAsync(db, rule, request);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            rule.AuthorMemberId = user.MemberId();
            await db.SaveChangesAsync();
            return Results.Ok((await ToResponsesAsync(db, [rule]))[0]);
        });

        rules.MapDelete("/{id:guid}", async (Guid id, TreasuryDbContext db) =>
        {
            if (await db.Rules.Where(r => r.Id == id).ExecuteDeleteAsync() == 0) return Results.NotFound();
            Renumber(await db.Rules.OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync());
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // The new priority order: every Rule id exactly once, first = highest priority.
        rules.MapPut("/order", async (ReorderRequest request, TreasuryDbContext db) =>
        {
            var all = await db.Rules.ToListAsync();
            var ids = request.RuleIds ?? [];
            if (ids.Length != all.Count || ids.Distinct().Count() != ids.Length || ids.Any(id => all.All(r => r.Id != id)))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ruleIds"] = ["List every Rule exactly once (someone may have changed the Rules: reload)."],
                });
            Renumber(ids.Select(id => all.Single(r => r.Id == id)).ToList());
            await db.SaveChangesAsync();
            return Results.Ok(await ToResponsesAsync(db, await LoadAllAsync(db)));
        });

        // Runs the Rules over the caller's visible Uncategorised Transactions (e.g. after adding a Rule). Each Rule
        // still only reaches Accounts its author can see, and manual edits still win.
        rules.MapPost("/apply", async (ClaimsPrincipal user, TreasuryDbContext db, RuleEngine engine) =>
        {
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();
                var uncategorised = await db.Transactions.VisibleTo(user.MemberId()).Uncategorised().ToListAsync();
                var run = await engine.ApplyAsync(uncategorised);
                await tx.CommitAsync();
                return run;
            });
            return Results.Ok(result);
        });

        // A Rule draft from a Transaction (typically right after correcting its Category by hand): matches its
        // counterparty when it has one, else its description, and assigns its current Category.
        rules.MapGet("/suggestion", async (Guid transactionId, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var t = await db.Transactions.VisibleTo(user.MemberId()).SingleOrDefaultAsync(x => x.Id == transactionId);
            if (t is null) return Results.NotFound();
            var useCounterparty = !string.IsNullOrWhiteSpace(t.Counterparty);
            var text = (useCounterparty ? t.Counterparty! : t.Description).Trim();
            if (text.Length > MaxTextLength) text = text[..MaxTextLength];
            return Results.Ok(new RuleDraft(
                (useCounterparty ? RuleField.Counterparty : RuleField.Description).ToApi(),
                (useCounterparty ? RuleMatch.Equals : RuleMatch.Contains).ToApi(),
                text, t.CategoryId, [], null));
        });
    }

    private static Task<List<Rule>> LoadAllAsync(TreasuryDbContext db) =>
        db.Rules.Include(r => r.TagLinks).OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync();

    private static void Renumber(List<Rule> ordered)
    {
        for (var i = 0; i < ordered.Count; i++) ordered[i].Priority = i;
    }

    /// Validates the request and copies it onto the Rule; returns the validation errors (camelCase keys).
    private static async Task<Dictionary<string, string[]>> ApplyRequestAsync(TreasuryDbContext db, Rule rule, RuleRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var field = ApiEnums.Parse<RuleField>(request.Field);
        if (field is null) errors["field"] = ["Choose description or counterparty."];
        var match = ApiEnums.Parse<RuleMatch>(request.Match);
        if (match is null) errors["match"] = ["Choose contains or equals."];
        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text)) errors["text"] = ["Enter the text to look for."];
        else if (text.Length > MaxTextLength) errors["text"] = [$"Use at most {MaxTextLength} characters."];

        // Same Category / Tag / Owner rules as classifying a Transaction by hand.
        await TransactionClassification.ValidateCategoryAsync(db, request.CategoryId, errors);
        var tagIds = await TransactionClassification.ValidateTagsAsync(db, request.TagIds, "tagIds", errors);
        var owner = request.Owner is null ? null : await TransactionClassification.ResolveOwnerAsync(db, request.Owner, errors);
        if (request.CategoryId is null && tagIds.Count == 0 && request.Owner is null)
            errors["actions"] = ["Choose a Category, Tags or an Owner for the Rule to assign."];

        if (errors.Count > 0) return errors;
        rule.Field = field!.Value;
        rule.Match = match!.Value;
        rule.Text = text!;
        rule.CategoryId = request.CategoryId;
        rule.SetsOwner = owner is not null;
        rule.OwnerMemberId = owner?.MemberId;
        rule.TagLinks.RemoveAll(l => !tagIds.Contains(l.TagId));
        rule.TagLinks.AddRange(tagIds.Where(id => rule.TagLinks.All(l => l.TagId != id))
            .Select(id => new RuleTag { RuleId = rule.Id, TagId = id }));
        return errors;
    }

    private static async Task<List<RuleResponse>> ToResponsesAsync(TreasuryDbContext db, IReadOnlyList<Rule> rules)
    {
        var members = await db.MembersAsync(rules.Select(r => (Guid?)r.AuthorMemberId).Concat(rules.Select(r => r.OwnerMemberId)));
        var names = members.ToDictionary(m => m.Key, m => m.Value.Name);
        var tagIds = rules.SelectMany(r => r.TagLinks).Select(l => l.TagId).Distinct().ToList();
        var tags = await db.Tags.Where(t => tagIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);
        var tree = rules.Any(r => r.CategoryId != null) ? await CategoryTree.LoadAsync(db) : new CategoryTree([]);

        return rules.Select(r => new RuleResponse(r.Id, r.Priority,
            r.Field.ToApi(),
            r.Match.ToApi(),
            r.Text,
            tree.RefOf(r.CategoryId),
            r.TagLinks.Where(l => tags.ContainsKey(l.TagId)).Select(l => new RuleTagRef(l.TagId, tags[l.TagId]))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(),
            r.Owner is { } owner ? OwnerResponse.From(owner, names) : null,
            new AuthorRef(r.AuthorMemberId, names.GetValueOrDefault(r.AuthorMemberId, "?")),
            members.TryGetValue(r.AuthorMemberId, out var author) && author.Active)).ToList();
    }
}
