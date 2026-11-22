using System.Globalization;
using Treasury.Api.Accounts;
using Treasury.Api.Integrations;

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
///
/// A Multi-currency account has the identifier, not its Accounts (ADR-0010), and a statement for it may have no
/// leg at all, so the pipeline runs <see cref="Verify"/> on the whole statement before splitting it by currency.
public sealed class IdentifierCheck : IImportStep
{
    public ImportStage Stage => ImportStage.Check;

    public Task RunAsync(ImportContext context, CancellationToken ct)
    {
        Verify(context.Account, context.Statement);
        return Task.CompletedTask;
    }

    /// Throws <see cref="ImportFailedException"/> when the statement is for another identifier than the target's.
    public static void Verify(IImportTarget target, ParsedStatement statement)
    {
        var expected = target.Identifier;
        var actual = statement.AccountIdentifier;
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return;
        if (!AccountIdentifiers.Matches(expected, actual))
            throw new ImportFailedException(
                $"This statement is for account {actual.Trim()}, but {target.Description} has the identifier " +
                $"{expected}. Was it dropped into the wrong folder? Nothing was imported.");
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
        var account = context.Account;
        if (context.SourceFile.MultiCurrencyAccountId is null)
        {
            context.SourceFile.ClosingBalanceDate = closing.Date;
            context.SourceFile.ClosingBalanceAmount = closing.Amount;
        }
        else
        {
            // One per currency, each for the Multi-currency account's Account in that currency (#25).
            context.SourceFile.CurrencyClosingBalances.Add(new SourceFileClosingBalance
            {
                SourceFileId = context.SourceFile.Id, AccountId = account.Id, Date = closing.Date, Amount = closing.Amount,
            });
        }

        if (closing.Date < account.OpeningDate) return; // before the opening balance: nothing to compare with
        var calculated = await AccountBalances.OnAsync(context.Db, account, closing.Date, ct);
        if (calculated != closing.Amount)
            context.Warnings.Add(
                $"The statement's closing balance on {closing.Date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} is " +
                $"{Amounts.Format(closing.Amount)} {account.Currency}, but the Account's calculated balance on that date is " +
                $"{Amounts.Format(calculated)} {account.Currency}. An import may be missing or doubled.");
    }
}
