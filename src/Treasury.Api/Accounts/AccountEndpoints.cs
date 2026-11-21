using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;
using Treasury.Api.Transactions;

namespace Treasury.Api.Accounts;

public static class AccountEndpoints
{
    public const int MaxNameLength = 100;
    public const int MaxIdentifierLength = 64;

    public record CreateAccountRequest(string? Name, OwnerRequest? Owner, string? IntegrationKey, string? Currency,
        decimal? OpeningBalance, DateOnly? OpeningDate, string? Identifier);

    public record RenameAccountRequest(string? Name);

    public record AccountResponse(Guid Id, string Name, string Slug, OwnerResponse Owner, string IntegrationKey,
        string Currency, decimal OpeningBalance, DateOnly OpeningDate, string? Identifier, decimal Balance,
        BalanceWarningResponse? BalanceWarning, Guid? MultiCurrencyAccountId);

    /// The Account's most recent statement closing balance differs from the calculated balance on that date (#11).
    public record BalanceWarningResponse(DateOnly Date, decimal StatementBalance, decimal CalculatedBalance,
        TransactionEndpoints.SourceFileRef SourceFile);

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
            var balances = await AccountBalances.CurrentAsync(db, list);
            var mismatches = await AccountBalances.MismatchesAsync(db, list);
            return Results.Ok(list.Select(a => ToResponse(a, names, balances, mismatches)));
        });

        accounts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db) =>
        {
            var account = await FindVisibleAsync(db, id, ViewerId(user));
            return account is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(db, account));
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
            else if (integration.HoldsSeveralCurrencies)
                errors["integrationKey"] = ["This Integration holds several currencies; create a Multi-currency account for it."];

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

            return Results.Created($"/api/accounts/{account.Id}", await ToResponseAsync(db, account));
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
            return Results.Ok(await ToResponseAsync(db, account));
        });

        api.MapMultiCurrencyAccounts();

        api.MapGet("/integrations", (IntegrationRegistry integrations) =>
            Results.Ok(integrations.All.OrderBy(i => i.Name).Select(i => new { i.Key, i.Name, i.CanProvideIdentifier, i.HoldsSeveralCurrencies })));
    }

    private static Guid ViewerId(ClaimsPrincipal user) => user.MemberId();

    private static Task<Account?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.Accounts.OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(a => a.Id == id);

    internal static void ValidateName(string? name, Dictionary<string, string[]> errors, int maxLength = MaxNameLength)
    {
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Enter a name."];
        else if (name.Length > maxLength) errors["name"] = [$"Use at most {maxLength} characters."];
    }

    /// The slug, or slug-2, slug-3... if an Account, a Multi-currency account, a leftover folder or one of the
    /// <paramref name="reserved"/> slugs (picked earlier in the same request) already uses it.
    internal static async Task<string> UniqueSlugAsync(TreasuryDbContext db, IngestFolders folders, string slug,
        params string[] reserved)
    {
        var taken = (await db.Accounts.Where(a => a.Slug == slug || a.Slug.StartsWith(slug + "-")).Select(a => a.Slug)
            .Concat(db.MultiCurrencyAccounts.Where(g => g.Slug == slug || g.Slug.StartsWith(slug + "-")).Select(g => g.Slug))
            .ToListAsync()).ToHashSet();
        taken.UnionWith(reserved);
        var candidate = slug;
        for (var n = 2; taken.Contains(candidate) || folders.Exists(candidate); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    internal static Task<Dictionary<Guid, string>> MemberNamesAsync(TreasuryDbContext db, IEnumerable<Account> accounts) =>
        db.MemberNamesAsync(accounts.Select(a => a.OwnerMemberId));

    private static async Task<AccountResponse> ToResponseAsync(TreasuryDbContext db, Account account) =>
        ToResponse(account, await MemberNamesAsync(db, [account]), await AccountBalances.CurrentAsync(db, [account]),
            await AccountBalances.MismatchesAsync(db, [account]));

    internal static AccountResponse ToResponse(Account a, IReadOnlyDictionary<Guid, string> memberNames,
        IReadOnlyDictionary<Guid, decimal> balances, IReadOnlyDictionary<Guid, BalanceMismatch> mismatches) =>
        new(a.Id, a.Name, a.Slug, OwnerResponse.From(a.Owner, memberNames), a.IntegrationKey, a.Currency,
            a.OpeningBalance, a.OpeningDate, a.Identifier, balances[a.Id],
            mismatches.TryGetValue(a.Id, out var m)
                ? new BalanceWarningResponse(m.Date, m.StatementBalance, m.CalculatedBalance,
                    new TransactionEndpoints.SourceFileRef(m.SourceFileId, m.FileName))
                : null,
            a.MultiCurrencyAccountId);
}
