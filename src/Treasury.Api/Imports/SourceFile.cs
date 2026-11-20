using Treasury.Api.Accounts;

namespace Treasury.Api.Imports;

/// A statement file that was imported into an Account and is kept (GLOSSARY: Source file). Every Transaction
/// imported from it links back to it; deleting it undoes the import (#10, cascade to its Transactions).
public class SourceFile
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid AccountId { get; init; }

    public Account Account { get; init; } = null!;

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
}
