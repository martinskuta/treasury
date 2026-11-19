using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;

namespace Treasury.Api.Accounts;

public static class AccountEndpoints
{
    public const int MaxNameLength = 100;
    public const int MaxIdentifierLength = 64;

    public record CreateAccountRequest(string? Name, OwnerRequest? Owner, string? IntegrationKey, string? Currency,
        decimal? OpeningBalance, DateOnly? OpeningDate, string? Identifier);

    public record RenameAccountRequest(string? Name);

    public record AccountResponse(Guid Id, string Name, string Slug, OwnerResponse Owner, string IntegrationKey,
        string Currency, decimal OpeningBalance, DateOnly OpeningDate, string? Identifier, decimal Balance);

    public static void AddAccounts(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IntegrationRegistry>();
        builder.Services.AddSingleton<IngestFolders>();
    }

    /// /api/accounts: the Accounts the current Member can see (own + Family's), creating and renaming them.
    public static void MapAccounts(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts");

        accounts.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var viewer = ViewerId(user);
            var list = await db.Accounts.OwnedByViewerOrFamily(viewer).OrderBy(a => a.Name).ToListAsync();
            var names = await MemberNamesAsync(db, list);
            return Results.Ok(list.Select(a => ToResponse(a, names)));
        });

        accounts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var account = await FindVisibleAsync(db, id, ViewerId(user));
            return account is null ? Results.NotFound() : Results.Ok(ToResponse(account, await MemberNamesAsync(db, [account])));
        });

        accounts.MapPost("/", async (CreateAccountRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            IntegrationRegistry integrations, IngestFolders folders) =>
        {
            var viewer = ViewerId(user);
            var errors = new Dictionary<string, string[]>();

            var name = request.Name?.Trim();
            ValidateName(name, errors);

            var owner = request.Owner?.ToOwner();
            if (owner is null) errors["owner"] = ["Choose yourself or the Family as the Owner."];

            var integration = integrations.Find(request.IntegrationKey);
            if (integration is null) errors["integrationKey"] = ["Choose an Integration."];

            if (request.Currency is null || !Currencies.All.Contains(request.Currency))
                errors["currency"] = [$"Choose one of {string.Join(", ", Currencies.All)}."];

            if (request.OpeningBalance is null) errors["openingBalance"] = ["Enter the opening balance."];
            if (request.OpeningDate is null) errors["openingDate"] = ["Enter the date of the opening balance."];

            var identifier = string.IsNullOrWhiteSpace(request.Identifier) ? null : request.Identifier.Trim();
            if (identifier is not null && integration is { CanProvideIdentifier: false })
                errors["identifier"] = ["This Integration can't check an identifier; leave it empty."];
            else if (identifier?.Length > MaxIdentifierLength)
                errors["identifier"] = [$"Use at most {MaxIdentifierLength} characters."];

            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!owner!.Value.IsManageableBy(viewer)) return Results.Forbid();

            var slug = await UniqueSlugAsync(db, folders, IngestFolders.Slugify(name!));
            var account = new Account
            {
                Name = name!,
                Slug = slug,
                Owner = owner.Value,
                IntegrationKey = integration!.Key,
                Currency = request.Currency!,
                OpeningBalance = request.OpeningBalance!.Value,
                OpeningDate = request.OpeningDate!.Value,
                Identifier = identifier,
            };
            folders.Create(slug);
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            return Results.Created($"/api/accounts/{account.Id}", ToResponse(account, await MemberNamesAsync(db, [account])));
        });

        accounts.MapPatch("/{id:guid}", async (Guid id, RenameAccountRequest request, ClaimsPrincipal user,
            TreasuryDbContext db) =>
        {
            var viewer = ViewerId(user);
            // Another Member's personal Account is invisible, so it is a 404 rather than a 403.
            var account = await FindVisibleAsync(db, id, viewer);
            if (account is null) return Results.NotFound();
            if (!account.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim();
            ValidateName(name, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            account.Name = name!;
            await db.SaveChangesAsync();
            return Results.Ok(ToResponse(account, await MemberNamesAsync(db, [account])));
        });

        api.MapGet("/integrations", (IntegrationRegistry integrations) =>
            Results.Ok(integrations.All.OrderBy(i => i.Name).Select(i => new { i.Key, i.Name, i.CanProvideIdentifier })));
    }

    private static Guid ViewerId(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Endpoint requires a logged-in Member."));

    private static Task<Account?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.Accounts.OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(a => a.Id == id);

    private static void ValidateName(string? name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Enter a name."];
        else if (name.Length > MaxNameLength) errors["name"] = [$"Use at most {MaxNameLength} characters."];
    }

    /// The slug, or slug-2, slug-3... if an Account or a leftover folder already uses it.
    private static async Task<string> UniqueSlugAsync(TreasuryDbContext db, IngestFolders folders, string slug)
    {
        var taken = (await db.Accounts.Where(a => a.Slug == slug || a.Slug.StartsWith(slug + "-"))
            .Select(a => a.Slug).ToListAsync()).ToHashSet();
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate) || folders.Exists(candidate); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    private static async Task<Dictionary<Guid, string>> MemberNamesAsync(TreasuryDbContext db, IEnumerable<Account> accounts)
    {
        var ids = accounts.Select(a => a.OwnerMemberId).OfType<Guid>().Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Users.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.UserName!);
    }

    private static AccountResponse ToResponse(Account a, IReadOnlyDictionary<Guid, string> memberNames) =>
        new(a.Id, a.Name, a.Slug, OwnerResponse.From(a.Owner, memberNames), a.IntegrationKey, a.Currency,
            a.OpeningBalance, a.OpeningDate, a.Identifier,
            Balance: a.OpeningBalance); // Transactions are added from #6/#7 on
}
