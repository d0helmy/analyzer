namespace Analyzer.Features.Search.Web;

/// <summary>
/// Slice 007 — outbound JSON body on HTTP 202 from the search-event
/// management endpoint. Carries the new row's <c>eventKey</c> so the
/// client can correlate (matches slice-004 / 005 / 006 response shape).
/// </summary>
/// <remarks>
/// Does NOT carry <c>RawQuery</c>, <c>NormalisedQuery</c>, or any
/// query-derived data — PII parsimony per FR-SRC-04. The caller
/// already has the raw query (it sent it); the normalised form is an
/// internal grouping key the read-side reporting slice will surface
/// through a role-gated API.
/// </remarks>
public sealed class AnalyzerSearchEventResponse
{
    public Guid EventKey { get; init; }
}
