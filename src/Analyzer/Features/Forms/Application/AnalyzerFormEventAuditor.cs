using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 — <see cref="ILogger"/>-backed
/// <see cref="IAnalyzerFormEventAuditor"/>. Emits one structured
/// <c>LogInformation</c> entry per successful per-form lifecycle
/// capture (FR-009; matches slice-004's <c>CustomEventAuditor</c>
/// pattern + research §R6).
/// </summary>
internal sealed class AnalyzerFormEventAuditor : IAnalyzerFormEventAuditor
{
    private readonly ILogger<AnalyzerFormEventAuditor> _logger;

    public AnalyzerFormEventAuditor(ILogger<AnalyzerFormEventAuditor> logger) =>
        _logger = logger;

    public void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        Guid formKey,
        AnalyzerFormEventType eventType,
        DateTimeOffset receivedUtc)
    {
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} FormKey={FormKey} EventType={EventType} At={ReceivedUtc}",
            Constants.AuditLog.FormEventCapture,
            actor.Upn,
            actor.Oid,
            eventKey,
            formKey,
            eventType,
            receivedUtc);
    }
}
