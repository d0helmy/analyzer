namespace Analyzer.Features.Scroll.Web;

/// <summary>
/// Slice 006 — outbound JSON body on HTTP 202 from the scroll-event
/// management endpoint. Carries the new row's <c>eventKey</c> so the
/// client can correlate with subsequent calls (matches slice-004 /
/// slice-005 response shape).
/// </summary>
public sealed class AnalyzerScrollEventResponse
{
    public Guid EventKey { get; init; }
}
