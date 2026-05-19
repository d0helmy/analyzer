using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Scroll.Application;

/// <summary>
/// Slice 006 — <see cref="ILogger"/>-backed
/// <see cref="IAnalyzerScrollEventAuditor"/>. Emits structured
/// <c>LogInformation</c> entries on the capture path; the
/// <c>Disposition</c> field distinguishes <c>Accepted</c> (202) from
/// <c>Duplicate</c> (409) so log shippers can split / count
/// independently.
/// </summary>
internal sealed class AnalyzerScrollEventAuditor : IAnalyzerScrollEventAuditor
{
    private const string AcceptedDisposition = "Accepted";
    private const string DuplicateDisposition = "Duplicate";

    private readonly ILogger<AnalyzerScrollEventAuditor> _logger;

    public AnalyzerScrollEventAuditor(ILogger<AnalyzerScrollEventAuditor> logger) =>
        _logger = logger;

    public void AuditAccepted(
        VisitorIdentity actor,
        Guid eventKey,
        Guid pageviewKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc) =>
        Emit(actor, eventKey, pageviewKey, bucket, receivedUtc, AcceptedDisposition);

    public void AuditDuplicate(
        VisitorIdentity actor,
        Guid attemptedEventKey,
        Guid pageviewKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc) =>
        Emit(actor, attemptedEventKey, pageviewKey, bucket, receivedUtc, DuplicateDisposition);

    private void Emit(
        VisitorIdentity actor,
        Guid eventKey,
        Guid pageviewKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc,
        string disposition) =>
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} PageviewKey={PageviewKey} Bucket={Bucket} " +
            "Disposition={Disposition} At={ReceivedUtc}",
            Constants.AuditLog.ScrollEventCapture,
            actor.Upn,
            actor.Oid,
            eventKey,
            pageviewKey,
            bucket,
            disposition,
            receivedUtc);
}
