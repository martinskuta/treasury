using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;
using Treasury.Api.Matching;

namespace Treasury.Api.Imports;

/// Undoes an import (#10): deletes the Source file, every Transaction imported from it (FK cascade) and its kept
/// copy under source-files/. The copy the Member has in ingest/&lt;slug&gt;/processed/ is theirs and stays.
/// Nothing else remembers the file afterwards (no hash, no rows), so the same file can be imported again.
public sealed class ImportUndo(TreasuryDbContext db, ImportLocks locks, ImportFiles files, TransactionMatcher matcher,
    FormerLegs formerLegs, ILogger<ImportUndo> logger)
{
    /// Deletes the Source file. Permission checks are the caller's job.
    public async Task UndoAsync(SourceFile sourceFile, CancellationToken ct)
    {
        // Same lock as imports: an undo never interleaves with an import into the same Account.
        using var _ = await locks.AcquireAsync(sourceFile.AccountId, ct);

        List<Guid> survivors = [];
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Break the Transfer/Contribution links of this file's Transactions first: the counterparts in other
            // Accounts stay and become plain Income/Expense again (losing Contribution Categories matching gave them).
            survivors = await matcher.BreakLinksOfSourceFileAsync(sourceFile.Id, ct);
            await db.SaveChangesAsync(ct);
            // Transactions go with it: Transaction.SourceFileId is ON DELETE CASCADE.
            await db.SourceFiles.Where(f => f.Id == sourceFile.Id).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        });

        // The counterparts may pair up with someone else now, or be categorised by Rules.
        db.ChangeTracker.Clear();
        await formerLegs.SettleAsync(survivors, ct);

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
