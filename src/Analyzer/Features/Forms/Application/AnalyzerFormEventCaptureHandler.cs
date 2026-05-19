using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 — orchestrates one in-request per-form lifecycle capture.
/// Identity gate (FR-014) → payload validation (FR-006 / FR-007) →
/// session resolution (slice 003) → repo insert (slice 005 §6) →
/// state-store append (slice 002 onward) → audit emit (FR-009).
/// Returns the persisted <c>eventKey</c> for the HTTP 202 body.
/// </summary>
/// <remarks>
/// Session-activity dispatch (per contract §3):
/// <list type="bullet">
///   <item><see cref="AnalyzerFormEventType.Impression"/> →
///     <see cref="SessionActivityKind.FormImpression"/> (passive read,
///     no Touch).</item>
///   <item><see cref="AnalyzerFormEventType.Start"/> +
///     <see cref="AnalyzerFormEventType.Success"/> →
///     <see cref="SessionActivityKind.CustomEvent"/> (Touch — engagement
///     keeps the session alive).</item>
///   <item><see cref="AnalyzerFormEventType.Abandon"/> NEVER enters
///     this handler (materialised by the sweeper).</item>
/// </list>
/// </remarks>
internal sealed class AnalyzerFormEventCaptureHandler : IAnalyzerFormEventCaptureHandler
{
    private readonly IAnalyzerSessionResolver _resolver;
    private readonly IAnalyzerFormEventRepository _repository;
    private readonly AnalyticsEventStateStore _stateStore;
    private readonly IAnalyzerFormEventAuditor _auditor;
    private readonly ILogger<AnalyzerFormEventCaptureHandler> _logger;

    public AnalyzerFormEventCaptureHandler(
        IAnalyzerSessionResolver resolver,
        IAnalyzerFormEventRepository repository,
        AnalyticsEventStateStore stateStore,
        IAnalyzerFormEventAuditor auditor,
        ILogger<AnalyzerFormEventCaptureHandler> logger)
    {
        _resolver = resolver;
        _repository = repository;
        _stateStore = stateStore;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(AnalyzerFormEventCapture command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1) Identity gate — defensive; the controller already rejects
        //    the unauthorised case before we get here.
        if (!command.Actor.IsAvailable || command.Actor.Key == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Form-event capture requires an authenticated EntraID actor.");
        }

        // 2) Payload validation — semantic invariants the model binder
        //    cannot express.
        ValidatePayload(command);

        // 3) Session resolution — dispatch on event-type.
        var activityKind = command.EventType == AnalyzerFormEventType.Impression
            ? SessionActivityKind.FormImpression
            : SessionActivityKind.CustomEvent;

        var resolution = await _resolver.ResolveAsync(
            command.Actor.Key,
            command.UserAgent,
            command.ReceivedUtc,
            activityKind,
            ct).ConfigureAwait(false);

        var eventKey = Guid.NewGuid();

        // 4) Persist.
        var dto = new AnalyzerFormEventDto
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            VisitorProfileKey = command.Actor.Key,
            SessionKey = resolution.SessionKey,
            FormKey = command.FormKey,
            ContentKey = command.ContentKey,
            EventType = (byte)command.EventType,
            ElapsedMsFromImpression = command.ElapsedMsFromImpression,
            ElapsedMsFromStart = command.ElapsedMsFromStart,
            ReceivedUtc = command.ReceivedUtc,
        };
        await _repository.InsertAsync(dto, ct).ConfigureAwait(false);

        // 5) State-store append (additive; never null).
        var projection = new AnalyticsFormEvent(
            EventKey: eventKey,
            VisitorProfileKey: command.Actor.Key,
            SessionKey: resolution.SessionKey,
            FormKey: command.FormKey,
            ContentKey: command.ContentKey,
            EventType: command.EventType,
            ElapsedMsFromImpression: command.ElapsedMsFromImpression,
            ElapsedMsFromStart: command.ElapsedMsFromStart,
            ReceivedUtc: command.ReceivedUtc);
        _stateStore.AppendFormEvent(projection);

        // 6) Audit.
        _auditor.Audit(
            command.Actor,
            eventKey,
            command.FormKey,
            command.EventType,
            command.ReceivedUtc);

        return eventKey;
    }

    private static void ValidatePayload(AnalyzerFormEventCapture command)
    {
        if (command.FormKey == Guid.Empty)
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.FormKey),
                "formKey must be a non-empty Guid.");
        }
        if (command.ContentKey == Guid.Empty)
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.ContentKey),
                "contentKey must be a non-empty Guid.");
        }
        if (!Enum.IsDefined(typeof(AnalyzerFormEventType), command.EventType))
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.EventType),
                "eventType must be a defined AnalyzerFormEventType value.");
        }
        if (command.EventType == AnalyzerFormEventType.Abandon)
        {
            // Abandons are sweeper-materialised; the management endpoint
            // MUST refuse client-sent Abandon rows.
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.EventType),
                "Abandon rows are materialised server-side; clients must not POST them.");
        }

        switch (command.EventType)
        {
            case AnalyzerFormEventType.Impression:
                if (command.ElapsedMsFromImpression is not null ||
                    command.ElapsedMsFromStart is not null)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.EventType),
                        "Impression rows must not carry elapsed-ms timing slots.");
                }
                break;

            case AnalyzerFormEventType.Start:
                if (command.ElapsedMsFromImpression is null ||
                    command.ElapsedMsFromImpression < 0)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.ElapsedMsFromImpression),
                        "Start rows require elapsedMsFromImpression ≥ 0.");
                }
                if (command.ElapsedMsFromStart is not null)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.ElapsedMsFromStart),
                        "Start rows must not carry elapsedMsFromStart.");
                }
                break;

            case AnalyzerFormEventType.Success:
                if (command.ElapsedMsFromStart is null ||
                    command.ElapsedMsFromStart < 0)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.ElapsedMsFromStart),
                        "Success rows require elapsedMsFromStart ≥ 0.");
                }
                if (command.ElapsedMsFromImpression is not null)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.ElapsedMsFromImpression),
                        "Success rows must not carry elapsedMsFromImpression.");
                }
                break;
        }
    }
}
