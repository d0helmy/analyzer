using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Scroll.Application;

/// <summary>
/// Slice 006 — orchestrates one in-request scroll-milestone capture.
/// Identity gate (FR-008) → payload validation (FR-006 / FR-007) →
/// session resolution (slice 003, dispatched as
/// <see cref="SessionActivityKind.ScrollEvent"/>) → repo insert (DB
/// unique index enforces idempotency; the typed
/// <see cref="ScrollSampleDuplicateException"/> bubbles to the
/// controller for HTTP 409) → state-store append (only on accepted
/// path) → audit emit (accepted on 202; <c>Duplicate</c>-tagged on
/// 409).
/// </summary>
internal sealed class AnalyzerScrollEventCaptureHandler : IAnalyzerScrollEventCaptureHandler
{
    private readonly IAnalyzerSessionResolver _resolver;
    private readonly IAnalyzerScrollSampleRepository _repository;
    private readonly AnalyticsEventStateStore _stateStore;
    private readonly IAnalyzerScrollEventAuditor _auditor;
    private readonly ILogger<AnalyzerScrollEventCaptureHandler> _logger;

    public AnalyzerScrollEventCaptureHandler(
        IAnalyzerSessionResolver resolver,
        IAnalyzerScrollSampleRepository repository,
        AnalyticsEventStateStore stateStore,
        IAnalyzerScrollEventAuditor auditor,
        ILogger<AnalyzerScrollEventCaptureHandler> logger)
    {
        _resolver = resolver;
        _repository = repository;
        _stateStore = stateStore;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(AnalyzerScrollEventCapture command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1) Identity gate — defensive; the controller already rejects
        //    the unauthorised case before we get here.
        if (!command.Actor.IsAvailable || command.Actor.Key == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Scroll-event capture requires an authenticated EntraID actor.");
        }

        // 2) Payload validation — semantic invariants the model binder
        //    cannot express.
        ValidatePayload(command);

        // 3) Session resolution — scroll milestones are engagement;
        //    dispatch via ScrollEvent (Touch — advances lastActivityUtc
        //    only).
        var resolution = await _resolver.ResolveAsync(
            command.Actor.Key,
            command.UserAgent,
            command.ReceivedUtc,
            SessionActivityKind.ScrollEvent,
            ct).ConfigureAwait(false);

        var eventKey = Guid.NewGuid();

        // 4) Persist. The repo translates the
        //    UX_analyzerScrollSample_pageviewBucket violation into
        //    ScrollSampleDuplicateException.
        var dto = new AnalyzerScrollSampleDto
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            VisitorProfileKey = command.Actor.Key,
            SessionKey = resolution.SessionKey,
            PageviewKey = command.PageviewKey,
            ContentKey = command.ContentKey,
            Bucket = (byte)command.Bucket,
            ReceivedUtc = command.ReceivedUtc,
        };

        try
        {
            await _repository.InsertAsync(dto, ct).ConfigureAwait(false);
        }
        catch (ScrollSampleDuplicateException)
        {
            // 4a) Duplicate path: audit-tag as Duplicate, DO NOT
            //     append to state-store (no row landed for this
            //     request), re-throw for controller to map to 409.
            _auditor.AuditDuplicate(
                command.Actor,
                eventKey,
                command.PageviewKey,
                command.Bucket,
                command.ReceivedUtc);
            throw;
        }

        // 5) State-store append (only on the accepted path).
        var projection = new AnalyticsScrollSample(
            EventKey: eventKey,
            VisitorProfileKey: command.Actor.Key,
            SessionKey: resolution.SessionKey,
            PageviewKey: command.PageviewKey,
            ContentKey: command.ContentKey,
            Bucket: command.Bucket,
            ReceivedUtc: command.ReceivedUtc);
        _stateStore.AppendScrollEvent(projection);

        // 6) Audit.
        _auditor.AuditAccepted(
            command.Actor,
            eventKey,
            command.PageviewKey,
            command.Bucket,
            command.ReceivedUtc);

        return eventKey;
    }

    private static void ValidatePayload(AnalyzerScrollEventCapture command)
    {
        if (command.PageviewKey == Guid.Empty)
        {
            throw new AnalyzerScrollPayloadValidationException(
                nameof(command.PageviewKey),
                "pageviewKey must be a non-empty Guid.");
        }
        if (command.ContentKey == Guid.Empty)
        {
            throw new AnalyzerScrollPayloadValidationException(
                nameof(command.ContentKey),
                "contentKey must be a non-empty Guid.");
        }
        if (!Enum.IsDefined(typeof(AnalyzerScrollBucket), command.Bucket))
        {
            throw new AnalyzerScrollPayloadValidationException(
                nameof(command.Bucket),
                "bucket must be a defined AnalyzerScrollBucket value (25 / 50 / 75 / 100).");
        }
    }
}
