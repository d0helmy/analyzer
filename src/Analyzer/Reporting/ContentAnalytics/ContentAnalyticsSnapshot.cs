namespace Analyzer.Reporting.ContentAnalytics;

/// <summary>
/// Aggregate read-side view of one content node's usage. Returned by
/// the per-content-node Analytics management endpoint (slice 008).
/// Computed on demand from existing capture tables (Analyzer slice
/// 002 + Customizer slice 003); not persisted.
/// </summary>
/// <remarks>
/// <para>
/// Privacy invariant — this DTO carries <b>no</b> field that
/// references an individual visitor. It contains aggregate counters,
/// an average duration, a content GUID, the request-time window
/// anchor, and a tombstone-state boolean. Any future addition that
/// would surface per-visitor data MUST be gated by
/// <c>IIndividualDataAccessCheck</c> and either omitted entirely
/// from the JSON or shaped so that the absent state is not
/// distinguishable from a non-privileged user. See
/// <c>contracts/ContentAnalyticsSnapshot.md § Forward compatibility</c>.
/// </para>
/// </remarks>
/// <param name="ContentKey">Node this snapshot describes (route echo).</param>
/// <param name="WindowEndUtc">UTC instant the three time windows are
/// anchored to. Sourced from <c>TimeProvider.GetUtcNow()</c> at
/// handler entry; all three window predicates derive from this one
/// value so successive metrics remain mutually consistent.</param>
/// <param name="Pageviews24h">Count of pageview rows where
/// <c>requestUtc &gt;= WindowEndUtc - 24h</c>. Always
/// <c>&lt;= Pageviews7d</c>.</param>
/// <param name="Pageviews7d">Count where <c>requestUtc &gt;= WindowEndUtc - 7d</c>.</param>
/// <param name="Pageviews30d">Count where <c>requestUtc &gt;= WindowEndUtc - 30d</c>.</param>
/// <param name="UniqueVisitors30d">Distinct <c>visitorProfileFk</c>
/// values in the 30d window — anonymised visitors continue to
/// contribute (FR-RPT-009).</param>
/// <param name="AvgTimeOnPageSeconds30d"><c>null</c> when zero
/// sessions on this node within 30d have at least two pageviews
/// (no successor delta to average). Otherwise non-negative.</param>
/// <param name="IsContentCurrentlyTombstoned"><c>true</c> when the
/// content is currently absent from <c>IPublishedContentCache</c>
/// (unpublished or recycled) but at least one historical pageview
/// exists. See Spec Clarifications §3 — present-state semantic.</param>
/// <param name="TopReferrers30d">Always empty in MVP. Placeholder
/// for the future click-through slice.</param>
public sealed record ContentAnalyticsSnapshot(
    Guid ContentKey,
    DateTimeOffset WindowEndUtc,
    int Pageviews24h,
    int Pageviews7d,
    int Pageviews30d,
    int UniqueVisitors30d,
    long? AvgTimeOnPageSeconds30d,
    bool IsContentCurrentlyTombstoned,
    IReadOnlyList<string> TopReferrers30d);
