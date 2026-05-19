using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Search.Domain;

/// <summary>
/// Slice 007 — in-process command passed from
/// <c>AnalyzerSearchEventManagementController</c> to
/// <c>AnalyzerSearchEventCaptureHandler</c>. The controller parses the
/// request + resolves the visitor + builds the command; the handler
/// owns identity gate, normalisation, visitor-bound pageview check,
/// session resolution, persistence, state-store append, and audit
/// emission.
/// </summary>
/// <param name="Actor">
/// EntraID-resolved visitor identity from <c>IVisitorIdentifier</c>.
/// Carries <c>Key</c>, <c>Upn</c>, <c>Oid</c>, <c>IsAvailable</c> — the
/// handler runs the identity gate (Actor.IsAvailable + non-empty Key);
/// the auditor logs <c>ActorUpn</c> + <c>ActorOid</c> without a second
/// identity round-trip.
/// </param>
/// <param name="PageviewKey">
/// <c>customizerPageview.Key</c> for the pageview the client is
/// instrumenting. MUST be non-empty (handler-level validation); the
/// handler additionally verifies the pageview belongs to
/// <paramref name="Actor"/> via repo
/// <c>ResolvePageviewVisitorBindingAsync</c> (research §R3 —
/// strengthens defence vs slice 006).
/// </param>
/// <param name="ContentKey">
/// Umbraco content node hosting the pageview. Server-set from
/// <c>customizerPageview.contentKey</c> for the validated
/// <paramref name="PageviewKey"/> — the controller never trusts a
/// client-supplied content-key (defends against forged correlations
/// per the controller contract).
/// </param>
/// <param name="RawQuery">
/// Pre-normalisation user-typed string. 1-256 chars after trim
/// (controller validates; the handler defends in depth). PII per
/// FR-SRC-04.
/// </param>
/// <param name="ResultCount">
/// Non-negative integer reported by the host page script. 0 is the
/// explicit "no-results" derived view (FR-SRC-02).
/// </param>
/// <param name="UserAgent">
/// Raw <c>User-Agent</c> header from the controller's
/// <c>HttpContext</c>. Tolerates null / whitespace — the session
/// resolver hashes to a deterministic sentinel device key.
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request. Sourced from the
/// injected <see cref="System.TimeProvider"/>.
/// </param>
public sealed record AnalyzerSearchEventCapture(
    VisitorIdentity Actor,
    Guid PageviewKey,
    Guid ContentKey,
    string RawQuery,
    int ResultCount,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
