using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Accounts;

/// The Identifiers the Accounts and Multi-currency accounts hold, apart from one of them: an Identifier belongs to
/// one account only (GLOSSARY: Identifier, ADR-0002), whether an import learns it or a Member adds it.
public sealed class TakenIdentifiers
{
    private readonly IReadOnlyList<IImportTarget> _holders;

    private TakenIdentifiers(IReadOnlyList<IImportTarget> holders) => _holders = holders;

    /// The Identifiers of every account except <paramref name="exceptId"/> (the one they would be given to).
    public static async Task<TakenIdentifiers> LoadAsync(TreasuryDbContext db, Guid? exceptId, CancellationToken ct = default)
    {
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.Id != exceptId && a.Identifiers.Count > 0).ToListAsync(ct);
        var multiCurrencyAccounts = await db.MultiCurrencyAccounts.AsNoTracking()
            .Where(m => m.Id != exceptId && m.Identifiers.Count > 0).ToListAsync(ct);
        return new TakenIdentifiers([.. accounts, .. multiCurrencyAccounts]);
    }

    /// The account holding an Identifier that matches <paramref name="identifier"/> (<see cref="AccountIdentifiers.Matches"/>), if any.
    public IImportTarget? HolderOf(string identifier) =>
        _holders.FirstOrDefault(h => h.Identifiers.Any(i => AccountIdentifiers.Matches(i, identifier)));

    /// How a message names <paramref name="holder"/> to someone who sees the accounts of <paramref name="visibleTo"/>
    /// (and the Family's): another Member's personal account stays unnamed.
    public static string Describe(IImportTarget holder, Func<Owner, bool> visibleTo) =>
        holder.Owner.IsFamily || visibleTo(holder.Owner) ? holder.Description : "another Member's account";
}
