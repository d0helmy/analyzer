namespace Analyzer.Features.Reporting.Domain;

/// <summary>
/// Internal projection carrying both the SQL aggregate result and a
/// "did we see any row?" flag so the controller can choose between
/// 200-with-zeros (content known to capture or cache) and 404 (known
/// to neither) per FR-RPT-010 / FR-RPT-011.
/// </summary>
/// <remarks>
/// Not part of the public surface — the repository returns this and
/// the query service composes the public
/// <c>ContentAnalyticsSnapshot</c> by combining it with the
/// tombstone-probe result. Kept internal so future changes to the
/// SQL projection shape are non-breaking.
/// </remarks>
internal sealed record ContentAnalyticsProjection(
    int Pageviews24h,
    int Pageviews7d,
    int Pageviews30d,
    int UniqueVisitors30d,
    long? AvgTimeOnPageSeconds30d,
    bool HasAnyCaptureRow);
