using Analyzer.Reporting.ContentAnalytics;

namespace Analyzer.Features.Reporting.Application;

/// <summary>
/// Slice 008 — orchestrates the read-side aggregate query and the
/// tombstone probe into a single
/// <see cref="ContentAnalyticsSnapshot"/>. Returns <c>null</c> when
/// the content GUID is unknown to both the capture tables (30d
/// window) AND the published-content cache — the controller maps
/// that case to HTTP 404 per
/// <c>contracts/AnalyzerContentAnalyticsManagementController.md</c>.
/// </summary>
public interface IContentAnalyticsQueryService
{
    Task<ContentAnalyticsSnapshot?> GetAsync(Guid contentKey, CancellationToken ct);
}
