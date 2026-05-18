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

    public AnalyticsEventReceipt? CurrentRequestReceipt => _currentReceipt;

    public AnalyticsSession? CurrentSession => _currentSession;

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
}
