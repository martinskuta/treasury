using Treasury.Api.Data;
using Treasury.Api.Integrations;
using Treasury.Api.Ownership;

namespace Treasury.Api.Accounts;

/// Request checks shared by creating (and renaming) Accounts and Multi-currency accounts. Each adds its message
/// to <c>errors</c> under the request field's name.
internal static class AccountValidation
{
    public static void Name(string? name, int maxLength, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrEmpty(name)) errors["name"] = ["Enter a name."];
        else if (name.Length > maxLength) errors["name"] = [$"Use at most {maxLength} characters."];
    }

    public static Owner? Owner(OwnerRequest? request, Dictionary<string, string[]> errors)
    {
        var owner = request?.ToOwner();
        if (owner is null) errors["owner"] = ["Choose yourself or the Family as the Owner."];
        return owner;
    }

    /// One of <see cref="Currencies.All"/>, in the request field <paramref name="field"/>.
    public static void Currency(string? currency, string field, Dictionary<string, string[]> errors)
    {
        if (!Currencies.IsSupported(currency))
            errors[field] = [
                "Choose a currency Treasury has exchange rates for (an ISO code such as CHF, EUR, USD or GBP)."];
    }

    /// The Identifiers of an Account or a Multi-currency account: trimmed, blank ones dropped, each once (after
    /// <see cref="AccountIdentifiers.Normalise"/>). Allowed only when the Integration can provide one to check
    /// against.
    public static List<string> Identifiers(IEnumerable<string?>? identifiers, IIntegration? integration,
        Dictionary<string, string[]> errors)
    {
        var trimmed = (identifiers ?? []).Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i!.Trim())
            .DistinctBy(AccountIdentifiers.Normalise).ToList();
        if (trimmed.Count > 0 && integration is { CanProvideIdentifier: false })
            errors["identifiers"] = ["This Integration can't check an Identifier; leave them empty."];
        else if (trimmed.FirstOrDefault(i => i.Length > AccountEndpoints.MaxIdentifierLength) is { } tooLong)
            errors["identifiers"] = [$"Use at most {AccountEndpoints.MaxIdentifierLength} characters ({tooLong})."];
        return trimmed;
    }

    /// Each Identifier belongs to one account only (ADR-0002): none of <paramref name="identifiers"/> may be one of
    /// another account than <paramref name="accountId"/> (null for a new one).
    public static async Task UniqueIdentifiersAsync(TreasuryDbContext db, IReadOnlyList<string> identifiers,
        Guid? accountId, Guid viewer, Dictionary<string, string[]> errors)
    {
        if (identifiers.Count == 0 || errors.ContainsKey("identifiers")) return;
        var taken = await TakenIdentifiers.LoadAsync(db, accountId);
        var clashes = identifiers
            .Select(i => (Identifier: i, Holder: taken.HolderOf(i)))
            .Where(c => c.Holder is not null)
            .Select(c => $"{c.Identifier} is already an Identifier of " +
                $"{TakenIdentifiers.Describe(c.Holder!, owner => owner.IsVisibleTo(viewer))}.")
            .ToArray();
        if (clashes.Length > 0) errors["identifiers"] = clashes;
    }

    public static void Opening(decimal? balance, DateOnly? date, Dictionary<string, string[]> errors)
    {
        if (balance is null) errors["openingBalance"] = ["Enter the opening balance."];
        if (date is null) errors["openingDate"] = ["Enter the date of the opening balance."];
    }
}
