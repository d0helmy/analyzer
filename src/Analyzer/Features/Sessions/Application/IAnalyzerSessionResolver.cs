using Analyzer.Analytics;

namespace Analyzer.Features.Sessions.Application;

/// <summary>
/// Slice 003 — resolves the session a captured pageview belongs to.
/// Called synchronously inside <c>PageviewCapturedHandler</c> before
/// the receipt is enqueued, so the receipt's <c>sessionKey</c> FK is
/// durable by the time the dispatcher inserts the receipt row.
/// </summary>
/// <remarks>
/// Internal contract (NOT pinned per slice-002 Clarifications Q3;
/// implementation under <c>Analyzer.Features.Sessions.*</c>). A
/// third-party MAY swap the implementation via
/// <c>services.Replace&lt;IAnalyzerSessionResolver, …&gt;(...)</c> —
/// the behaviour-compatibility contract is documented in
/// <c>specs/003-session-tracking/contracts/AnalyzerSessionResolver.md</c>.
/// Slice 004 added an <see cref="SessionActivityKind"/> parameter so the
/// resolver dispatches to <c>ExtendAsync</c> (pageview; increments
/// <c>pageviewCount</c>) or <c>TouchAsync</c> (custom event; activity
/// only) on the cache-/DB-hit path.
/// </remarks>
internal interface IAnalyzerSessionResolver
{
    /// <summary>
    /// Resolve the session this activity belongs to. Either extends or
    /// touches an in-progress active session for the visitor+device,
    /// closes a stale one and opens a new session, or opens a fresh
    /// session if none exists.
    /// </summary>
    /// <param name="visitorProfileKey">
    /// Customizer-resolved visitor key. MUST be non-empty (caller
    /// guarantees; the slice-002 handler short-circuits empty keys).
    /// </param>
    /// <param name="userAgent">
    /// Raw <c>User-Agent</c> carried on the immutable <c>Pageview</c>
    /// record by the <c>PageviewCaptured</c> notification (cross-product
    /// prereq at customizer <c>5273c38</c>); for slice 004 custom-event
    /// flows, read by the controller from the request <c>HttpContext</c>
    /// (live on the request thread). Null / whitespace tolerated —
    /// hashed to a deterministic sentinel device key.
    /// </param>
    /// <param name="receivedUtc">
    /// When the caller observed the activity. Set on the new /
    /// extended session row as <c>lastActivityUtc</c> (and as
    /// <c>startUtc</c> on fresh sessions).
    /// </param>
    /// <param name="activityKind">
    /// Slice 004 — selects the activity-advancement semantic on the
    /// cache-/DB-hit path: <see cref="SessionActivityKind.Pageview"/>
    /// invokes <c>ExtendAsync</c> (advances <c>lastActivityUtc</c>
    /// AND increments <c>pageviewCount</c>);
    /// <see cref="SessionActivityKind.CustomEvent"/> invokes
    /// <c>TouchAsync</c> (advances <c>lastActivityUtc</c> only). On
    /// the open-new-session path the value is irrelevant: every fresh
    /// session row starts with <c>pageviewCount = 1</c>.
    /// </param>
    /// <param name="ct">Cancellation token from the caller's chain.</param>
    /// <returns>
    /// <see cref="SessionResolutionResult"/> carrying both the session
    /// key and the in-flight <see cref="AnalyticsSession"/> projection
    /// (for the state store).
    /// </returns>
    ValueTask<SessionResolutionResult> ResolveAsync(
        Guid visitorProfileKey,
        string? userAgent,
        DateTimeOffset receivedUtc,
        SessionActivityKind activityKind,
        CancellationToken ct);
}

/// <summary>
/// Slice 004 — selects the activity-advancement semantic the resolver
/// applies when the session row already exists.
/// </summary>
internal enum SessionActivityKind
{
    /// <summary>
    /// Pageview — advance <c>lastActivityUtc</c> AND increment
    /// <c>pageviewCount</c> (slice 003 behaviour).
    /// </summary>
    Pageview = 0,

    /// <summary>
    /// Custom event — advance <c>lastActivityUtc</c> only;
    /// <c>pageviewCount</c> stays unchanged (Clarification §1; the
    /// Mixpanel/GA "engagement keeps the session alive" pattern).
    /// </summary>
    CustomEvent = 1,

    /// <summary>
    /// Slice 005 — form impression: passive observation, does NOT
    /// advance <c>lastActivityUtc</c>. The resolver returns the
    /// current session for FK linkage but issues no UPDATE. Edge case
    /// "Impressions are passive" (spec) — an impression alone must
    /// not keep a session alive past the inactivity timeout.
    /// </summary>
    FormImpression = 2,

    /// <summary>
    /// Slice 006 — scroll-depth milestone crossing: visitor reached
    /// a 25 / 50 / 75 / 100 % depth bucket. Same semantic as
    /// <see cref="CustomEvent"/> — intentional engagement,
    /// <c>TouchAsync</c> advances <c>lastActivityUtc</c> without
    /// incrementing <c>pageviewCount</c>. Distinct from
    /// <see cref="FormImpression"/> which is passive.
    /// </summary>
    ScrollEvent = 3,

    /// <summary>
    /// Slice 007 — internal search submission: visitor invoked
    /// <c>analyzer.sendSearch(query, resultCount)</c>. Same semantic as
    /// <see cref="CustomEvent"/> / <see cref="ScrollEvent"/> —
    /// intentional engagement, <c>TouchAsync</c> advances
    /// <c>lastActivityUtc</c> without incrementing
    /// <c>pageviewCount</c>.
    /// </summary>
    SearchEvent = 4,
}

/// <summary>
/// Resolver return: the session's stable handle plus the consumer-facing
/// projection the handler writes to the request-scoped state store.
/// Zero-allocation hot-path return — <c>readonly record struct</c>.
/// </summary>
internal readonly record struct SessionResolutionResult(
    Guid SessionKey,
    AnalyticsSession Projection);
