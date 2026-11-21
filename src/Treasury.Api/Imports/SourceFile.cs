using Treasury.Api.Accounts;

namespace Treasury.Api.Imports;

/// A statement file that was imported and is kept (GLOSSARY: Source file): into one Account, or into a
/// Multi-currency account (#25, then its Transactions sit in that Multi-currency account's Accounts of their
/// currencies). Exactly one of <see cref="AccountId"/> and <see cref="MultiCurrencyAccountId"/> is set. Every Transaction imported from it
/// links back to it; deleting it undoes the import (#10, cascade to its Transactions).
public class SourceFile
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// The Account it was imported into; null for a file imported into a Multi-currency account.
    public Guid? AccountId { get; init; }

    public Account? Account { get; init; }

    /// The Multi-currency account it was imported into (#25); null for a file imported into one Account.
    public Guid? MultiCurrencyAccountId { get; init; }

    public MultiCurrencyAccount? MultiCurrencyAccount { get; init; }

    /// Id of what it was imported into: <see cref="AccountId"/> or <see cref="MultiCurrencyAccountId"/>.
    public Guid TargetId => (AccountId ?? MultiCurrencyAccountId)!.Value;

    /// What it was imported into (load <see cref="Account"/> and <see cref="MultiCurrencyAccount"/> first).
    public IImportTarget Target => (IImportTarget?)Account ?? MultiCurrencyAccount!;

    /// The file's original name, as uploaded or dropped (no folder).
    public required string FileName { get; init; }

    /// Where the kept copy lives, relative to the data root's source-files/ folder.
    public required string StoredPath { get; init; }

    public long Size { get; init; }

    /// SHA-256 of the content (lower-case hex), e.g. to recognise a file dropped twice.
    public required string ContentHash { get; init; }

    public DateTimeOffset ImportedAt { get; init; }

    /// The Member who uploaded it; null when it came from the Ingest folder.
    public Guid? ImportedByMemberId { get; init; }

    /// The statement's closing balance date, when the format has one (#11). Kept so the Account's balance warning
    /// is computed on read and clears by itself once the balances match again. Single-Account files only; a
    /// Multi-currency account's file keeps one per currency in <see cref="CurrencyClosingBalances"/>.
    public DateOnly? ClosingBalanceDate { get; set; }

    /// The statement's closing balance (Account currency), together with <see cref="ClosingBalanceDate"/>.
    public decimal? ClosingBalanceAmount { get; set; }

    /// For a file imported into a Multi-currency account: the statement's closing balance of each currency, each
    /// kept for (and checked against) the Multi-currency account's Account in that currency (#25).
    public List<SourceFileClosingBalance> CurrencyClosingBalances { get; init; } = [];
}

/// One currency's closing balance on a Multi-currency account's Source file, for its Account in that currency
/// (whose balance warning it feeds, see <see cref="AccountBalances"/>).
public class SourceFileClosingBalance
{
    public Guid SourceFileId { get; init; }

    public Guid AccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }
}
