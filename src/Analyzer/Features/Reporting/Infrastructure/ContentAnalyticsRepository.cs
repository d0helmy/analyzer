using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Domain;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Reporting.Infrastructure;

/// <summary>
/// Slice 008 — single-pass aggregate query. Counts pageviews in each
/// of the three windows, distinct visitors in the 30d window, and
/// the average time on page derived via a session-scoped
/// <c>LAG()</c> window function (research §R3). Reads only
/// non-identifying columns (FR-RPT-009).
/// </summary>
/// <remarks>
/// <para>
/// The projection's <c>HasAnyCaptureRow</c> is derived from
/// <c>Pageviews30d &gt; 0</c> — a pageview row outside the 30d window
/// is invisible to this slice per
/// <c>contracts/AnalyzerContentAnalyticsManagementController.md</c>.
/// </para>
/// <para>
/// The session join is intentionally inner — pageviews that lack a
/// matching <c>analyzerSession</c> row contribute to the counters
/// but not to the average-time-on-page calculation (no successor
/// requestUtc to delta against). Sessions are matched by visitor +
/// time-range; sessions across devices may overlap, which can cause
/// minor over-counting in the average. The aggregate-level smoothing
/// keeps this within the SC-001 / SC-002 tolerances.
/// </para>
/// </remarks>
internal sealed class ContentAnalyticsRepository : IContentAnalyticsRepository
{
    private readonly IScopeProvider _scopeProvider;

    public ContentAnalyticsRepository(IScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public async Task<ContentAnalyticsProjection> GetAsync(
        Guid contentKey,
        DateTimeOffset windowEndUtc,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var start24h = windowEndUtc.AddHours(-24);
        var start7d = windowEndUtc.AddDays(-7);
        var start30d = windowEndUtc.AddDays(-30);

        using var scope = _scopeProvider.CreateScope();
        var row = await scope.Database
            .SingleOrDefaultAsync<ProjectionRow>(
                Sql,
                contentKey,
                start24h,
                start7d,
                start30d)
            .ConfigureAwait(false);
        scope.Complete();

        if (row is null)
        {
            return new ContentAnalyticsProjection(
                Pageviews24h: 0,
                Pageviews7d: 0,
                Pageviews30d: 0,
                UniqueVisitors30d: 0,
                AvgTimeOnPageSeconds30d: null,
                HasAnyCaptureRow: false);
        }

        return new ContentAnalyticsProjection(
            Pageviews24h: row.Pageviews24h,
            Pageviews7d: row.Pageviews7d,
            Pageviews30d: row.Pageviews30d,
            UniqueVisitors30d: row.UniqueVisitors30d,
            AvgTimeOnPageSeconds30d: row.AvgTimeOnPageSeconds30d,
            HasAnyCaptureRow: row.Pageviews30d > 0);
    }

    internal const string Sql = @"
SELECT
    SUM(CASE WHEN requestUtc >= @1 THEN 1 ELSE 0 END) AS Pageviews24h,
    SUM(CASE WHEN requestUtc >= @2 THEN 1 ELSE 0 END) AS Pageviews7d,
    COUNT(*) AS Pageviews30d,
    COUNT(DISTINCT visitorProfileFk) AS UniqueVisitors30d,
    (
        SELECT AVG(CAST(deltaSeconds AS BIGINT))
        FROM (
            SELECT DATEDIFF(SECOND, prevRequestUtc, pw.requestUtc) AS deltaSeconds
            FROM (
                SELECT
                    pv.requestUtc,
                    s.sessionKey,
                    LAG(pv.requestUtc) OVER (PARTITION BY s.sessionKey ORDER BY pv.requestUtc) AS prevRequestUtc
                FROM customizerVisitorPageview pv
                INNER JOIN customizerVisitorProfile vp ON vp.id = pv.visitorProfileFk
                INNER JOIN analyzerSession s
                    ON s.visitorProfileKey = vp.[key]
                    AND pv.requestUtc >= s.startUtc
                    AND pv.requestUtc <= s.lastActivityUtc
                WHERE pv.contentKey = @0
                  AND pv.requestUtc >= @3
            ) pw
            WHERE pw.prevRequestUtc IS NOT NULL
        ) deltas
    ) AS AvgTimeOnPageSeconds30d
FROM customizerVisitorPageview
WHERE contentKey = @0
  AND requestUtc >= @3
";

    private sealed class ProjectionRow
    {
        public int Pageviews24h { get; set; }
        public int Pageviews7d { get; set; }
        public int Pageviews30d { get; set; }
        public int UniqueVisitors30d { get; set; }
        public long? AvgTimeOnPageSeconds30d { get; set; }
    }
}
