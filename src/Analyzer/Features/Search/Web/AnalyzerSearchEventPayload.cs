using System.ComponentModel.DataAnnotations;

namespace Analyzer.Features.Search.Web;

/// <summary>
/// Slice 007 — inbound JSON payload for the search-event management
/// endpoint. Handler-level validation in
/// <c>AnalyzerSearchEventCaptureHandler</c> enforces the semantic
/// invariants the binder cannot express (visitor-bound
/// <c>PageviewKey</c>, non-empty normalised output).
/// </summary>
/// <remarks>
/// Field discipline: this DTO MUST NOT carry any property whose name
/// suggests result snippets, result URLs, click positions, or anything
/// beyond the <c>{query, count, pageview-correlation}</c> signal —
/// FR-SRC-04 PII parsimony. The server does NOT accept a client-
/// supplied <c>ContentKey</c>; that field is read from
/// <c>customizerPageview.contentKey</c> for the validated
/// <c>PageviewKey</c> (defends against forged correlations).
/// </remarks>
public sealed class AnalyzerSearchEventPayload
{
    /// <summary>
    /// <c>customizerPageview.Key</c> for the pageview the search was
    /// performed on. Server validates that this pageview belongs to
    /// the resolved visitor (research §R3 + FR-008).
    /// </summary>
    [Required]
    public Guid PageviewKey { get; init; }

    /// <summary>
    /// Pre-normalisation user-typed query. Length 1-256 after trim.
    /// </summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Non-negative result count. <c>0</c> is the explicit "no-results"
    /// derived view (FR-SRC-02). Sanity-capped at 1,000,000 (any host
    /// reporting more is misbehaving).
    /// </summary>
    [Required]
    [Range(0, 1_000_000)]
    public int ResultCount { get; init; }
}
