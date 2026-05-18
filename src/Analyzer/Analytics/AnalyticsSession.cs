namespace Analyzer.Analytics;

/// <summary>
/// A bounded sequence of pageviews by one visitor on one device within
/// the configured inactivity timeout. The consumer-facing projection of
/// an <c>analyzerSession</c> row, surfaced through
/// <see cref="IAnalyticsEventStateProvider.CurrentSession"/> for
/// in-process consumers.
/// </summary>
/// <remarks>
/// <para>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyticsEventReceipt"/> and
/// <see cref="IAnalyticsEventStateProvider"/> so the pinning baseline
/// captures it directly. Breaking changes are PROHIBITED outside a
/// MAJOR release (Constitution Principle X).
/// </para>
/// <para>
/// The internal <c>deviceKey</c> column on the row (a truncated SHA-256
/// of the request <c>User-Agent</c>) is intentionally NOT exposed on
/// this record — it's a server-side resolution artefact, not a public
/// device dimension. Consumers attributing sessions to devices should
/// derive from a future receipt row's <c>UserAgent</c> column when a
/// later slice surfaces it.
/// </para>
/// </remarks>
/// <param name="SessionKey">
/// Publicly-exposed stable identifier; matches
/// <c>analyzerEventReceipt.sessionKey</c> on the receipt rows attributed
/// to this session.
/// </param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty —
/// the resolver short-circuits empty visitor keys (inherited from the
/// slice-002 handler's Guid.Empty skip).
/// </param>
/// <param name="StartUtc">
/// When the session opened — the <c>receivedUtc</c> of the first
/// pageview attached. Set once at insert; never changes.
/// </param>
/// <param name="LastActivityUtc">
/// When the most recent attached pageview was observed. Advances on
/// every <c>Extend</c> operation. For a closed session this is the
/// <c>receivedUtc</c> of the last pageview, NOT the <see cref="EndUtc"/>.
/// </param>
/// <param name="EndUtc">
/// Logical close time —
/// <c>LastActivityUtc + InactivityTimeoutMinutes</c>. NOT
/// <c>now</c> (sweeper observation time); the logical-close-time
/// semantic is load-bearing for session-duration analytics.
/// <c>null</c> while the session is still active.
/// </param>
/// <param name="PageviewCount">
/// Number of pageviews attached to the session. May exceed
/// <c>COUNT(*) FROM analyzerEventReceipt WHERE sessionKey = …</c> under
/// slice-002 back-pressure: if a receipt write op is dropped due to
/// queue capacity, the session's count still increments because
/// session resolution happens BEFORE the receipt enqueue.
/// </param>
/// <param name="IsActive">
/// True while the session is open; false once closed (lazy-close or
/// sweeper).
/// </param>
public sealed record AnalyticsSession(
    Guid SessionKey,
    Guid VisitorProfileKey,
    DateTimeOffset StartUtc,
    DateTimeOffset LastActivityUtc,
    DateTimeOffset? EndUtc,
    int PageviewCount,
    bool IsActive);
