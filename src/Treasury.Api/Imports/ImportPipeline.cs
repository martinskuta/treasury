using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Data;
using Treasury.Api.Integrations;
using Treasury.Api.Transactions;

namespace Treasury.Api.Imports;

/// The single import pipeline (spec #1 "Ingestion pipeline"), fed by the upload endpoint and the Ingest folder
/// scan (#8). One call imports one file into one Account:
///
///  1. read      — the Account's Integration parses the file; rows are validated against the Account
///  2. Check     — IImportStep stage: reject the file (identifier mismatch, #11)
///  3. Deduplicate — IImportStep stage: drop rows already imported (#9) from <see cref="ImportContext.Rows"/>
///  4. store     — keep the Source file and create its Transactions (saved inside a DB transaction)
///  5. Match     — IImportStep stage: Transfers and Contributions (#16)
///  6. Classify  — IImportStep stage: date-range Tags (#14), then Rules (#15)
///  7. Verify    — IImportStep stage: closing balance (#11), adds <see cref="ImportContext.Warnings"/>
///  8. commit, then move the file to the Account's processed/ folder
///
/// Any step may throw <see cref="ImportFailedException"/> (readers throw <see cref="StatementFormatException"/>):
/// nothing is imported (the DB transaction rolls back), and the file goes to failed/ with an error note next to it.
/// Imports into the same Account run one at a time.
public sealed class ImportPipeline(
    TreasuryDbContext db,
    IntegrationRegistry integrations,
    ImportFiles files,
    ImportLocks locks,
    IEnumerable<IImportStep> steps,
    TimeProvider clock,
    ILogger<ImportPipeline> logger)
{
    public const int MaxReferenceLength = 200;

    private readonly IReadOnlyList<IImportStep> _steps = steps.OrderBy(s => s.Stage).ToList();

    public async Task<ImportResult> ImportAsync(Account account, IncomingFile file, CancellationToken ct = default)
    {
        using var _ = await locks.AcquireAsync(account.Id, ct);
        var content = new SourceFileContent(ImportFiles.SafeFileName(file.FileName), file.Content);
        string? keptCopy = null;
        try
        {
            var integration = integrations.Find(account.IntegrationKey)
                ?? throw new ImportFailedException($"The Integration '{account.IntegrationKey}' of this Account isn't available.");
            var statement = integration.Read(content);
            ValidateRows(account, statement);

            var context = new ImportContext
            {
                Db = db,
                Account = account,
                Integration = integration,
                File = content,
                Statement = statement,
                ImportedByMemberId = file.ImportedByMemberId,
                Now = clock.GetUtcNow(),
            };
            await RunStepsAsync(context, s => s < ImportStage.Match, ct);

            var sourceFileId = Guid.CreateVersion7();
            keptCopy = files.Keep(account, sourceFileId, content);
            var stored = new SourceFile
            {
                Id = sourceFileId,
                AccountId = account.Id,
                FileName = content.FileName,
                StoredPath = keptCopy,
                Size = content.Bytes.LongLength,
                ContentHash = Convert.ToHexStringLower(SHA256.HashData(content.Bytes)),
                ImportedAt = context.Now,
                ImportedByMemberId = file.ImportedByMemberId,
            };

            // The execution strategy (Npgsql retries) re-runs the whole unit on a transient failure.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                db.Attach(account);
                context.Warnings.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                context.SourceFile = stored;
                context.Transactions = context.Rows.Select(row => ToTransaction(account, stored, row, context.Now)).ToList();
                db.SourceFiles.Add(stored);
                db.Transactions.AddRange(context.Transactions);
                await db.SaveChangesAsync(ct);

                await RunStepsAsync(context, s => s >= ImportStage.Match, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });

            var placedAt = files.PlaceProcessed(account, content, file.DroppedAtPath);
            logger.LogInformation("Imported {Count} Transactions from {File} into Account {Account}",
                context.Transactions.Count, content.FileName, account.Slug);
            return ImportResult.Success(stored, context.Transactions.Count, context.SkippedCount, context.Warnings, placedAt);
        }
        catch (Exception e) when (e is ImportFailedException or StatementFormatException)
        {
            return Fail(account, content, file, keptCopy, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Import of {File} into Account {Account} failed unexpectedly", content.FileName, account.Slug);
            return Fail(account, content, file, keptCopy, $"Unexpected error while importing: {e.Message}");
        }
    }

    private ImportResult Fail(Account account, SourceFileContent content, IncomingFile file, string? keptCopy, string reason)
    {
        db.ChangeTracker.Clear();
        if (keptCopy is not null) files.DeleteKept(keptCopy);
        var placedAt = files.PlaceFailed(account, content, file.DroppedAtPath, reason);
        logger.LogWarning("Import of {File} into Account {Account} failed: {Reason}", content.FileName, account.Slug, reason);
        return ImportResult.Failure(reason, placedAt);
    }

    private async Task RunStepsAsync(ImportContext context, Func<ImportStage, bool> stages, CancellationToken ct)
    {
        foreach (var step in _steps.Where(s => stages(s.Stage)))
            await step.RunAsync(context, ct);
    }

    /// Facts every Integration must deliver in a shape Treasury can store.
    private static void ValidateRows(Account account, ParsedStatement statement)
    {
        for (var i = 0; i < statement.Rows.Count; i++)
        {
            var row = statement.Rows[i];
            var at = $"Row {i + 1} ({row.Date:yyyy-MM-dd} {row.Description})";
            if (row.Currency is { } currency && !string.Equals(currency, account.Currency, StringComparison.OrdinalIgnoreCase))
                throw new ImportFailedException(
                    $"{at} is in {currency}, but this Account is in {account.Currency}. Is this the right Account?");
            if (string.IsNullOrWhiteSpace(row.Description))
                throw new ImportFailedException($"{at} has no description.");
            if (row.Description.Trim().Length > TransactionEndpoints.MaxDescriptionLength)
                throw new ImportFailedException($"{at}: the description is longer than {TransactionEndpoints.MaxDescriptionLength} characters.");
            if (row.Counterparty?.Trim().Length > TransactionEndpoints.MaxDescriptionLength)
                throw new ImportFailedException($"{at}: the counterparty is longer than {TransactionEndpoints.MaxDescriptionLength} characters.");
            if (row.Reference?.Trim().Length > MaxReferenceLength)
                throw new ImportFailedException($"{at}: the reference is longer than {MaxReferenceLength} characters.");
            if (row.OriginalAmount is null != string.IsNullOrWhiteSpace(row.OriginalCurrency))
                throw new ImportFailedException($"{at} needs both an original amount and an original currency, or neither.");
            if (row.OriginalCurrency is { } original && !IsCurrencyCode(original.Trim()))
                throw new ImportFailedException($"{at}: '{original}' is not a currency code.");
        }
    }

    private static bool IsCurrencyCode(string code) => code.Length == 3 && code.All(char.IsAsciiLetter);

    private static Transaction ToTransaction(Account account, SourceFile sourceFile, StatementRow row, DateTimeOffset now) =>
        new()
        {
            AccountId = account.Id,
            Account = account,
            Origin = TransactionOrigin.Imported,
            SourceFileId = sourceFile.Id,
            Date = row.Date,
            Amount = row.Amount,
            Description = row.Description.Trim(),
            Counterparty = Blank(row.Counterparty),
            Reference = Blank(row.Reference),
            OriginalAmount = row.OriginalAmount,
            OriginalCurrency = Blank(row.OriginalCurrency)?.ToUpperInvariant(),
            Owner = account.Owner,
            ManuallySet = ManuallySetFields.None,
            CreatedAt = now,
        };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// A file to import into an Account.
/// <param name="FileName">The original file name (may come from a browser: it is sanitised).</param>
/// <param name="Content">The file's bytes.</param>
/// <param name="ImportedByMemberId">The uploading Member; null for files from the Ingest folder.</param>
/// <param name="DroppedAtPath">For a file found in the Ingest folder (#8): its path there. The pipeline then
/// moves that file to processed/ or failed/ instead of writing <paramref name="Content"/>.</param>
public sealed record IncomingFile(string FileName, byte[] Content, Guid? ImportedByMemberId = null, string? DroppedAtPath = null);

public sealed record ImportResult(
    bool Succeeded,
    SourceFile? SourceFile,
    int ImportedCount,
    int SkippedCount,
    IReadOnlyList<string> Warnings,
    string? Error,
    string PlacedAt)
{
    public static ImportResult Success(SourceFile sourceFile, int imported, int skipped, IEnumerable<string> warnings,
        string placedAt) => new(true, sourceFile, imported, skipped, warnings.ToList(), null, placedAt);

    public static ImportResult Failure(string error, string placedAt) => new(false, null, 0, 0, [], error, placedAt);
}

/// The stages where later features hook into the pipeline, in the order they run.
public enum ImportStage
{
    /// Before anything is stored: may reject the whole file (throw ImportFailedException). E.g. identifier (#11).
    Check = 10,

    /// Before anything is stored: removes rows from ImportContext.Rows and counts them in SkippedCount (#9).
    Deduplicate = 20,

    /// After the Source file and Transactions are saved (same DB transaction): Transfers/Contributions (#16).
    Match = 30,

    /// After matching: date-range Tags (#14) and Rules (#15). Must respect Transaction.ManuallySet.
    Classify = 40,

    /// Last: closing-balance comparison (#11); may add ImportContext.Warnings.
    Verify = 50,
}

/// A pluggable pipeline step. Register with services.AddScoped&lt;IImportStep, MyStep&gt;(); steps of the same stage
/// run in registration order. Use ImportContext.Db (the pipeline's DbContext) so everything shares one transaction;
/// the pipeline saves after the store and after the last step, so steps only need to change tracked entities.
public interface IImportStep
{
    ImportStage Stage { get; }

    Task RunAsync(ImportContext context, CancellationToken ct);
}

/// Everything a step can see and change about one import.
public sealed class ImportContext
{
    public required TreasuryDbContext Db { get; init; }

    public required Account Account { get; init; }

    public required IIntegration Integration { get; init; }

    public required SourceFileContent File { get; init; }

    /// What the Integration read (all rows, identifier, closing balance).
    public required ParsedStatement Statement { get; init; }

    public Guid? ImportedByMemberId { get; init; }

    public DateTimeOffset Now { get; init; }

    /// The rows that will become Transactions. Starts as all of the statement's rows; Deduplicate steps remove rows.
    public List<StatementRow> Rows { get => _rows ??= [.. Statement.Rows]; set => _rows = value; }
    private List<StatementRow>? _rows;

    /// Rows a Deduplicate step dropped (reported to the Member).
    public int SkippedCount { get; set; }

    /// Set by the store step (available from the Match stage on).
    public SourceFile SourceFile { get; set; } = null!;

    /// The new Transactions, in the order of <see cref="Rows"/> (available from the Match stage on, tracked by Db).
    public List<Transaction> Transactions { get; set; } = [];

    /// Non-fatal findings shown to the Member (e.g. closing balance mismatch).
    public List<string> Warnings { get; } = [];
}

/// Thrown by a pipeline step to reject the file: nothing is imported and the file goes to failed/ with this
/// message as its error note. Write the message for a Member.
public class ImportFailedException(string message) : Exception(message);

/// One import at a time per Account (upload and folder scan may race; duplicate detection needs it).
public sealed class ImportLocks
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(Guid accountId, CancellationToken ct)
    {
        var semaphore = _locks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Release(semaphore);
    }

    private sealed class Release(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
        }
    }
}
