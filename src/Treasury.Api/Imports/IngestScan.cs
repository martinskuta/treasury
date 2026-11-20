using Microsoft.EntityFrameworkCore;
using Treasury.Api.Accounts;
using Treasury.Api.Data;
using Treasury.Api.Ownership;

namespace Treasury.Api.Imports;

/// Imports the files Members drop into Ingest folders (ADR-0002, spec #1 "Folder scanning"): every file directly
/// inside ingest/&lt;account-slug&gt;/ goes through the <see cref="ImportPipeline"/> into that Account, which moves it
/// to processed/ or failed/. Scans run one at a time (the background job and on-demand scans share this singleton).
public sealed class IngestScanner(IServiceScopeFactory scopes, IngestFolders folders, IngestScanOptions options,
    ILogger<IngestScanner> logger)
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// Scans every Account's Ingest folder once and returns what happened to each file found.
    public async Task<IReadOnlyList<ScannedFile>> ScanAsync(CancellationToken ct = default)
    {
        await _oneAtATime.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TreasuryDbContext>();
            var pipeline = scope.ServiceProvider.GetRequiredService<ImportPipeline>();
            var found = new List<(Account Account, string Path, FileState State)>();
            foreach (var account in await db.Accounts.AsNoTracking().OrderBy(a => a.Slug).ToListAsync(ct))
            {
                var folder = folders.PathFor(account.Slug);
                if (!Directory.Exists(folder)) continue;
                foreach (var path in Directory.EnumerateFiles(folder).Where(IsCandidate).Order(StringComparer.Ordinal))
                    if (FileState.Of(path) is { } state) found.Add((account, path, state));
            }
            if (found.Count == 0) return [];

            // A file that is still being copied in changes size or modification time while we wait. Real time on
            // purpose: this is about another process writing to the disk, not about Treasury's clock.
            await Task.Delay(options.SettleTime, ct);

            var results = new List<ScannedFile>();
            foreach (var (account, path, state) in found)
            {
                if (FileState.Of(path) != state || TryReadExclusively(path) is not { } bytes)
                {
                    logger.LogDebug("{File} is still being written; leaving it for the next scan", path);
                    continue;
                }
                var result = await pipeline.ImportAsync(account,
                    new IncomingFile(Path.GetFileName(path), bytes, ImportedByMemberId: null, DroppedAtPath: path), ct);
                results.Add(new ScannedFile(account.Id, account.Owner, Path.GetFileName(path), result));
            }
            return results;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// Not error notes, and not the temporary files that downloads and copy tools write before renaming
    /// (".part", ".crdownload", ".tmp", "~$..." Office lock files, hidden "." files).
    private static bool IsCandidate(string path)
    {
        var name = Path.GetFileName(path);
        return !name.StartsWith('.') && !name.StartsWith("~$", StringComparison.Ordinal)
            && !name.EndsWith(ImportFiles.ErrorNoteSuffix, StringComparison.OrdinalIgnoreCase)
            && !TemporaryExtensions.Contains(Path.GetExtension(name));
    }

    private static readonly HashSet<string> TemporaryExtensions =
        new([".part", ".partial", ".crdownload", ".download", ".tmp", ".temp"], StringComparer.OrdinalIgnoreCase);

    /// The file's bytes, or null when another process still has it open for writing (Windows refuses the
    /// exclusive open; Linux doesn't lock, there the settle check above is what catches a file being written)
    /// or it disappeared meanwhile.
    private static byte[]? TryReadExclusively(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct FileState(long Length, DateTime LastWriteUtc)
    {
        public static FileState? Of(string path)
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileState(info.Length, info.LastWriteTimeUtc) : null;
        }
    }
}

/// Settings "Ingest:ScanInterval" (default 1 minute) and "Ingest:SettleTime" (default 2 seconds: how long a
/// file must stay unchanged during a scan before it is imported).
public sealed record IngestScanOptions(TimeSpan ScanInterval, TimeSpan SettleTime);

/// Scans the Ingest folders at startup and then every <see cref="IngestScanOptions.ScanInterval"/> (on the injected
/// TimeProvider). Polling rather than file-change events, which are unreliable on Docker volumes and network shares.
public sealed class IngestScanJob(IngestScanner scanner, IngestScanOptions options, TimeProvider clock,
    ILogger<IngestScanJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await scanner.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Folder unreadable, database down...: try again at the next scan.
                logger.LogWarning(ex, "Scanning the Ingest folders failed; retrying in {Interval}", options.ScanInterval);
            }

            try
            {
                await Task.Delay(options.ScanInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

/// One file a scan picked up, and what the import made of it.
public sealed record ScannedFile(Guid AccountId, Owner AccountOwner, string FileName, ImportResult Result);

public static class IngestScanSetup
{
    /// The scanner and its background job. Settings: Ingest:ScanInterval (default 1 minute), Ingest:SettleTime
    /// (default 2 seconds).
    public static void AddIngestScan(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        builder.Services.AddSingleton(new IngestScanOptions(
            config.GetValue<TimeSpan?>("Ingest:ScanInterval") ?? TimeSpan.FromMinutes(1),
            config.GetValue<TimeSpan?>("Ingest:SettleTime") ?? TimeSpan.FromSeconds(2)));
        builder.Services.AddSingleton<IngestScanner>();
        builder.Services.AddHostedService<IngestScanJob>();
    }

    public record ScannedFileResponse(Guid AccountId, string FileName, bool Succeeded, int ImportedCount,
        int SkippedCount, IReadOnlyList<string> Warnings, string? Error);

    /// POST /api/ingest/scan: scans every Ingest folder now (instead of waiting for the next scheduled scan) and
    /// returns the files it imported or failed, for the Accounts the Member can see.
    public static void MapIngestScan(this RouteGroupBuilder api)
    {
        api.MapPost("/ingest/scan", async (System.Security.Claims.ClaimsPrincipal user, IngestScanner scanner,
            CancellationToken ct) =>
        {
            var viewer = user.MemberId();
            var scanned = await scanner.ScanAsync(ct);
            return Results.Ok(scanned.Where(f => f.AccountOwner.IsVisibleTo(viewer))
                .Select(f => new ScannedFileResponse(f.AccountId, f.FileName, f.Result.Succeeded, f.Result.ImportedCount,
                    f.Result.SkippedCount, f.Result.Warnings, f.Result.Error)));
        });
    }
}
