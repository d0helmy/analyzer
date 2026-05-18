using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 — emits a structured audit-log entry on every successful
/// custom-event capture. <c>ILogger</c>-backed (no dedicated
/// <c>analyzerAuditLog</c> table per spec Assumption). FR-008.
/// </summary>
/// <remarks>
/// Internal contract; slice 005's content-app actions may define their
/// own audit action names + reuse this shape — rename to a generalised
/// <c>IAnalyzerAuditor</c> if that broader use lands; for slice 004,
/// the narrower name keeps coupling tight.
/// </remarks>
internal interface ICustomEventAuditor
{
    /// <summary>
    /// Emit one audit-log entry. Called only after the row has been
    /// persisted (US3 AS4 — no audit entry on validation failure).
    /// Named log properties: <c>AuditAction</c>, <c>ActorUpn</c>,
    /// <c>ActorOid</c>, <c>EventKey</c>, <c>Category</c>, <c>Action</c>,
    /// <c>ReceivedUtc</c>.
    /// </summary>
    void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        string category,
        string action,
        DateTimeOffset receivedUtc);
}
