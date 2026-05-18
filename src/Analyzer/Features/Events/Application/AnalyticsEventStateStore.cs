using Analyzer.Analytics;

namespace Analyzer.Features.Events.Application;

/// <summary>
/// Scoped backing store for <see cref="IAnalyticsEventStateProvider"/>.
/// One instance per request scope. The <c>PageviewCapturedHandler</c>
/// writes the receipt + session opportunistically (best-effort,
/// swallows if the request scope is already disposed); the state
/// provider exposes them to in-process consumers.
/// </summary>
internal sealed class AnalyticsEventStateStore
{
    private AnalyticsEventReceipt? _currentReceipt;
    private AnalyticsSession? _currentSession;
    private readonly List<AnalyticsCustomEvent> _currentCustomEvents = new();

    public AnalyticsEventReceipt? CurrentRequestReceipt => _currentReceipt;

    public AnalyticsSession? CurrentSession => _currentSession;

    /// <summary>
    /// Slice 004 — read-only view over the custom events captured in
    /// the current request scope. Never null; empty list when no
    /// <c>analyzer.send(...)</c> call has been processed for this
    /// request. Grows in append order as the handler invokes
    /// <see cref="AppendCustomEvent"/>.
    /// </summary>
    public IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents =>
        _currentCustomEvents.AsReadOnly();

    public void SetCurrentReceipt(AnalyticsEventReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _currentReceipt = receipt;
    }

    public void SetCurrentSession(AnalyticsSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _currentSession = session;
    }

    /// <summary>
    /// Slice 004 — record one custom event captured in this request
    /// scope. Called by <c>CustomEventCaptureHandler</c> after the
    /// row is persisted. Concurrency: scoped per request; the
    /// controller runs on a single thread, so multi-thread append
    /// inside one scope is not a real slice-004 scenario.
    /// </summary>
    public void AppendCustomEvent(AnalyticsCustomEvent customEvent)
    {
        ArgumentNullException.ThrowIfNull(customEvent);
        _currentCustomEvents.Add(customEvent);
    }
}
