using Analyzer.Analytics;

namespace Analyzer.Features.Scroll.Web;

/// <summary>
/// Slice 006 — inbound JSON payload for the scroll-milestone
/// management endpoint. Handler-level validation in
/// <c>AnalyzerScrollEventCaptureHandler</c> enforces the semantic
/// invariants the binder cannot express
/// (<c>PageviewKey</c> non-empty, <c>Bucket</c> defined enum value).
/// </summary>
/// <remarks>
/// Field discipline: this DTO MUST NOT carry any property whose name
/// suggests page content, raw scroll position, or anything beyond the
/// milestone-crossed signal (<c>*Position</c>, <c>*Pixels</c>,
/// <c>*Selector</c>, etc.). The system captures
/// <em>crossed-a-bucket</em>, not <em>scrolled-to-pixel-X</em>; the
/// minimal payload is the privacy / capture-scope contract.
/// </remarks>
public sealed class AnalyzerScrollEventPayload
{
    public Guid PageviewKey { get; init; }

    public Guid ContentKey { get; init; }

    public AnalyzerScrollBucket Bucket { get; init; }
}
