using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 — <see cref="ILogger"/>-backed
/// <see cref="ICustomEventAuditor"/>. Emits one structured
/// <c>LogInformation</c> entry per successful custom-event capture
/// with named properties so operator-side log shipping can index every
/// field without parsing free-text. FR-008 + research §5.
/// </summary>
internal sealed class CustomEventAuditor : ICustomEventAuditor
{
    private readonly ILogger<CustomEventAuditor> _logger;

    public CustomEventAuditor(ILogger<CustomEventAuditor> logger) =>
        _logger = logger;

    public void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        string category,
        string action,
        DateTimeOffset receivedUtc)
    {
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} Category={Category} Action={Action} At={ReceivedUtc}",
            Constants.AuditLog.CustomEventCapture,
            actor.Upn,
            actor.Oid,
            eventKey,
            category,
            action,
            receivedUtc);
    }
}
