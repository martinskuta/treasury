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

    /// The optional identifier of an Account or a Multi-currency account: trimmed, blank = none. Allowed only when
    /// the Integration can provide one to check against.
    public static string? Identifier(string? identifier, IIntegration? integration,
        Dictionary<string, string[]> errors)
    {
        var trimmed = string.IsNullOrWhiteSpace(identifier) ? null : identifier.Trim();
        if (trimmed is not null && integration is { CanProvideIdentifier: false })
            errors["identifier"] = ["This Integration can't check an identifier; leave it empty."];
        else if (trimmed?.Length > AccountEndpoints.MaxIdentifierLength)
            errors["identifier"] = [$"Use at most {AccountEndpoints.MaxIdentifierLength} characters."];
        return trimmed;
    }

    public static void Opening(decimal? balance, DateOnly? date, Dictionary<string, string[]> errors)
    {
        if (balance is null) errors["openingBalance"] = ["Enter the opening balance."];
        if (date is null) errors["openingDate"] = ["Enter the date of the opening balance."];
    }
}
