namespace Treasury.Api.Accounts;

/// How an Account's identifier is compared with the identifier a statement carries (#11).
///
/// Both sides are normalised first: whitespace and '-' are removed and letters upper-cased, so
/// "CH93 0076 2011 6238 5295 7" equals "ch9300762011623852957".
///
/// Card numbers: when BOTH sides look like a card number — only digits once spaces, '-' and the mask characters
/// '*', 'X', '•' and '.' are removed, with at least 4 digits — they match when their last 4 digits are equal.
/// So an Account identifier "1234" matches "5413 **** **** 1234" and "5413XXXXXXXX1234".
///
/// Anything else (IBANs, other account numbers) must be equal after normalisation.
public static class AccountIdentifiers
{
    public const int CardDigitsCompared = 4;

    public static bool Matches(string accountIdentifier, string statementIdentifier)
    {
        var a = Normalise(accountIdentifier);
        var s = Normalise(statementIdentifier);
        if (a == s) return true;
        return CardDigits(a) is { } aDigits && CardDigits(s) is { } sDigits
            && aDigits[^CardDigitsCompared..] == sDigits[^CardDigitsCompared..];
    }

    public static string Normalise(string identifier) =>
        string.Concat(identifier.Where(c => !char.IsWhiteSpace(c) && c != '-')).ToUpperInvariant();

    /// The digits of a (possibly masked) card number, or null when it isn't one.
    private static string? CardDigits(string normalised)
    {
        var digits = string.Concat(normalised.Where(c => c is not ('*' or 'X' or '•' or '.')));
        return digits.Length >= CardDigitsCompared && digits.All(char.IsAsciiDigit) ? digits : null;
    }
}
