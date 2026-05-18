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
/// </remarks>
internal interface IAnalyzerSessionResolver
{
    /// <summary>
    /// Resolve the session this pageview belongs to. Either extends an
    /// in-progress active session for the visitor+device, closes a
    /// stale one and opens a new session, or opens a fresh session if
    /// none exists.
    /// </summary>
    /// <param name="visitorProfileKey">
    /// Customizer-resolved visitor key. MUST be non-empty (caller
    /// guarantees; the slice-002 handler short-circuits empty keys).
    /// </param>
    /// <param name="userAgent">
    /// Raw <c>User-Agent</c> carried on the immutable
    /// <c>Pageview</c> record by the <c>PageviewCaptured</c>
    /// notification (cross-product prereq at customizer
    /// <c>5273c38</c>). NOT read from <c>IHttpContextAccessor</c> —
    /// that path is unreliable under typical fire-and-forget timing
    /// (lesson #40 / analysis C1). Null / whitespace tolerated —
    /// hashed to a deterministic sentinel device key.
    /// </param>
    /// <param name="receivedUtc">
    /// When the handler observed the notification. Set on the new /
    /// extended session row as <c>lastActivityUtc</c> (and as
    /// <c>startUtc</c> on fresh sessions).
    /// </param>
    /// <param name="ct">Cancellation token from the handler chain.</param>
    /// <returns>
    /// <see cref="SessionResolutionResult"/> carrying both the session
    /// key (handle for the receipt FK) and the in-flight
    /// <see cref="AnalyticsSession"/> projection (for the state store).
    /// </returns>
    ValueTask<SessionResolutionResult> ResolveAsync(
        Guid visitorProfileKey,
        string? userAgent,
        DateTimeOffset receivedUtc,
        CancellationToken ct);
}

/// <summary>
/// Resolver return: the session's stable handle plus the consumer-facing
/// projection the handler writes to the request-scoped state store.
/// Zero-allocation hot-path return — <c>readonly record struct</c>.
/// </summary>
internal readonly record struct SessionResolutionResult(
    Guid SessionKey,
    AnalyticsSession Projection);
