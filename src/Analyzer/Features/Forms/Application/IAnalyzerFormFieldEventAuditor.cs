using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 US2 — emits one structured audit-log entry per
/// successful field-event capture (FR-009). Named log properties
/// carry <c>FieldKey</c> and <c>HadValue</c> in addition to the
/// shared identity / target slots.
/// </summary>
internal interface IAnalyzerFormFieldEventAuditor
{
    void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        Guid formKey,
        Guid fieldKey,
        AnalyzerFormFieldEventType eventType,
        bool? hadValue,
        DateTimeOffset receivedUtc);
}
