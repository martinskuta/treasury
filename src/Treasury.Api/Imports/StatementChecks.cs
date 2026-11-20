using Treasury.Api.Accounts;

namespace Treasury.Api.Imports;

public static class StatementChecks
{
    /// The #11 safety checks on every import: identifier (Check stage) and closing balance (Verify stage).
    public static void AddStatementChecks(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IImportStep, IdentifierCheck>();
    }
}

/// Rejects a statement dropped into the wrong Account's folder: when both the Account and the statement have an
/// identifier and they don't match (<see cref="AccountIdentifiers.Matches"/>), nothing is imported and the file goes
/// to failed/. Skipped when either side has no identifier.
public sealed class IdentifierCheck : IImportStep
{
    public ImportStage Stage => ImportStage.Check;

    public Task RunAsync(ImportContext context, CancellationToken ct)
    {
        var expected = context.Account.Identifier;
        var actual = context.Statement.AccountIdentifier;
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return Task.CompletedTask;
        if (!AccountIdentifiers.Matches(expected, actual))
            throw new ImportFailedException(
                $"This statement is for account {actual.Trim()}, but the Account '{context.Account.Name}' has the " +
                $"identifier {expected}. Was it dropped into the wrong folder? Nothing was imported.");
        return Task.CompletedTask;
    }
}
