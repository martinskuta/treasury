using Treasury.Api.Auth;

namespace Treasury.Api.Members;

/// A Member's first and last name as a request sends them, checked and trimmed.
public static class MemberNames
{
    /// Trims both names (a blank last name becomes null) and checks them. Errors are keyed "firstName" / "lastName",
    /// ready for Results.ValidationProblem; null when the names are fine.
    public static Dictionary<string, string[]>? Validate(string? firstName, string? lastName,
        out string first, out string? last)
    {
        first = firstName?.Trim() ?? "";
        last = string.IsNullOrWhiteSpace(lastName) ? null : lastName.Trim();

        var errors = new Dictionary<string, string[]>();
        if (first.Length == 0) errors["firstName"] = ["Enter a first name."];
        else if (first.Length > Member.MaxNameLength)
            errors["firstName"] = [$"Keep the first name to {Member.MaxNameLength} characters."];
        if (last?.Length > Member.MaxNameLength)
            errors["lastName"] = [$"Keep the last name to {Member.MaxNameLength} characters."];
        return errors.Count == 0 ? null : errors;
    }
}
