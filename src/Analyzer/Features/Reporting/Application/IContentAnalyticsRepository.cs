using Analyzer.Features.Reporting.Domain;

namespace Analyzer.Features.Reporting.Application;

/// <summary>
/// Slice 008 — read-only repository for the per-content-node
/// aggregate projection consumed by
/// <see cref="IContentAnalyticsQueryService"/>.
/// </summary>
internal interface IContentAnalyticsRepository
{
    /// <summary>
    /// Runs the single-pass aggregate query against
    /// <c>customizerVisitorPageview</c> + <c>analyzerSession</c> for
    /// <paramref name="contentKey"/>, bounded by the request-time
    /// window anchor <paramref name="windowEndUtc"/>. Always returns
    /// a projection; zero metrics are not signalled with null.
    /// </summary>
    Task<ContentAnalyticsProjection> GetAsync(
        Guid contentKey,
        DateTimeOffset windowEndUtc,
        CancellationToken ct);
}
