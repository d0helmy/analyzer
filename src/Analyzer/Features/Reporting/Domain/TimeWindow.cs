namespace Analyzer.Features.Reporting.Domain;

/// <summary>
/// Labels for the three windows surfaced by
/// <c>ContentAnalyticsSnapshot</c>. The repository accepts
/// <see cref="DateTimeOffset"/> bounds directly — this enum exists
/// only to tag application-layer code paths, not to ride into the
/// SQL layer.
/// </summary>
internal enum TimeWindow
{
    TwentyFourHours,
    SevenDays,
    ThirtyDays,
}
