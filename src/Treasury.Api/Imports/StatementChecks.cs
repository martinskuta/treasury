using System.Globalization;
using Treasury.Api.Accounts;

namespace Treasury.Api.Imports;

public static class StatementChecks
{
    /// The #11 safety checks on every import: identifier (Check stage) and closing balance (Verify stage).
    public static void AddStatementChecks(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IImportStep, IdentifierCheck>();
        builder.Services.AddScoped<IImportStep, ClosingBalanceCheck>();
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

/// Keeps the statement's closing balance on its Source file (the Account's balance warning is computed from it on
/// read, see <see cref="AccountBalances"/>) and tells the importing Member right away when it doesn't match the
/// calculated balance on that date. Never rejects the file.
public sealed class ClosingBalanceCheck : IImportStep
{
    public ImportStage Stage => ImportStage.Verify;

    public async Task RunAsync(ImportContext context, CancellationToken ct)
    {
        if (context.Statement.ClosingBalance is not { } closing) return;
        context.SourceFile.ClosingBalanceDate = closing.Date;
        context.SourceFile.ClosingBalanceAmount = closing.Amount;

        var account = context.Account;
        if (closing.Date < account.OpeningDate) return; // before the opening balance: nothing to compare with
        var calculated = await AccountBalances.OnAsync(context.Db, account, closing.Date, ct);
        if (calculated != closing.Amount)
            context.Warnings.Add(
                $"The statement's closing balance on {closing.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} is " +
                $"{Format(closing.Amount)} {account.Currency}, but the Account's calculated balance on that date is " +
                $"{Format(calculated)} {account.Currency}. An import may be missing or doubled.");
    }

    /// Swiss style like the frontend's formatAmount: 1'234.50.
    private static string Format(decimal amount) =>
        amount.ToString("#,##0.00", new NumberFormatInfo { NumberGroupSeparator = "'", NumberDecimalSeparator = "." });
}
