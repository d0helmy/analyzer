using Analyzer.Features.Reporting.Infrastructure;
using Analyzer.Reporting.ContentAnalytics;

namespace Analyzer.Features.Reporting.Application;

internal sealed class ContentAnalyticsQueryService : IContentAnalyticsQueryService
{
    private static readonly IReadOnlyList<string> EmptyTopReferrers = Array.Empty<string>();

    private readonly IContentAnalyticsRepository _repository;
    private readonly IPublishedContentTombstoneProbe _tombstoneProbe;
    private readonly TimeProvider _timeProvider;

    public ContentAnalyticsQueryService(
        IContentAnalyticsRepository repository,
        IPublishedContentTombstoneProbe tombstoneProbe,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _tombstoneProbe = tombstoneProbe;
        _timeProvider = timeProvider;
    }

    public async Task<ContentAnalyticsSnapshot?> GetAsync(Guid contentKey, CancellationToken ct)
    {
        var windowEndUtc = _timeProvider.GetUtcNow();
        var projection = await _repository.GetAsync(contentKey, windowEndUtc, ct).ConfigureAwait(false);
        var isTombstoned = _tombstoneProbe.IsTombstoned(contentKey);

        if (!projection.HasAnyCaptureRow && isTombstoned)
        {
            return null;
        }

        return new ContentAnalyticsSnapshot(
            ContentKey: contentKey,
            WindowEndUtc: windowEndUtc,
            Pageviews24h: projection.Pageviews24h,
            Pageviews7d: projection.Pageviews7d,
            Pageviews30d: projection.Pageviews30d,
            UniqueVisitors30d: projection.UniqueVisitors30d,
            AvgTimeOnPageSeconds30d: projection.AvgTimeOnPageSeconds30d,
            IsContentCurrentlyTombstoned: isTombstoned,
            TopReferrers30d: EmptyTopReferrers);
    }
}
