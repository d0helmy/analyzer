namespace Analyzer.Features.CustomEvents.Web;

/// <summary>
/// Slice 004 — outbound JSON body on HTTP 202; carries the new row's
/// publicly-exposed <c>eventKey</c>. Matches Clarification §2's
/// JS-side <c>Promise&lt;{ eventKey: string }&gt;</c> shape.
/// </summary>
public sealed class CustomEventResponse
{
    public Guid EventKey { get; init; }
}
