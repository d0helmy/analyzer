using Analyzer.Analytics;
using Analyzer.Features.Events.Infrastructure.Dispatcher;
using Customizer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Analyzer.Features.Events.Application;

/// <summary>
/// Subscribes to Customizer's <see cref="PageviewCaptured"/> notification
/// and enqueues an event-receipt write op onto Analyzer's bounded
/// queue. Runs on a thread-pool thread (Customizer's notifier wraps
/// dispatch in <c>Task.Run</c>), so the body must never block, never
/// propagate exceptions, and never depend on the request scope being
/// alive (research §1, §5).
/// </summary>
internal sealed class PageviewCapturedHandler : INotificationAsyncHandler<PageviewCaptured>
{
    private readonly AnalyzerEventReceiptWriteQueue _queue;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PageviewCapturedHandler> _logger;

    public PageviewCapturedHandler(
        AnalyzerEventReceiptWriteQueue queue,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<PageviewCapturedHandler> logger)
    {
        _queue = queue;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public Task HandleAsync(PageviewCaptured notification, CancellationToken cancellationToken)
    {
        try
        {
            var pv = notification.Pageview;

            if (pv.Key == Guid.Empty)
            {
                _logger.LogDebug("Skipping PageviewCaptured with empty Pageview.Key");
                return Task.CompletedTask;
            }

            if (pv.VisitorProfileKey == Guid.Empty)
            {
                _logger.LogWarning(
                    "Configuration error: PageviewCaptured fired with VisitorProfileKey=Empty for PageviewKey={PageviewKey}; skipping receipt.",
                    pv.Key);
                return Task.CompletedTask;
            }

            var receipt = new AnalyticsEventReceipt(
                Id: Guid.NewGuid(),
                PageviewKey: pv.Key,
                VisitorProfileKey: pv.VisitorProfileKey,
                ReceivedUtc: _timeProvider.GetUtcNow());

            var op = new AnalyzerEventReceiptWriteOp(receipt);
            if (!_queue.TryEnqueue(op))
            {
                _logger.LogWarning(
                    "Analyzer write queue at capacity ({Capacity}); dropping receipt for PageviewKey={PageviewKey} VisitorProfileKey={VisitorProfileKey}",
                    _queue.Capacity, pv.Key, pv.VisitorProfileKey);
                return Task.CompletedTask;
            }

            // Slice-002 US3 — opportunistic in-flight state-store
            // update so the (rare) in-request consumer sees the
            // receipt via IAnalyticsEventStateProvider. Best-effort:
            // typically null on a fire-and-forget pageview dispatch
            // because the request scope has already been disposed.
            TryUpdateInFlightStateStore(receipt);
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

        return Task.CompletedTask;
    }

    private void TryUpdateInFlightStateStore(AnalyticsEventReceipt receipt)
    {
        try
        {
            var requestServices = _httpContextAccessor.HttpContext?.RequestServices;
            var store = requestServices?.GetService<AnalyticsEventStateStore>();
            store?.SetCurrentReceipt(receipt);
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
