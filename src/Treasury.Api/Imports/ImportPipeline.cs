using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Data;
using Treasury.Api.ExchangeRates;
using Treasury.Api.Integrations;
using Treasury.Api.Transactions;

namespace Treasury.Api.Imports;

/// The single import pipeline (spec #1 "Ingestion pipeline"), fed by the upload endpoints and the Ingest folder
/// scan (#8). One call imports one file into one Account, or into a Multi-currency account (#25, ADR-0009):
///
///  1. read      — the Integration parses the file; rows are validated. Into a Multi-currency account, the rows are
///                 split by currency, one "leg" per currency, each for the group's Account in that currency; a
///                 currency without an Account gets one (saved with the import), opened with the statement's balance
///                 before that currency's first row, on the day before that row
///  2. Check     — IImportStep stage: reject the file (identifier mismatch, #11)
///  3. Deduplicate — IImportStep stage: drop rows already imported (#9) from <see cref="ImportContext.Rows"/>
///  4. store     — keep ONE Source file and create its Transactions (saved inside a DB transaction)
///  5. Match     — IImportStep stage: Transfers and Contributions (#16)
///  6. Classify  — IImportStep stage: date-range Tags (#14), then Rules (#15)
///  7. Verify    — IImportStep stage: closing balance (#11), adds <see cref="ImportContext.Warnings"/>
///  8. commit, then move the file to the target's processed/ folder and ask for an exchange-rate backfill (#12)
///     when the earliest imported date has no stored rate yet
///
/// Steps see one Account at a time: each leg has its own <see cref="ImportContext"/>, and every step runs over all
/// legs before the next step starts (so all Transactions are stored before matching pairs an exchange between two
/// of the group's Accounts).
///
/// Any step may throw <see cref="ImportFailedException"/> (readers throw <see cref="StatementFormatException"/>):
/// nothing is imported (the DB transaction rolls back, Accounts it would create included), and the file goes to
/// failed/ with an error note next to it. Imports into the same Account or Multi-currency account run one at a time.
public sealed class ImportPipeline(
    TreasuryDbContext db,
    IntegrationRegistry integrations,
    ImportFiles files,
    IngestFolders folders,
    ImportLocks locks,
    IEnumerable<IImportStep> steps,
    ExchangeRateSyncSignal rateSync,
    TimeProvider clock,
    ILogger<ImportPipeline> logger)
{
    public const int MaxReferenceLength = 200;

    private readonly IReadOnlyList<IImportStep> _steps = steps.OrderBy(s => s.Stage).ToList();

    /// Imports a file into one Account (not one in a Multi-currency account: callers import into its group).
    public Task<ImportResult> ImportAsync(Account account, IncomingFile file, CancellationToken ct = default) =>
        RunAsync(account, file, (content, _) =>
        {
            var integration = FindIntegration(account.IntegrationKey);
            var statement = integration.Read(content);
            ValidateRows(statement);
            ValidateCurrency(account, statement);
            return Task.FromResult(new Plan(integration, [new Leg(account, IsNew: false, statement)]));
        }, ct);

    /// Imports a file into a Multi-currency account (load <see cref="MultiCurrencyAccount.Accounts"/> first).
    public Task<ImportResult> ImportAsync(MultiCurrencyAccount group, IncomingFile file, CancellationToken ct = default) =>
        RunAsync(group, file, async (content, token) =>
        {
            var integration = FindIntegration(group.IntegrationKey);
            var statement = integration.Read(content);
            ValidateRows(statement);
            return new Plan(integration, await LegsByCurrencyAsync(group, statement, token));
        }, ct);

    private async Task<ImportResult> RunAsync(IImportTarget target, IncomingFile file,
        Func<SourceFileContent, CancellationToken, Task<Plan>> read, CancellationToken ct)
    {
        using var _ = await locks.AcquireAsync(target.Id, ct);
        var content = new SourceFileContent(ImportFiles.SafeFileName(file.FileName), file.Content);
        string? keptCopy = null;
        try
        {
            var (integration, legs) = await read(content, ct);
            var now = clock.GetUtcNow();
            var contexts = legs.Select(leg => new ImportContext
            {
                Db = db,
                Account = leg.Account,
                Integration = integration,
                File = content,
                Statement = leg.Statement,
                ImportedByMemberId = file.ImportedByMemberId,
                Now = now,
            }).ToList();
            await RunStepsAsync(contexts, s => s < ImportStage.Match, ct);

            var sourceFileId = Guid.CreateVersion7();
            keptCopy = files.Keep(target, sourceFileId, content);
            var stored = new SourceFile
            {
                Id = sourceFileId,
                AccountId = target is Account ? target.Id : null,
                MultiCurrencyAccountId = target is MultiCurrencyAccount ? target.Id : null,
                FileName = content.FileName,
                StoredPath = keptCopy,
                Size = content.Bytes.LongLength,
                ContentHash = Convert.ToHexStringLower(SHA256.HashData(content.Bytes)),
                ImportedAt = now,
                ImportedByMemberId = file.ImportedByMemberId,
            };

            // The execution strategy (Npgsql retries) re-runs the whole unit on a transient failure.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                foreach (var leg in legs)
                    if (leg.IsNew) db.Accounts.Add(leg.Account);
                    else db.Attach(leg.Account);
                stored.CurrencyClosingBalances.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                foreach (var context in contexts)
                {
                    context.Warnings.Clear();
                    context.SourceFile = stored;
                    context.Transactions = context.Rows
                        .Select(row => ToTransaction(context.Account, stored, row, now)).ToList();
                    db.Transactions.AddRange(context.Transactions);
                }
                db.SourceFiles.Add(stored);
                await db.SaveChangesAsync(ct);

                await RunStepsAsync(contexts, s => s >= ImportStage.Match, ct);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });

            // Committed: from here on the import stands, whatever happens to the file move or the rate request.
            var created = legs.Where(l => l.IsNew).Select(l => l.Account).ToList();
            return await AfterCommitAsync(target, content, file, contexts, stored, created);
        }
        catch (Exception e) when (e is ImportFailedException or StatementFormatException)
        {
            return Fail(target, content, file, keptCopy, e.Message);
        }
        catch (OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            if (keptCopy is not null) files.DeleteKept(keptCopy);
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import of {File} into {Target} failed unexpectedly", content.FileName, target.Slug);
            return Fail(target, content, file, keptCopy, $"Unexpected error while importing: {e.Message}");
        }
    }

    private IIntegration FindIntegration(string key) =>
        integrations.Find(key) ?? throw new ImportFailedException($"The Integration '{key}' of this Account isn't available.");

    /// One leg per currency of the statement's rows, for the group's Account in that currency. A currency without
    /// one gets a new (not yet saved) Account: "&lt;group name&gt; &lt;CUR&gt;", the group's Owner, opened on the day
    /// before the currency's first row with the statement's balance before that row.
    private async Task<List<Leg>> LegsByCurrencyAsync(MultiCurrencyAccount group, ParsedStatement statement,
        CancellationToken ct)
    {
        for (var i = 0; i < statement.Rows.Count; i++)
            if (string.IsNullOrWhiteSpace(statement.Rows[i].Currency))
                throw new ImportFailedException(
                    $"{At(i, statement.Rows[i])} has no currency, so Treasury can't tell which Account it belongs to.");

        var legs = new List<Leg>();
        var reservedSlugs = new List<string>();
        foreach (var rows in statement.Rows.GroupBy(r => r.Currency!.Trim().ToUpperInvariant())
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var currency = rows.Key;
            var balances = statement.BalancesByCurrency
                .FirstOrDefault(b => string.Equals(b.Currency, currency, StringComparison.OrdinalIgnoreCase));
            var legStatement = new ParsedStatement([.. rows], ClosingBalance: balances?.Closing);
            if (group.Accounts.FirstOrDefault(a => a.Currency == currency) is { } existing)
            {
                legs.Add(new Leg(existing, IsNew: false, legStatement));
                continue;
            }

            if (!Currencies.All.Contains(currency))
                throw new ImportFailedException(
                    $"The statement has rows in {currency}, but Treasury keeps Accounts only in " +
                    $"{string.Join(", ", Currencies.All)}. Nothing was imported.");
            if (balances is null)
                throw new ImportFailedException(
                    $"The statement doesn't say the {currency} balance before its first {currency} row, so Treasury " +
                    $"can't open an Account for {currency}. Nothing was imported.");
            var name = $"{group.Name} {currency}";
            var slug = await AccountEndpoints.UniqueSlugAsync(db, folders, IngestFolders.Slugify(name),
                [group.Slug, .. reservedSlugs]);
            reservedSlugs.Add(slug);
            var account = new Account
            {
                Name = name,
                // Reserved for the Account's kept Source files; it has no Ingest folder of its own (ADR-0009).
                Slug = slug,
                Owner = group.Owner,
                IntegrationKey = group.IntegrationKey,
                Currency = currency,
                OpeningBalance = balances.BalanceBeforeFirstRow,
                OpeningDate = balances.FirstRowDate.AddDays(-1),
                MultiCurrencyAccountId = group.Id,
            };
            legs.Add(new Leg(account, IsNew: true, legStatement));
        }
        return legs;
    }

    private async Task<ImportResult> AfterCommitAsync(IImportTarget target, SourceFileContent content, IncomingFile file,
        List<ImportContext> contexts, SourceFile stored, List<Account> created)
    {
        var transactions = contexts.SelectMany(c => c.Transactions).ToList();
        var placedAt = "";
        try
        {
            placedAt = files.PlaceProcessed(target, content, file.DroppedAtPath);
            // Old Transactions may need exchange rates from before every stored one: ask for a backfill now (#12).
            if (transactions.Count > 0)
                await rateSync.RequestIfNotCoveredAsync(db, transactions.Min(t => t.Date), CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Imported {File} into {Target}, but finishing up failed", content.FileName, target.Slug);
        }
        logger.LogInformation("Imported {Count} Transactions from {File} into {Target}",
            transactions.Count, content.FileName, target.Slug);
        return ImportResult.Success(stored, transactions.Count, contexts.Sum(c => c.SkippedCount),
            contexts.SelectMany(c => c.Warnings), placedAt, created);
    }

    private ImportResult Fail(IImportTarget target, SourceFileContent content, IncomingFile file, string? keptCopy,
        string reason)
    {
        db.ChangeTracker.Clear();
        if (keptCopy is not null) files.DeleteKept(keptCopy);
        var placedAt = files.PlaceFailed(target, content, file.DroppedAtPath, reason);
        logger.LogWarning("Import of {File} into {Target} failed: {Reason}", content.FileName, target.Slug, reason);
        return ImportResult.Failure(reason, placedAt);
    }

    /// Runs each step over every leg before the next step.
    private async Task RunStepsAsync(List<ImportContext> contexts, Func<ImportStage, bool> stages, CancellationToken ct)
    {
        foreach (var step in _steps.Where(s => stages(s.Stage)))
        foreach (var context in contexts)
            await step.RunAsync(context, ct);
    }

    /// Facts every Integration must deliver in a shape Treasury can store.
    private static void ValidateRows(ParsedStatement statement)
    {
        for (var i = 0; i < statement.Rows.Count; i++)
        {
            var row = statement.Rows[i];
            var at = At(i, row);
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

    /// A single Account takes rows in its own currency only.
    private static void ValidateCurrency(Account account, ParsedStatement statement)
    {
        for (var i = 0; i < statement.Rows.Count; i++)
            if (statement.Rows[i].Currency is { } currency
                && !string.Equals(currency, account.Currency, StringComparison.OrdinalIgnoreCase))
                throw new ImportFailedException(
                    $"{At(i, statement.Rows[i])} is in {currency}, but this Account is in {account.Currency}. Is this the right Account?");
    }

    private static string At(int index, StatementRow row) => $"Row {index + 1} ({row.Date:yyyy-MM-dd} {row.Description})";

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

    /// What a file reads as: its Integration and one leg per Account it goes to.
    private sealed record Plan(IIntegration Integration, List<Leg> Legs);

    /// The rows (and that currency's closing balance) for one Account; IsNew = created by this import.
    private sealed record Leg(Account Account, bool IsNew, ParsedStatement Statement);
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
    string PlacedAt,
    IReadOnlyList<Account> CreatedAccounts)
{
    /// <param name="created">Accounts the import created for new currencies of a Multi-currency account (#25).</param>
    public static ImportResult Success(SourceFile sourceFile, int imported, int skipped, IEnumerable<string> warnings,
        string placedAt, IReadOnlyList<Account> created) =>
        new(true, sourceFile, imported, skipped, warnings.ToList(), null, placedAt, created);

    public static ImportResult Failure(string error, string placedAt) => new(false, null, 0, 0, [], error, placedAt, []);
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
