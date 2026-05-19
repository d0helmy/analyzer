using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Forms.Domain;

/// <summary>
/// Slice 005 — in-process command passed from
/// <c>AnalyzerFormEventManagementController</c> to
/// <c>AnalyzerFormEventCaptureHandler</c>. The controller parses the
/// request + builds the command; the handler owns identity gate,
/// payload validation, session resolution, persistence, state-store
/// append, and audit emission.
/// </summary>
/// <param name="Actor">
/// EntraID-resolved visitor identity from <c>IVisitorIdentifier</c>.
/// </param>
/// <param name="FormKey">
/// Umbraco Forms <c>Form.Id</c>. MUST be non-empty
/// (handler-level validation).
/// </param>
/// <param name="ContentKey">
/// Umbraco content node hosting the form impression. Non-empty;
/// non-FK (tombstone tolerance).
/// </param>
/// <param name="EventType">
/// Lifecycle discriminator. <c>Abandon</c> NEVER enters this command —
/// abandons are materialised by the sweeper, not POSTed.
/// </param>
/// <param name="ElapsedMsFromImpression">
/// Required (≥ 0) on <c>Start</c>; MUST be null on the other types.
/// </param>
/// <param name="ElapsedMsFromStart">
/// Required (≥ 0) on <c>Success</c>; MUST be null on
/// <c>Impression</c> and <c>Start</c>.
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
public sealed record AnalyzerFormEventCapture(
    VisitorIdentity Actor,
    Guid FormKey,
    Guid ContentKey,
    AnalyzerFormEventType EventType,
    int? ElapsedMsFromImpression,
    int? ElapsedMsFromStart,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
