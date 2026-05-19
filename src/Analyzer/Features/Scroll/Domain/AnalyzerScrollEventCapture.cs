using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Scroll.Domain;

/// <summary>
/// Slice 006 — in-process command passed from
/// <c>AnalyzerScrollEventManagementController</c> to
/// <c>AnalyzerScrollEventCaptureHandler</c>. The controller parses the
/// request + builds the command; the handler owns identity gate,
/// payload validation, session resolution, persistence (with
/// duplicate-rejection), state-store append, and audit emission.
/// </summary>
/// <param name="Actor">
/// EntraID-resolved visitor identity from <c>IVisitorIdentifier</c>.
/// </param>
/// <param name="PageviewKey">
/// <c>customizerPageview.Key</c> for the pageview the client is
/// instrumenting. MUST be non-empty (handler-level validation); part
/// of the DB unique index <c>UX_analyzerScrollSample_pageviewBucket</c>
/// that enforces per-pageview-per-bucket idempotency.
/// </param>
/// <param name="ContentKey">
/// Umbraco content node hosting the pageview. Non-empty; non-FK
/// (tombstone tolerance — content may be deleted; the row stays).
/// </param>
/// <param name="Bucket">
/// Depth-milestone discriminator. MUST be a defined enum value
/// (Quarter / Half / ThreeQuarters / Full). The DB <c>CHECK</c>
/// constraint <c>CK_analyzerScrollSample_bucket</c> enforces the
/// value set at the storage layer.
/// </param>
/// <param name="UserAgent">
/// Raw <c>User-Agent</c> header from the controller's
/// <c>HttpContext</c>. Tolerates null / whitespace — the session
/// resolver hashes to a deterministic sentinel device key.
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request. Sourced from
/// the injected <see cref="System.TimeProvider"/>.
/// </param>
public sealed record AnalyzerScrollEventCapture(
    VisitorIdentity Actor,
    Guid PageviewKey,
    Guid ContentKey,
    AnalyzerScrollBucket Bucket,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
