using Analyzer.Analytics;

namespace Analyzer.Features.Forms.Web;

/// <summary>
/// Slice 005 — inbound JSON payload for the per-form lifecycle
/// management endpoint. Handler-level validation in
/// <c>AnalyzerFormEventCaptureHandler</c> enforces the semantic
/// invariants the binder cannot express (timing-slot/event-type
/// correspondence; the no-client-Abandon rule).
/// </summary>
/// <remarks>
/// Privacy invariant (SC-003): this DTO MUST NOT carry any property
/// whose name suggests field content. Adding a <c>*Value</c>,
/// <c>*Content</c>, or <c>*Text</c> property here breaches the
/// slice's privacy contract — gate via PR review.
/// </remarks>
public sealed class AnalyzerFormEventPayload
{
    public Guid FormKey { get; init; }

    public Guid ContentKey { get; init; }

    public AnalyzerFormEventType EventType { get; init; }

    public int? ElapsedMsFromImpression { get; init; }

    public int? ElapsedMsFromStart { get; init; }
}
