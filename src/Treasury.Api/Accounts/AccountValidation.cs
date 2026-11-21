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
        if (currency is null || !Currencies.All.Contains(currency))
            errors[field] = [$"Choose one of {string.Join(", ", Currencies.All)}."];
    }

    public static void Opening(decimal? balance, DateOnly? date, Dictionary<string, string[]> errors)
    {
        if (balance is null) errors["openingBalance"] = ["Enter the opening balance."];
        if (date is null) errors["openingDate"] = ["Enter the date of the opening balance."];
    }
}
