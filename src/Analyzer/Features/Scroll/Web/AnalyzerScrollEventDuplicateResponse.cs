namespace Analyzer.Features.Scroll.Web;

/// <summary>
/// Slice 006 — outbound JSON body on HTTP 409 from the scroll-event
/// management endpoint when the unique-index
/// <c>UX_analyzerScrollSample_pageviewBucket</c> rejected the insert
/// (a same-tuple POST replay). Distinct shape from
/// <see cref="AnalyzerScrollEventResponse"/> so a TypeScript caller
/// can branch on response status without inspecting field presence.
/// </summary>
public sealed class AnalyzerScrollEventDuplicateResponse
{
    /// <summary>
    /// Always <c>"duplicate"</c>. Stable machine-readable discriminator
    /// for client-side handling.
    /// </summary>
    public string Code { get; init; } = "duplicate";
}
