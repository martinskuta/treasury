using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Treasury.Api.Data;

namespace Treasury.Api.ExchangeRates;

/// Lets request handlers (entering or importing Transactions) ask the background job to sync now
/// instead of at its next scheduled run, e.g. when a Transaction is dated before every stored rate.
public sealed class ExchangeRateSyncSignal
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// Asks for a sync as soon as possible (requests made while one is pending are merged).
    public void Request() => _requests.Writer.TryWrite(true);

    /// Requests a sync (backfill) when no rate on or before <paramref name="date"/> is stored yet.
    /// Call after saving Transactions; pass the earliest of their dates.
    public async Task RequestIfNotCoveredAsync(TreasuryDbContext db, DateOnly date, CancellationToken ct = default)
    {
        if (!await db.ExchangeRates.AnyAsync(r => r.Date <= date, ct)) Request();
    }

    internal Task WaitAsync(CancellationToken ct) => _requests.Reader.ReadAsync(ct).AsTask();
}

/// Scheduled exchange-rate fetching: syncs at startup, then every <see cref="ExchangeRateOptions.SyncInterval"/>
/// (on the injected TimeProvider) or sooner when <see cref="ExchangeRateSyncSignal"/> asks.
public sealed class ExchangeRateJob(IServiceScopeFactory scopes, ExchangeRateSyncSignal signal, TimeProvider clock,
    ExchangeRateOptions options, ILogger<ExchangeRateJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ExchangeRateSync>().SyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Frankfurter down, network trouble...: keep the stored rates and try again at the next run.
                logger.LogWarning(ex, "Fetching exchange rates failed; retrying in {Interval}", options.SyncInterval);
            }

            using var wake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await Task.WhenAny(Task.Delay(options.SyncInterval, clock, wake.Token), signal.WaitAsync(wake.Token));
            await wake.CancelAsync();
        }
    }
}

public sealed record ExchangeRateOptions(TimeSpan SyncInterval);
