using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;
using static Treasury.Api.Accounts.AccountEndpoints;

namespace Treasury.Api.Accounts;

/// /api/multi-currency-accounts (ADR-0009): creating, listing and renaming Multi-currency accounts. Their Accounts
/// are ordinary Accounts (renamed through /api/accounts/{id}); they are never moved or added by hand.
public static class MultiCurrencyAccountEndpoints
{
    /// Room for the " CHF" suffix of the main-currency Account's name.
    public const int MaxNameLength = AccountEndpoints.MaxNameLength - 4;

    public record CreateMultiCurrencyAccountRequest(string? Name, OwnerRequest? Owner, string? IntegrationKey,
        string? MainCurrency, decimal? OpeningBalance, DateOnly? OpeningDate);

    public record RenameMultiCurrencyAccountRequest(string? Name);

    /// <param name="TotalBalance">The sum of its Accounts' balances in the main currency at today's exchange rates;
    /// null while a needed rate is missing.</param>
    public record MultiCurrencyAccountResponse(Guid Id, string Name, string Slug, OwnerResponse Owner,
        string IntegrationKey, string MainCurrency, IReadOnlyList<AccountResponse> Accounts, decimal? TotalBalance);

    public static void MapMultiCurrencyAccounts(this RouteGroupBuilder api)
    {
        var multiCurrencyAccounts = api.MapGroup("/multi-currency-accounts");

        multiCurrencyAccounts.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter converter,
            TimeProvider clock) =>
        {
            var list = await db.MultiCurrencyAccounts.Include(m => m.Accounts)
                .OwnedByViewerOrFamily(user.MemberId()).OrderBy(m => m.Name).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, converter, clock, list));
        });

        multiCurrencyAccounts.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter converter, TimeProvider clock) =>
        {
            var multiCurrencyAccount = await FindVisibleAsync(db, id, user.MemberId());
            return multiCurrencyAccount is null
                ? Results.NotFound()
                : Results.Ok((await ToResponsesAsync(db, converter, clock, [multiCurrencyAccount])).Single());
        });

        multiCurrencyAccounts.MapPost("/", async (CreateMultiCurrencyAccountRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, IntegrationRegistry integrations, IngestFolders folders, ChfConverter converter,
            TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var errors = new Dictionary<string, string[]>();

            var name = request.Name?.Trim();
            AccountValidation.Name(name, MaxNameLength, errors);
            var owner = AccountValidation.Owner(request.Owner, errors);

            var integration = integrations.Find(request.IntegrationKey);
            if (integration is null) errors["integrationKey"] = ["Choose an Integration."];
            else if (!integration.HoldsSeveralCurrencies)
                errors["integrationKey"] = ["This Integration holds one currency; create a plain Account for it."];

            AccountValidation.Currency(request.MainCurrency, "mainCurrency", errors);
            AccountValidation.Opening(request.OpeningBalance, request.OpeningDate, errors);

            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!owner!.Value.IsManageableBy(viewer)) return Results.Forbid();

            var slug = await folders.UniqueSlugAsync(db, name!);
            var accountName = $"{name} {request.MainCurrency}";
            var multiCurrencyAccount = new MultiCurrencyAccount
            {
                Name = name!,
                Slug = slug,
                IntegrationKey = integration!.Key,
                MainCurrency = request.MainCurrency!,
                Accounts =
                {
                    new Account
                    {
                        Name = accountName,
                        // Reserved for the Account's kept Source files; no Ingest folder is created for it.
                        Slug = await folders.UniqueSlugAsync(db, accountName, [slug]),
                        IntegrationKey = integration.Key,
                        Currency = request.MainCurrency!,
                        OpeningBalance = request.OpeningBalance!.Value,
                        OpeningDate = request.OpeningDate!.Value,
                    },
                },
            };
            multiCurrencyAccount.SetOwner(owner.Value);
            folders.Create(slug);
            db.MultiCurrencyAccounts.Add(multiCurrencyAccount);
            await db.SaveChangesAsync();

            return Results.Created($"/api/multi-currency-accounts/{multiCurrencyAccount.Id}",
                (await ToResponsesAsync(db, converter, clock, [multiCurrencyAccount])).Single());
        });

        multiCurrencyAccounts.MapPatch("/{id:guid}", async (Guid id, RenameMultiCurrencyAccountRequest request,
            ClaimsPrincipal user, TreasuryDbContext db, ChfConverter converter, TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var multiCurrencyAccount = await FindVisibleAsync(db, id, viewer);
            if (multiCurrencyAccount is null) return Results.NotFound();
            if (!multiCurrencyAccount.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim();
            AccountValidation.Name(name, MaxNameLength, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            multiCurrencyAccount.Name = name!;
            await db.SaveChangesAsync();
            return Results.Ok((await ToResponsesAsync(db, converter, clock, [multiCurrencyAccount])).Single());
        });
    }

    private static Task<MultiCurrencyAccount?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.MultiCurrencyAccounts.Include(m => m.Accounts).OwnedByViewerOrFamily(viewer)
            .SingleOrDefaultAsync(m => m.Id == id);

    private static async Task<List<MultiCurrencyAccountResponse>> ToResponsesAsync(TreasuryDbContext db,
        ChfConverter converter, TimeProvider clock, IReadOnlyCollection<MultiCurrencyAccount> multiCurrencyAccounts)
    {
        var allAccounts = multiCurrencyAccounts.SelectMany(m => m.Accounts).ToList();
        var names = await db.MemberNamesAsync(multiCurrencyAccounts.Select(m => m.OwnerMemberId));
        var balances = await AccountBalances.CurrentAsync(db, allAccounts);
        var mismatches = await AccountBalances.MismatchesAsync(db, allAccounts);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rates = await converter.LoadAsync(today, today);

        return multiCurrencyAccounts.Select(m =>
        {
            var accounts = m.Accounts.OrderBy(a => a.Currency != m.MainCurrency).ThenBy(a => a.Name)
                .Select(a => ToResponse(a, names, balances, mismatches)).ToList();
            var converted = accounts.Select(a => rates.Convert(a.Balance, a.Currency, m.MainCurrency, today)).ToList();
            decimal? total = converted.Any(c => c is null) ? null : converted.Sum(c => c!.Value);
            return new MultiCurrencyAccountResponse(m.Id, m.Name, m.Slug, OwnerResponse.From(m.Owner, names),
                m.IntegrationKey, m.MainCurrency, accounts, total);
        }).ToList();
    }
}
