using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Search.Application;

/// <summary>
/// Slice 007 — <see cref="ILogger"/>-backed
/// <see cref="IAnalyzerSearchEventAuditor"/>. Emits one structured
/// <c>LogInformation</c> entry per accepted capture, carrying
/// <c>EventKey</c>, <c>PageviewKey</c>, <c>ResultCount</c>,
/// <c>ActorUpn</c>, <c>ActorOid</c>, <c>ReceivedUtc</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>PII redaction (FR-SRC-04 + SC-006)</b>: the template DOES NOT
/// contain <c>{RawQuery}</c> or <c>{NormalisedQuery}</c> placeholders
/// — even as unused parameters — defending against future accidental
/// log-overload introducing them. The DB row is the canonical record
/// of the literal query text; the log substrate is operationally
/// accessible (Serilog / App Insights) to staff who should not see PII.
/// </para>
/// </remarks>
internal sealed class AnalyzerSearchEventAuditor : IAnalyzerSearchEventAuditor
{
    private const string AcceptedDisposition = "Accepted";

    private readonly ILogger<AnalyzerSearchEventAuditor> _logger;

    public AnalyzerSearchEventAuditor(ILogger<AnalyzerSearchEventAuditor> logger) =>
        _logger = logger;

    public void AuditAccepted(
        VisitorIdentity actor,
        Guid eventKey,
        Guid pageviewKey,
        int resultCount,
        DateTimeOffset receivedUtc) =>
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} PageviewKey={PageviewKey} ResultCount={ResultCount} " +
            "Disposition={Disposition} At={ReceivedUtc}",
            Constants.AuditLog.SearchEventCapture,
            actor.Upn,
            actor.Oid,
            eventKey,
            pageviewKey,
            resultCount,
            AcceptedDisposition,
            receivedUtc);
}
