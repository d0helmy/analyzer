using Analyzer.Features.Events.Application;

namespace Analyzer.Analytics;

/// <summary>
/// Default <see cref="IAnalyticsEventStateProvider"/> — projects the
/// scoped <see cref="AnalyticsEventStateStore"/>'s field into the
/// public read contract.
/// </summary>
internal sealed class AnalyticsEventStateProvider : IAnalyticsEventStateProvider
{
    private readonly AnalyticsEventStateStore _store;

    public AnalyticsEventStateProvider(AnalyticsEventStateStore store) =>
        _store = store;

    public AnalyticsEventReceipt? CurrentRequestReceipt => _store.CurrentRequestReceipt;
}
