namespace Analyzer.Features.Forms.Web;

/// <summary>
/// Slice 005 — outbound JSON body on HTTP 202 from the form-event
/// management endpoint. Carries the new row's <c>eventKey</c> so the
/// client can correlate with subsequent calls.
/// </summary>
public sealed class AnalyzerFormEventResponse
{
    public Guid EventKey { get; init; }
}
