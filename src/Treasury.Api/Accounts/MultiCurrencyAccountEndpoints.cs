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
        var groups = api.MapGroup("/multi-currency-accounts");

        groups.MapGet("/", async (ClaimsPrincipal user, TreasuryDbContext db, ChfConverter converter,
            TimeProvider clock) =>
        {
            var list = await db.MultiCurrencyAccounts.Include(g => g.Accounts)
                .OwnedByViewerOrFamily(user.MemberId()).OrderBy(g => g.Name).ToListAsync();
            return Results.Ok(await ToResponsesAsync(db, converter, clock, list));
        });

        groups.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, TreasuryDbContext db,
            ChfConverter converter, TimeProvider clock) =>
        {
            var group = await FindVisibleAsync(db, id, user.MemberId());
            return group is null
                ? Results.NotFound()
                : Results.Ok((await ToResponsesAsync(db, converter, clock, [group])).Single());
        });

        groups.MapPost("/", async (CreateMultiCurrencyAccountRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, IntegrationRegistry integrations, IngestFolders folders, ChfConverter converter,
            TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var errors = new Dictionary<string, string[]>();

            var name = request.Name?.Trim();
            ValidateName(name, errors, MaxNameLength);

            var owner = request.Owner?.ToOwner();
            if (owner is null) errors["owner"] = ["Choose yourself or the Family as the Owner."];

            var integration = integrations.Find(request.IntegrationKey);
            if (integration is null) errors["integrationKey"] = ["Choose an Integration."];
            else if (!integration.HoldsSeveralCurrencies)
                errors["integrationKey"] = ["This Integration holds one currency; create a plain Account for it."];

            if (request.MainCurrency is null || !Currencies.All.Contains(request.MainCurrency))
                errors["mainCurrency"] = [$"Choose one of {string.Join(", ", Currencies.All)}."];

            if (request.OpeningBalance is null) errors["openingBalance"] = ["Enter the opening balance."];
            if (request.OpeningDate is null) errors["openingDate"] = ["Enter the date of the opening balance."];

            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (!owner!.Value.IsManageableBy(viewer)) return Results.Forbid();

            var slug = await UniqueSlugAsync(db, folders, IngestFolders.Slugify(name!));
            var accountName = $"{name} {request.MainCurrency}";
            var group = new MultiCurrencyAccount
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
                        Slug = await UniqueSlugAsync(db, folders, IngestFolders.Slugify(accountName), slug),
                        IntegrationKey = integration.Key,
                        Currency = request.MainCurrency!,
                        OpeningBalance = request.OpeningBalance!.Value,
                        OpeningDate = request.OpeningDate!.Value,
                    },
                },
            };
            group.SetOwner(owner.Value);
            folders.Create(slug);
            db.MultiCurrencyAccounts.Add(group);
            await db.SaveChangesAsync();

            return Results.Created($"/api/multi-currency-accounts/{group.Id}",
                (await ToResponsesAsync(db, converter, clock, [group])).Single());
        });

        groups.MapPatch("/{id:guid}", async (Guid id, RenameMultiCurrencyAccountRequest request, ClaimsPrincipal user,
            TreasuryDbContext db, ChfConverter converter, TimeProvider clock) =>
        {
            var viewer = user.MemberId();
            var group = await FindVisibleAsync(db, id, viewer);
            if (group is null) return Results.NotFound();
            if (!group.Owner.IsManageableBy(viewer)) return Results.Forbid();

            var errors = new Dictionary<string, string[]>();
            var name = request.Name?.Trim();
            ValidateName(name, errors, MaxNameLength);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            group.Name = name!;
            await db.SaveChangesAsync();
            return Results.Ok((await ToResponsesAsync(db, converter, clock, [group])).Single());
        });
    }

    private static Task<MultiCurrencyAccount?> FindVisibleAsync(TreasuryDbContext db, Guid id, Guid viewer) =>
        db.MultiCurrencyAccounts.Include(g => g.Accounts).OwnedByViewerOrFamily(viewer)
            .SingleOrDefaultAsync(g => g.Id == id);

    private static async Task<List<MultiCurrencyAccountResponse>> ToResponsesAsync(TreasuryDbContext db,
        ChfConverter converter, TimeProvider clock, IReadOnlyCollection<MultiCurrencyAccount> groups)
    {
        var accounts = groups.SelectMany(g => g.Accounts).ToList();
        var names = await db.MemberNamesAsync(groups.Select(g => g.OwnerMemberId));
        var balances = await AccountBalances.CurrentAsync(db, accounts);
        var mismatches = await AccountBalances.MismatchesAsync(db, accounts);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rates = await converter.LoadAsync(today, today);

        return groups.Select(g =>
        {
            var members = g.Accounts.OrderBy(a => a.Currency != g.MainCurrency).ThenBy(a => a.Name)
                .Select(a => ToResponse(a, names, balances, mismatches)).ToList();
            var converted = members.Select(a => rates.Convert(a.Balance, a.Currency, g.MainCurrency, today)).ToList();
            decimal? total = converted.Any(c => c is null) ? null : converted.Sum(c => c!.Value);
            return new MultiCurrencyAccountResponse(g.Id, g.Name, g.Slug, OwnerResponse.From(g.Owner, names),
                g.IntegrationKey, g.MainCurrency, members, total);
        }).ToList();
    }
}
