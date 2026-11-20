using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.Imports;

/// Undoes an import (#10): deletes the Source file, every Transaction imported from it (FK cascade) and its kept
/// copy under source-files/. The copy the Member has in ingest/&lt;slug&gt;/processed/ is theirs and stays.
/// Nothing else remembers the file afterwards (no hash, no rows), so the same file can be imported again.
public sealed class ImportUndo(TreasuryDbContext db, ImportLocks locks, ImportFiles files, ILogger<ImportUndo> logger)
{
    /// Deletes the Source file. Permission checks are the caller's job.
    public async Task UndoAsync(SourceFile sourceFile, CancellationToken ct)
    {
        // Same lock as imports: an undo never interleaves with an import into the same Account.
        using var _ = await locks.AcquireAsync(sourceFile.AccountId, ct);

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // TODO(#16): break the Transfer links / matches of this file's Transactions here, in the same DB
            // transaction, before they are deleted (the counterpart Transaction in the other Account stays and must
            // become unmatched again).

            // Transactions go with it: Transaction.SourceFileId is ON DELETE CASCADE.
            await db.SourceFiles.Where(f => f.Id == sourceFile.Id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        try
        {
            files.DeleteKept(sourceFile.StoredPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(e, "Undid the import of {FileName} but couldn't delete its kept copy {StoredPath}",
                sourceFile.FileName, sourceFile.StoredPath);
        }
        logger.LogInformation("Undid the import of {FileName} (Source file {Id})", sourceFile.FileName, sourceFile.Id);
    }
}
