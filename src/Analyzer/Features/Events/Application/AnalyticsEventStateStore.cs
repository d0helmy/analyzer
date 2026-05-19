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
    private readonly List<AnalyticsFormEvent> _currentFormEvents = new();
    private readonly List<AnalyticsFormFieldEvent> _currentFormFieldEvents = new();
    private readonly List<AnalyticsScrollSample> _currentScrollEvents = new();

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

    /// <summary>
    /// Slice 005 — read-only view over the per-form lifecycle events
    /// captured in the current request scope. Never null; empty list
    /// at scope creation. Grows in append order as the handler invokes
    /// <see cref="AppendFormEvent"/>.
    /// </summary>
    public IReadOnlyList<AnalyticsFormEvent> CurrentRequestFormEvents =>
        _currentFormEvents.AsReadOnly();

    /// <summary>
    /// Slice 005 — read-only view over the per-field events captured
    /// in the current request scope. Never null; empty list at scope
    /// creation.
    /// </summary>
    public IReadOnlyList<AnalyticsFormFieldEvent> CurrentRequestFormFieldEvents =>
        _currentFormFieldEvents.AsReadOnly();

    /// <summary>
    /// Slice 006 — read-only view over the scroll-milestone events
    /// captured in the current request scope. Never null; empty list
    /// at scope creation. Grows in append order as the handler invokes
    /// <see cref="AppendScrollEvent"/>; the DB unique index
    /// <c>UX_analyzerScrollSample_pageviewBucket</c> ensures at most
    /// one entry per <c>(pageview, bucket)</c> tuple per request.
    /// </summary>
    public IReadOnlyList<AnalyticsScrollSample> CurrentRequestScrollEvents =>
        _currentScrollEvents.AsReadOnly();

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

    /// <summary>
    /// Slice 005 — record one form-lifecycle event captured in this
    /// request scope. Called by
    /// <c>AnalyzerFormEventCaptureHandler</c> after the row is
    /// persisted. Single-threaded per scope (the controller runs on
    /// the request thread); no locking required.
    /// </summary>
    public void AppendFormEvent(AnalyticsFormEvent formEvent)
    {
        ArgumentNullException.ThrowIfNull(formEvent);
        _currentFormEvents.Add(formEvent);
    }

    /// <summary>
    /// Slice 005 — record one field event captured in this request
    /// scope. Called by
    /// <c>AnalyzerFormFieldEventCaptureHandler</c> after persistence.
    /// </summary>
    public void AppendFormFieldEvent(AnalyticsFormFieldEvent fieldEvent)
    {
        ArgumentNullException.ThrowIfNull(fieldEvent);
        _currentFormFieldEvents.Add(fieldEvent);
    }

    /// <summary>
    /// Slice 006 — record one scroll-milestone event captured in this
    /// request scope. Called by
    /// <c>AnalyzerScrollEventCaptureHandler</c> after a successful
    /// insert (NOT on the 409 duplicate path — the duplicate audit
    /// is emitted but the state store stays untouched, since no row
    /// landed for this request).
    /// </summary>
    public void AppendScrollEvent(AnalyticsScrollSample scrollEvent)
    {
        ArgumentNullException.ThrowIfNull(scrollEvent);
        _currentScrollEvents.Add(scrollEvent);
    }
}
