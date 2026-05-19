using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Search.Application;

/// <summary>
/// Slice 007 — emits structured audit-log entries on the
/// search-event capture path (FR-009 / SC-006). Single overload —
/// there is no 409 / duplicate path (search events have no
/// idempotency unique index per research §R7).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Microsoft.Extensions.Logging.ILogger"/>-backed (no
/// dedicated audit-log table per spec Assumption).
/// </para>
/// <para>
/// <b>PII redaction is load-bearing (FR-SRC-04 + SC-006)</b>: the log
/// template MUST NOT carry <c>RawQuery</c> or <c>NormalisedQuery</c>
/// in any form — neither as a parameter, nor as a placeholder, nor as
/// a substring of any other field. The DB row is the canonical,
/// role-gated record of the literal query text; the log substrate is
/// operationally accessible to ops/SRE staff who should not see PII.
/// </para>
/// </remarks>
internal interface IAnalyzerSearchEventAuditor
{
    /// <summary>
    /// Emit one accepted-capture entry. Called only after the row has
    /// landed in <c>analyzerSearchEvent</c>.
    /// </summary>
    /// <param name="actor">Resolved EntraID visitor identity.</param>
    /// <param name="eventKey">Persisted row's <c>eventKey</c>.</param>
    /// <param name="pageviewKey">Visitor-bound pageview correlation.</param>
    /// <param name="resultCount">Reported result count.</param>
    /// <param name="receivedUtc">Capture timestamp.</param>
    void AuditAccepted(
        VisitorIdentity actor,
        Guid eventKey,
        Guid pageviewKey,
        int resultCount,
        DateTimeOffset receivedUtc);
}
