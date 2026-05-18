using Analyzer.Analytics;

namespace Analyzer.Features.Events.Application;

/// <summary>
/// Scoped backing store for <see cref="IAnalyticsEventStateProvider"/>.
/// One instance per request scope. The <c>PageviewCapturedHandler</c>
/// writes the receipt opportunistically (best-effort, swallows if the
/// request scope is already disposed); the state provider exposes it
/// to in-process consumers.
/// </summary>
internal sealed class AnalyticsEventStateStore
{
    private AnalyticsEventReceipt? _current;

    public AnalyticsEventReceipt? CurrentRequestReceipt => _current;

    public void SetCurrentReceipt(AnalyticsEventReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _current = receipt;
    }
}
