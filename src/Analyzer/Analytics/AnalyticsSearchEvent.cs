namespace Analyzer.Analytics;

/// <summary>
/// Slice 007 — immutable projection of an <c>analyzerSearchEvent</c>
/// row. Returned by
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestSearchEvents"/>
/// for in-process consumers within the request scope, and through the
/// eventual read-side reporting API that drives the search-aggregation
/// report (<c>FR-SRC-01</c>; deferred to a future slice).
/// </summary>
/// <remarks>
/// <para>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyticsCustomEvent"/>, <see cref="AnalyticsFormEvent"/>,
/// <see cref="AnalyticsFormFieldEvent"/>, and
/// <see cref="AnalyticsScrollSample"/>. Breaking changes PROHIBITED
/// outside a MAJOR release (Constitution Principle X).
/// </para>
/// <para>
/// <b>PII notice (FR-SRC-04)</b>: <see cref="RawQuery"/> and
/// <see cref="NormalisedQuery"/> are potentially personal data (e.g.
/// names of colleagues searched for, sensitive topics). Read-side
/// surfaces exposing these fields MUST be role-gated per
/// <c>NFR-SEC-05</c>. Audit-log substrates MUST NOT receive either
/// value — see <c>AnalyzerSearchEventAuditor</c>'s redaction-by-design
/// (SC-006). The anonymisation cascade hard-deletes rather than
/// re-keying (spec Clarifications §2), so consumers MUST NOT expect a
/// re-keyed <see cref="AnalyticsSearchEvent"/> for an anonymised
/// visitor — the row is removed entirely.
/// </para>
/// </remarks>
public sealed record AnalyticsSearchEvent
{
    /// <summary>
    /// Publicly-exposed stable identifier; matches the DB row's
    /// <c>eventKey</c>. Returned by the management endpoint's HTTP 202
    /// body.
    /// </summary>
    public required Guid EventKey { get; init; }

    /// <summary>
    /// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty
    /// (identity gate rejects empty keys at the controller).
    /// </summary>
    public required Guid VisitorProfileKey { get; init; }

    /// <summary>
    /// Hard FK to <see cref="AnalyticsSession.SessionKey"/>. NOT nullable
    /// — search-event capture resolves a session synchronously (slice
    /// 003 contract).
    /// </summary>
    public required Guid SessionKey { get; init; }

    /// <summary>
    /// Soft FK to <c>customizerPageview.Key</c>. Tombstone tolerance
    /// per slice-002 precedent. Visitor-bound at the controller layer
    /// (research §R3) — the pageview must belong to the resolved
    /// visitor, otherwise the POST is rejected with 400 before this
    /// projection is produced.
    /// </summary>
    public required Guid PageviewKey { get; init; }

    /// <summary>
    /// Umbraco content node hosting the pageview. Server-set from
    /// <c>customizerPageview.contentKey</c> at write time (denormalised
    /// for fast per-content-node lookup). Non-FK — tombstone tolerance
    /// per slice-002 precedent.
    /// </summary>
    public required Guid ContentKey { get; init; }

    /// <summary>
    /// Pre-normalisation user-typed string. 1-256 chars after trim.
    /// <b>PII-sensitive per FR-SRC-04</b> — never logged; read-side
    /// surfaces exposing it MUST be role-gated.
    /// </summary>
    public required string RawQuery { get; init; }

    /// <summary>
    /// Output of <see cref="IAnalyzerSearchQueryNormaliser"/> at capture
    /// time. The grouping key for "top queries" aggregations.
    /// <b>Also PII-sensitive per FR-SRC-04</b>.
    /// </summary>
    public required string NormalisedQuery { get; init; }

    /// <summary>
    /// Non-negative integer. <c>0</c> is the explicit "no-results"
    /// derived view (FR-SRC-02).
    /// </summary>
    public required int ResultCount { get; init; }

    /// <summary>
    /// When the management endpoint observed the request. Sourced from
    /// the injected <see cref="System.TimeProvider"/>.
    /// </summary>
    public required DateTimeOffset ReceivedUtc { get; init; }
}
