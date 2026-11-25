using System.Globalization;
using Treasury.Api.Accounts;
using Treasury.Api.Data;
using Treasury.Api.Integrations;

namespace Treasury.Api.Imports;

public static class StatementChecks
{
    /// The #11 safety check run on every import's legs: closing balance (Verify stage). The Identifier check runs
    /// on the whole statement, before it is split into legs (<see cref="IdentifierCheck"/>).
    public static void AddStatementChecks(this WebApplicationBuilder builder) =>
        builder.Services.AddScoped<IImportStep, ClosingBalanceCheck>();
}

/// The wrong-folder protection and the learning of Identifiers (GLOSSARY: Identifier, ADR-0002). A statement is
/// accepted when it shows no Identifier, when the target has none yet, or when one it shows matches one of the
/// target's (<see cref="AccountIdentifiers.Matches"/>); the others it shows are then the target's too. Otherwise
/// nothing is imported and the file goes to failed/.
///
/// It runs on the whole statement, before a Multi-currency account's is split by currency: the Multi-currency
/// account has the Identifiers, not its Accounts, and a statement for it may have no leg at all.
public static class IdentifierCheck
{
    /// The Identifiers the statement shows that the target doesn't have yet, to add to it when the import succeeds.
    /// Throws <see cref="ImportFailedException"/> when the statement shows only Identifiers the target doesn't have,
    /// or one that belongs to another account.
    public static async Task<IReadOnlyList<string>> VerifyAsync(TreasuryDbContext db, IImportTarget target,
        ParsedStatement statement, CancellationToken ct)
    {
        var shown = statement.AccountIdentifiers.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim())
            .DistinctBy(AccountIdentifiers.Normalise).ToList();
        bool IsKnown(string identifier) => target.Identifiers.Any(known => AccountIdentifiers.Matches(known, identifier));
        if (target.Identifiers.Count > 0 && shown.Count > 0 && !shown.Any(IsKnown))
            throw new ImportFailedException(
                $"This statement is for account {string.Join(", ", shown)}, but {target.Description} has the " +
                $"Identifiers {string.Join(", ", target.Identifiers)}. Was it dropped into the wrong folder? If the " +
                "bank gave the account a new IBAN or card, add it to its Identifiers and drop the file again. " +
                "Nothing was imported.");

        var learned = shown.Where(i => !IsKnown(i)).ToList();
        if (learned.Count == 0) return [];
        var taken = await TakenIdentifiers.LoadAsync(db, target.Id, ct);
        foreach (var identifier in learned)
            if (taken.HolderOf(identifier) is { } holder)
                throw new ImportFailedException(
                    $"This statement shows the Identifier {identifier}, which belongs to " +
                    $"{TakenIdentifiers.Describe(holder, owner => owner == target.Owner)}. Was it dropped into the " +
                    "wrong folder? Nothing was imported.");
        return learned;
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
