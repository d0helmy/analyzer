using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application;

internal sealed class AnalyzerFormFieldEventAuditor : IAnalyzerFormFieldEventAuditor
{
    private readonly ILogger<AnalyzerFormFieldEventAuditor> _logger;

    public AnalyzerFormFieldEventAuditor(ILogger<AnalyzerFormFieldEventAuditor> logger) =>
        _logger = logger;

    public void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        Guid formKey,
        Guid fieldKey,
        AnalyzerFormFieldEventType eventType,
        bool? hadValue,
        DateTimeOffset receivedUtc)
    {
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} FormKey={FormKey} FieldKey={FieldKey} " +
            "EventType={EventType} HadValue={HadValue} At={ReceivedUtc}",
            Constants.AuditLog.FormFieldEventCapture,
            actor.Upn,
            actor.Oid,
            eventKey,
            formKey,
            fieldKey,
            eventType,
            hadValue,
            receivedUtc);
    }
}
