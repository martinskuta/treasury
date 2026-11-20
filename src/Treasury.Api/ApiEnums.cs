using System.Text.Json;

namespace Treasury.Api;

/// THE mapping between enums and their API strings: the member name in camelCase (TransactionType.Income ->
/// "income", TransactionTagSource.DateRange -> "dateRange", RuleField.Counterparty -> "counterparty"). Use it for
/// every enum that appears in a request or response instead of hand-written switches.
public static class ApiEnums
{
    public static string ToApi<T>(this T value) where T : struct, Enum =>
        JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// The enum member whose API string is <paramref name="value"/> (ignoring case and surrounding spaces), or null.
    public static T? Parse<T>(string? value) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        foreach (var member in Enum.GetValues<T>())
            if (member.ToApi().Equals(trimmed, StringComparison.OrdinalIgnoreCase)) return member;
        return null;
    }

    /// The API strings of the flags set in a [Flags] value (zero members excluded), in declaration order.
    public static string[] FlagNames<T>(T flags) where T : struct, Enum =>
        Enum.GetValues<T>()
            .Where(f => Convert.ToInt64(f, System.Globalization.CultureInfo.InvariantCulture) != 0 && flags.HasFlag(f))
            .Select(f => f.ToApi())
            .ToArray();
}
