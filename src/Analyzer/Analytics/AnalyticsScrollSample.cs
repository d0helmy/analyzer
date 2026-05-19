namespace Analyzer.Analytics;

/// <summary>
/// Slice 006 — immutable projection of an <c>analyzerScrollSample</c>
/// row. Returned by
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestScrollEvents"/>
/// for in-process consumers within the request scope, and through the
/// eventual read-side reporting API that drives the per-content-node
/// scroll heatmap (<c>FR-HMP-01</c>; deferred to a future slice).
/// </summary>
/// <remarks>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyticsCustomEvent"/>, <see cref="AnalyticsFormEvent"/>,
/// and <see cref="AnalyticsFormFieldEvent"/>. Breaking changes
/// PROHIBITED outside a MAJOR release (Constitution Principle X).
/// </remarks>
/// <param name="EventKey">
/// Publicly-exposed stable identifier; matches the DB row's
/// <c>eventKey</c>. Returned by the management endpoint's HTTP 202
/// body.
/// </param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty.
/// </param>
/// <param name="SessionKey">
/// Soft FK to <see cref="AnalyticsSession.SessionKey"/>. NULL allowed
/// (pre-sessions cohort + back-pressure-drop posture, matching slice
/// 002 receipt + slice 005 form events).
/// </param>
/// <param name="PageviewKey">
/// Soft FK to <c>customizerPageview.Key</c>. Tombstone tolerance per
/// slice-002 precedent — Customizer may anonymise the pageview row
/// through its own cascade independently of this row's lifetime.
/// Always non-empty (the unique index
/// <c>UX_analyzerScrollSample_pageviewBucket</c> requires it).
/// </param>
/// <param name="ContentKey">
/// Umbraco content node hosting the pageview. Non-FK — tombstone
/// tolerance per slice-002 precedent.
/// </param>
/// <param name="Bucket">
/// <see cref="AnalyzerScrollBucket"/> discriminator — exactly one of
/// <c>Quarter</c> (25), <c>Half</c> (50), <c>ThreeQuarters</c> (75),
/// <c>Full</c> (100). The DB <c>CHECK</c> constraint
/// <c>CK_analyzerScrollSample_bucket</c> enforces the value set at the
/// storage layer.
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request. Sourced from
/// injected <see cref="System.TimeProvider"/>.
/// </param>
public sealed record AnalyticsScrollSample(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid PageviewKey,
    Guid ContentKey,
    AnalyzerScrollBucket Bucket,
    DateTimeOffset ReceivedUtc);
