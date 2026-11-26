using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;
using static Treasury.Api.Accounts.AccountEndpoints;

namespace Treasury.Api.Accounts;

/// /api/multi-currency-accounts (ADR-0009): creating, listing and editing Multi-currency accounts. Their Accounts
/// are ordinary Accounts (renamed through /api/accounts/{id}); they are never moved or added by hand.
public static class MultiCurrencyAccountEndpoints
{
    /// Room for the " CHF" suffix of the main-currency Account's name.
    public const int MaxNameLength = AccountEndpoints.MaxNameLength - 4;

    public record CreateMultiCurrencyAccountRequest(string? Name, OwnerRequest? Owner, string? IntegrationKey,
        string? MainCurrency, decimal? OpeningBalance, DateOnly? OpeningDate, IReadOnlyList<string?>? Identifiers);

    /// <param name="Identifiers">Left out (null) = keep them as they are; an empty list removes them all.</param>
    public record UpdateMultiCurrencyAccountRequest(string? Name, IReadOnlyList<string?>? Identifiers);

    /// <param name="TotalBalance">The sum of its Accounts' balances in the main currency at today's exchange rates;
    /// null while a needed rate is missing.</param>
    /// <param name="TotalBalanceChf">The same total in CHF at today's exchange rates; null while a needed rate is
    /// missing.</param>
    /// <param name="Reach">The latest reach among its Accounts; null when none of them has one (#37).</param>
    /// <param name="LastImportedAt">When its most recent Source file was imported; null when there is none.</param>
    /// <param name="Lagging">The reach is before the last day of the last complete calendar month, or missing.</param>
    public record MultiCurrencyAccountResponse(Guid Id, string Name, string Slug, OwnerResponse Owner,
        string IntegrationKey, string MainCurrency, IReadOnlyList<string> Identifiers, IReadOnlyList<AccountResponse> Accounts,
        decimal? TotalBalance, decimal? TotalBalanceChf, DateOnly? Reach, DateTimeOffset? LastImportedAt, bool Lagging);

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
            var identifiers = AccountValidation.Identifiers(request.Identifiers, integration, errors);
            await AccountValidation.UniqueIdentifiersAsync(db, identifiers, null, viewer, errors);

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
                Identifiers = identifiers,
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

        multiCurrencyAccounts.MapPatch("/{id:guid}", async (Guid id, UpdateMultiCurrencyAccountRequest request,
            ClaimsPrincipal user, TreasuryDbContext db, IntegrationRegistry integrations, ChfConverter converter,
            TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var multiCurrencyAccount = await FindVisibleAsync(db, id, viewer);
            if (multiCurrencyAccount is null) return Results.NotFound();
            if (!multiCurrencyAccount.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim();
            AccountValidation.Name(name, MaxNameLength, errors);
            var identifiers = AccountValidation.Identifiers(request.Identifiers,
                integrations.Find(multiCurrencyAccount.IntegrationKey), errors);
            await AccountValidation.UniqueIdentifiersAsync(db, identifiers, multiCurrencyAccount.Id, viewer, errors);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            multiCurrencyAccount.Name = name!;
            if (request.Identifiers is not null) multiCurrencyAccount.Identifiers = identifiers;
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
        var rates = await TodaysRates.LoadAsync(converter, clock);
        var reach = await AccountReach.LoadAsync(db, clock, allAccounts);

        return multiCurrencyAccounts.Select(m =>
        {
            var accounts = m.Accounts.OrderBy(a => a.Currency != m.MainCurrency).ThenBy(a => a.Name)
                .Select(a => ToResponse(a, names, balances, mismatches, rates, reach)).ToList();
            var converted = accounts.Select(a => rates.Convert(a.Balance, a.Currency, m.MainCurrency));
            return new MultiCurrencyAccountResponse(m.Id, m.Name, m.Slug, OwnerResponse.From(m.Owner, names),
                m.IntegrationKey, m.MainCurrency, m.Identifiers, accounts, SumOrNull(converted),
                SumOrNull(accounts.Select(a => a.BalanceChf)), reach.Of(m), reach.LastImportedAt(m),
                reach.IsLagging(m));
        }).ToList();
    }

    private static decimal? SumOrNull(IEnumerable<decimal?> amounts)
    {
        var list = amounts.ToList();
        return list.Any(a => a is null) ? null : list.Sum(a => a!.Value);
    }
}