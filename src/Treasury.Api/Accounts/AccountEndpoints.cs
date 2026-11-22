using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
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

    /// <param name="Reach">The date the Account's data reaches: the later of its latest Transaction date and its
    /// statements' latest closing balance date; null when it has neither (#37).</param>
    /// <param name="LastImportedAt">When its most recent Source file was imported (its Multi-currency account's, for
    /// an Account in one); null when there is none.</param>
    /// <param name="Lagging">The reach is before the last day of the last complete calendar month, or missing.</param>
    public record AccountResponse(Guid Id, string Name, string Slug, OwnerResponse Owner, string IntegrationKey,
        string Currency, decimal OpeningBalance, DateOnly OpeningDate, string? Identifier, decimal Balance,
        BalanceWarningResponse? BalanceWarning, Guid? MultiCurrencyAccountId, decimal? BalanceChf, DateOnly? Reach,
        DateTimeOffset? LastImportedAt, bool Lagging);

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

        accounts.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter converter,
            TimeProvider clock) =>
        {
            var viewer = ViewerId(user);
            var list = await db.Accounts.OwnedByViewerOrFamily(viewer).OrderBy(a => a.Name).ToListAsync();
            var names = await MemberNamesAsync(db, list);
            var balances = await AccountBalances.CurrentAsync(db, list);
            var mismatches = await AccountBalances.MismatchesAsync(db, list);
            var rates = await TodaysRates.LoadAsync(converter, clock);
            var reach = await AccountReach.LoadAsync(db, clock, list);
            return Results.Ok(list.Select(a => ToResponse(a, names, balances, mismatches, rates, reach)));
        });

        accounts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter converter, TimeProvider clock) =>
        {
            var account = await FindVisibleAsync(db, id, ViewerId(user));
            return account is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(db, converter, clock, account));
        });

        accounts.MapPost("/", async (CreateAccountRequest request, ClaimsPrincipal user, TreasuryDbContext db,
            IntegrationRegistry integrations, IngestFolders folders, ChfConverter converter, TimeProvider clock) =>
        {
            var viewer = ViewerId(user);
            var errors = new Dictionary<string, string[]>();

            var name = request.Name?.Trim();
            AccountValidation.Name(name, MaxNameLength, errors);
            var owner = AccountValidation.Owner(request.Owner, errors);

            var integration = integrations.Find(request.IntegrationKey);
            if (integration is null) errors["integrationKey"] = ["Choose an Integration."];
            else if (integration.HoldsSeveralCurrencies)
                errors["integrationKey"] = ["This Integration holds several currencies; create a Multi-currency account for it."];

            AccountValidation.Currency(request.Currency, "currency", errors);
            AccountValidation.Opening(request.OpeningBalance, request.OpeningDate, errors);

            var identifier = AccountValidation.Identifier(request.Identifier, integration, errors);

            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!owner!.Value.IsManageableBy(viewer)) return Results.Forbid();

            var slug = await folders.UniqueSlugAsync(db, name!);
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

            return Results.Created($"/api/accounts/{account.Id}", await ToResponseAsync(db, converter, clock, account));
        });

        accounts.MapPatch("/{id:guid}", async (Guid id, RenameAccountRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, ChfConverter converter, TimeProvider clock) =>
        {
            var viewer = ViewerId(user);
            // Another Member's personal Account is invisible, so it is a 404 rather than a 403.
            var account = await FindVisibleAsync(db, id, viewer);
            if (account is null) return Results.NotFound();
            if (!account.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim();
            AccountValidation.Name(name, MaxNameLength, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            account.Name = name!;
            await db.SaveChangesAsync();
            return Results.Ok(await ToResponseAsync(db, converter, clock, account));
        });

        api.MapMultiCurrencyAccounts();

        // The currencies an Account can be in (Currencies.All), for the web app's currency pickers.
        api.MapGet("/currencies", () => Results.Ok(Currencies.All));

        api.MapGet("/integrations", (IntegrationRegistry integrations) =>
            Results.Ok(integrations.All.OrderBy(i => i.Name).Select(i => new { i.Key, i.Name, i.CanProvideIdentifier, i.HoldsSeveralCurrencies })));
    }

    private static Guid ViewerId(ClaimsPrincipal user) => user.MemberId();

    private static Task<Account?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.Accounts.OwnedByViewerOrFamily(viewer).SingleOrDefaultAsync(a => a.Id == id);

    private static Task<Dictionary<Guid, string>> MemberNamesAsync(TreasuryDbContext db, IEnumerable<Account> accounts) =>
        db.MemberNamesAsync(accounts.Select(a => a.OwnerMemberId));

    private static async Task<AccountResponse> ToResponseAsync(TreasuryDbContext db, ChfConverter converter,
        TimeProvider clock, Account account) =>
        ToResponse(account, await MemberNamesAsync(db, [account]), await AccountBalances.CurrentAsync(db, [account]),
            await AccountBalances.MismatchesAsync(db, [account]), await TodaysRates.LoadAsync(converter, clock),
            await AccountReach.LoadAsync(db, clock, [account]));

    internal static AccountResponse ToResponse(Account a, IReadOnlyDictionary<Guid, string> memberNames,
        IReadOnlyDictionary<Guid, decimal> balances, IReadOnlyDictionary<Guid, BalanceMismatch> mismatches,
        TodaysRates rates, AccountReach reach) =>
        new(a.Id, a.Name, a.Slug, OwnerResponse.From(a.Owner, memberNames), a.IntegrationKey, a.Currency,
            a.OpeningBalance, a.OpeningDate, a.Identifier, balances[a.Id],
            mismatches.TryGetValue(a.Id, out var m)
                ? new BalanceWarningResponse(m.Date, m.StatementBalance, m.CalculatedBalance,
                    new TransactionEndpoints.SourceFileRef(m.SourceFileId, m.FileName))
                : null,
            a.MultiCurrencyAccountId,
            rates.ToChf(balances[a.Id], a.Currency),
            reach.Of(a), reach.LastImportedAt(a), reach.IsLagging(reach.Of(a)));
}
