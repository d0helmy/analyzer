using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Scroll.Application;

/// <summary>
/// Slice 006 — emits structured audit-log entries on the capture path.
/// Two overloads:
/// <list type="bullet">
///   <item><see cref="AuditAccepted"/> — successful insert (HTTP 202).</item>
///   <item><see cref="AuditDuplicate"/> — unique-index idempotency
///     rejection (HTTP 409). Operationally interesting because a
///     duplicate POST signals a buggy client.</item>
/// </list>
/// <see cref="ILogger"/>-backed (no dedicated audit-log table per
/// spec Assumption). Both overloads use the same
/// <see cref="Constants.AuditLog.ScrollEventCapture"/> action; the
/// <c>Duplicate</c> tag distinguishes the 409 path in logs.
/// </summary>
internal interface IAnalyzerScrollEventAuditor
{
    /// <summary>
    /// Emit one accepted-capture entry. Called only after the row has
    /// landed in <c>analyzerScrollSample</c>.
    /// </summary>
    void AuditAccepted(
        VisitorIdentity actor,
        Guid eventKey,
        Guid pageviewKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc);

    /// <summary>
    /// Emit one <c>Duplicate</c>-tagged entry. Called when the
    /// unique-index <c>UX_analyzerScrollSample_pageviewBucket</c>
    /// rejected the insert. The <c>EventKey</c> argument is the Guid
    /// the handler had generated for the attempted insert (NOT the
    /// already-existing row's eventKey — we deliberately do not
    /// look that up to keep the path cheap).
    /// </summary>
    void AuditDuplicate(
        VisitorIdentity actor,
        Guid attemptedEventKey,
        Guid pageviewKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc);
}
