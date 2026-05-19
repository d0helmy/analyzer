using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 — emits a structured audit-log entry on every successful
/// form-lifecycle capture (FR-009). <c>ILogger</c>-backed (no dedicated
/// audit-log table per spec Assumption).
/// </summary>
internal interface IAnalyzerFormEventAuditor
{
    /// <summary>
    /// Emit one audit-log entry. Called only after the row has been
    /// persisted (no audit on validation failure). Named log
    /// properties: <c>AuditAction</c>, <c>ActorUpn</c>,
    /// <c>ActorOid</c>, <c>EventKey</c>, <c>FormKey</c>,
    /// <c>EventType</c>, <c>ReceivedUtc</c>.
    /// </summary>
    void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        Guid formKey,
        AnalyzerFormEventType eventType,
        DateTimeOffset receivedUtc);
}
