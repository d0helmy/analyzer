using Analyzer.Analytics;
using Analyzer.Features.Events.Infrastructure.Dispatcher;
using Analyzer.Features.Sessions.Application;
using Customizer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Analyzer.Features.Events.Application;

/// <summary>
/// Subscribes to Customizer's <see cref="PageviewCaptured"/> notification.
/// First runs slice-003 session resolution synchronously (the receipt's
/// <c>sessionKey</c> FK must be durable by enqueue time), then enqueues
/// an event-receipt write op onto Analyzer's bounded queue. Runs on a
/// thread-pool thread (Customizer's notifier wraps dispatch in
/// <c>Task.Run</c>), so the body must never block, never propagate
/// exceptions, and never depend on the request scope being alive
/// (research §1, §5).
/// </summary>
internal sealed class PageviewCapturedHandler : INotificationAsyncHandler<PageviewCaptured>
{
    private readonly AnalyzerEventReceiptWriteQueue _queue;
    private readonly IAnalyzerSessionResolver _sessionResolver;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PageviewCapturedHandler> _logger;

    public PageviewCapturedHandler(
        AnalyzerEventReceiptWriteQueue queue,
        IAnalyzerSessionResolver sessionResolver,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<PageviewCapturedHandler> logger)
    {
        _queue = queue;
        _sessionResolver = sessionResolver;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(PageviewCaptured notification, CancellationToken cancellationToken)
    {
        try
        {
            var pv = notification.Pageview;

            if (pv.Key == Guid.Empty)
            {
                _logger.LogDebug("Skipping PageviewCaptured with empty Pageview.Key");
                return;
            }

            if (pv.VisitorProfileKey == Guid.Empty)
            {
                _logger.LogWarning(
                    "Configuration error: PageviewCaptured fired with VisitorProfileKey=Empty for PageviewKey={PageviewKey}; skipping receipt.",
                    pv.Key);
                return;
            }

            var receivedUtc = _timeProvider.GetUtcNow();

            // Slice 003 — resolve the session before building the
            // receipt. UA sourced from pv.UserAgent (cross-product
            // prereq at customizer 5273c38); NOT from
            // IHttpContextAccessor — that path is unreliable under
            // fire-and-forget timing (lesson #40 / analysis C1).
            SessionResolutionResult resolution;
            try
            {
                resolution = await _sessionResolver
                    .ResolveAsync(pv.VisitorProfileKey, pv.UserAgent, receivedUtc, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Session resolution failed for PageviewKey={PageviewKey} VisitorProfileKey={VisitorProfileKey}; skipping receipt enqueue.",
                    pv.Key, pv.VisitorProfileKey);
                return;
            }

            var receipt = new AnalyticsEventReceipt(
                Id: Guid.NewGuid(),
                PageviewKey: pv.Key,
                VisitorProfileKey: pv.VisitorProfileKey,
                ReceivedUtc: receivedUtc)
            with
            { SessionKey = resolution.SessionKey };

            var op = new AnalyzerEventReceiptWriteOp(receipt);
            if (!_queue.TryEnqueue(op))
            {
                _logger.LogWarning(
                    "Analyzer write queue at capacity ({Capacity}); dropping receipt for PageviewKey={PageviewKey} VisitorProfileKey={VisitorProfileKey}",
                    _queue.Capacity, pv.Key, pv.VisitorProfileKey);
                return;
            }

            // Slice-002 US3 + slice-003 — opportunistic in-flight
            // state-store update so the (rare) in-request consumer
            // sees both the receipt + session via
            // IAnalyticsEventStateProvider. Best-effort: typically
            // null on a fire-and-forget pageview dispatch because the
            // request scope has already been disposed.
            TryUpdateInFlightStateStore(receipt, resolution.Projection);
        }
        catch (Exception ex)
        {
            // Defence in depth — Customizer's PageviewCapturedNotifier
            // already swallows at warning level; this redundant catch
            // contains a bug inside this handler that's not part of the
            // publish chain.
            _logger.LogWarning(ex,
                "PageviewCapturedHandler failed for PageviewKey={PageviewKey}",
                notification.Pageview.Key);
        }
    }

    private void TryUpdateInFlightStateStore(
        AnalyticsEventReceipt receipt,
        AnalyticsSession session)
    {
        try
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices;
            var store = requestServices?.GetService<AnalyticsEventStateStore>();
            if (store is null)
            {
                return;
            }
            store.SetCurrentReceipt(receipt);
            store.SetCurrentSession(session);
        }
        catch (ObjectDisposedException)
        {
            // Request scope already disposed — the common case for
            // fire-and-forget pageview dispatches. Not an error.
        }
        catch (InvalidOperationException)
        {
            // Service provider disposed; same case.
        }
    }
}
