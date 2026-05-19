using Analyzer.Features.Forms.Application.Abandonment;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Sessions.Infrastructure.Sweeper;

/// <summary>
/// Slice 003 US3 / FR-007 — background hosted service that closes
/// inactive sessions. Scans <c>analyzerSession</c> on a configurable
/// cadence for rows where <c>isActive = 1 AND lastActivityUtc + inactivityTimeout &lt; now</c>;
/// sets <c>isActive = 0, endUtc = lastActivityUtc + inactivityTimeout</c>
/// (logical close time, NOT now — spec Assumption #5).
/// </summary>
/// <remarks>
/// Per-tick scope via <see cref="IServiceScopeFactory"/> resolves the
/// scoped repository. Tick exceptions are swallowed + logged at error
/// level so the loop survives downstream-DB hiccups (mirrors
/// slice-002's <c>AnalyzerEventReceiptWriteDispatcher</c>).
/// </remarks>
internal sealed class AnalyzerSessionSweeperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AnalyzerSessionCacheStore _cacheStore;
    private readonly IOptionsMonitor<AnalyzerSessionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalyzerSessionSweeperService> _logger;

    public AnalyzerSessionSweeperService(
        IServiceScopeFactory scopeFactory,
        AnalyzerSessionCacheStore cacheStore,
        IOptionsMonitor<AnalyzerSessionOptions> options,
        TimeProvider timeProvider,
        ILogger<AnalyzerSessionSweeperService> logger)
    {
        _scopeFactory = scopeFactory;
        _cacheStore = cacheStore;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Analyzer session sweeper started");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Analyzer session sweeper tick failed");
                }

                try
                {
                    var intervalSeconds = Math.Max(1, _options.CurrentValue.SweepIntervalSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _logger.LogInformation("Analyzer session sweeper stopped");
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        var options = _options.CurrentValue;
        var inactivity = TimeSpan.FromMinutes(Math.Max(1, options.InactivityTimeoutMinutes));
        var batchSize = Math.Max(1, options.SweepBatchSize);
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - inactivity;

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAnalyzerSessionRepository>();

        var closed = await repository
            .SweepEligibleAsync(cutoff, inactivity, batchSize, ct)
            .ConfigureAwait(false);

        if (closed.Count > 0)
        {
            foreach (var sessionKey in closed)
            {
                _cacheStore.InvalidateBySessionKey(sessionKey);
            }

            // Slice 005 — materialise Abandon rows for any
            // (visitorKey, formKey, sessionKey) tuple with an open
            // Start row in the just-closed batch. Same DI scope as
            // the close-UPDATEs above so an exception rolls both
            // sides back atomically. logicalCloseUtc is `now`: the
            // session row's endUtc is `lastActivityUtc + inactivity`,
            // but the abandonment instant (when we observe the user
            // has stopped engaging) is now.
            var materialiser = scope.ServiceProvider
                .GetRequiredService<IAnalyzerFormAbandonmentMaterialiser>();
            await materialiser
                .MaterialiseAsync(closed, now, ct)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Analyzer session sweeper closed {Count} sessions",
                closed.Count);
        }
    }
}
