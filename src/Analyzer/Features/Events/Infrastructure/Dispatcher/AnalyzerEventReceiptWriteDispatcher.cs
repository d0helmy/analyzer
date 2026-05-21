using System.Threading.Channels;
using Analyzer.Features.Events.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Events.Infrastructure.Dispatcher;

/// <summary>
/// Background loop that drains
/// <see cref="AnalyzerEventReceiptWriteQueue"/> and inserts receipts
/// in batches. Single-reader per host instance — the queue's
/// <c>SingleReader = true</c> setting lets the runtime optimise away
/// multi-consumer locking. Mirrors Customizer's
/// <c>VisitorWriteDispatcher</c> shape.
/// </summary>
internal sealed class AnalyzerEventReceiptWriteDispatcher : BackgroundService
{
    private readonly AnalyzerEventReceiptWriteQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AnalyzerWriteQueueOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalyzerEventReceiptWriteDispatcher> _logger;

    public AnalyzerEventReceiptWriteDispatcher(
        AnalyzerEventReceiptWriteQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AnalyzerWriteQueueOptions> options,
        TimeProvider timeProvider,
        ILogger<AnalyzerEventReceiptWriteDispatcher> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // #47 — same SuppressFlow guard as AnalyzerSessionSweeperService;
        // see that file for the full rationale. The receipt dispatcher
        // ticks only when the queue drains so the AmbientContext failure
        // is quieter here, but the vulnerability shape is identical
        // (BackgroundService.ExecuteAsync inherits the host-startup
        // ExecutionContext, leaked Umbraco scope contaminates every
        // CreateScope() in the loop). Wrap in SuppressFlow + Task.Run
        // for the same reason; await sits OUTSIDE the using so
        // AsyncFlowControl.Dispose() runs on the calling thread.
        Task loop;
        using (ExecutionContext.SuppressFlow())
        {
            loop = Task.Run(() => RunLoopAsync(stoppingToken), stoppingToken);
        }
        await loop.ConfigureAwait(false);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analyzer event-receipt dispatcher started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Analyzer event-receipt dispatcher tick failed");
                }
            }
        }
        finally
        {
            await DrainGracefulShutdownAsync(TimeSpan.FromSeconds(5));
            _logger.LogInformation("Analyzer event-receipt dispatcher stopped");
        }
    }

    private async Task DrainOnceAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var flushInterval = TimeSpan.FromMilliseconds(Math.Max(50, options.FlushIntervalMs));
        var maxBatch = Math.Max(1, options.FlushBatchSize);

        var batch = new List<AnalyzerEventReceiptWriteOp>(maxBatch);
        using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        batchCts.CancelAfter(flushInterval);

        while (batch.Count < maxBatch)
        {
            try
            {
                var op = await _queue.Reader.ReadAsync(batchCts.Token);
                batch.Add(op);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ChannelClosedException)
            {
                break;
            }
        }

        if (batch.Count == 0)
        {
            return;
        }

        await FlushBatchAsync(batch, cancellationToken);
    }

    private async Task DrainGracefulShutdownAsync(TimeSpan timeout)
    {
        var deadline = _timeProvider.GetUtcNow().UtcDateTime + timeout;
        var batch = new List<AnalyzerEventReceiptWriteOp>();
        while (_timeProvider.GetUtcNow().UtcDateTime < deadline && _queue.Reader.TryRead(out var op))
        {
            batch.Add(op);
        }
        if (batch.Count > 0)
        {
            try
            {
                await FlushBatchAsync(batch, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Analyzer event-receipt dispatcher graceful-shutdown drain failed");
            }
        }
    }

    private async Task FlushBatchAsync(
        IReadOnlyList<AnalyzerEventReceiptWriteOp> batch,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalyzerEventReceiptRepository>();
        foreach (var op in batch)
        {
            await repository.InsertAsync(op.Receipt, cancellationToken).ConfigureAwait(false);
        }
        _logger.LogDebug(
            "Analyzer event-receipt batch flushed Count={Count}",
            batch.Count);
    }
}
