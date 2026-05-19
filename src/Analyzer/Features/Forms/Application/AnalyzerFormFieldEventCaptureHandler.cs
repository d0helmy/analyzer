using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 US2 — orchestrates one in-request field-event capture.
/// Mirrors <see cref="AnalyzerFormEventCaptureHandler"/> with the
/// field-level <c>HadValue</c>/<c>EventType</c> invariant.
/// </summary>
internal sealed class AnalyzerFormFieldEventCaptureHandler : IAnalyzerFormFieldEventCaptureHandler
{
    private readonly IAnalyzerSessionResolver _resolver;
    private readonly IAnalyzerFormFieldEventRepository _repository;
    private readonly AnalyticsEventStateStore _stateStore;
    private readonly IAnalyzerFormFieldEventAuditor _auditor;
    private readonly ILogger<AnalyzerFormFieldEventCaptureHandler> _logger;

    public AnalyzerFormFieldEventCaptureHandler(
        IAnalyzerSessionResolver resolver,
        IAnalyzerFormFieldEventRepository repository,
        AnalyticsEventStateStore stateStore,
        IAnalyzerFormFieldEventAuditor auditor,
        ILogger<AnalyzerFormFieldEventCaptureHandler> logger)
    {
        _resolver = resolver;
        _repository = repository;
        _stateStore = stateStore;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(AnalyzerFormFieldEventCapture command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Actor.IsAvailable || command.Actor.Key == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Form-field-event capture requires an authenticated EntraID actor.");
        }

        ValidatePayload(command);

        // Both FieldFocus + FieldUnfocus count as engagement → Touch
        // (matches slice-004's CustomEvent semantic).
        var resolution = await _resolver.ResolveAsync(
            command.Actor.Key,
            command.UserAgent,
            command.ReceivedUtc,
            SessionActivityKind.CustomEvent,
            ct).ConfigureAwait(false);

        var eventKey = Guid.NewGuid();

        var dto = new AnalyzerFormFieldEventDto
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            VisitorProfileKey = command.Actor.Key,
            SessionKey = resolution.SessionKey,
            FormKey = command.FormKey,
            FieldKey = command.FieldKey,
            EventType = (byte)command.EventType,
            HadValue = command.HadValue,
            ReceivedUtc = command.ReceivedUtc,
        };
        await _repository.InsertAsync(dto, ct).ConfigureAwait(false);

        var projection = new AnalyticsFormFieldEvent(
            EventKey: eventKey,
            VisitorProfileKey: command.Actor.Key,
            SessionKey: resolution.SessionKey,
            FormKey: command.FormKey,
            FieldKey: command.FieldKey,
            EventType: command.EventType,
            HadValue: command.HadValue,
            ReceivedUtc: command.ReceivedUtc);
        _stateStore.AppendFormFieldEvent(projection);

        _auditor.Audit(
            command.Actor,
            eventKey,
            command.FormKey,
            command.FieldKey,
            command.EventType,
            command.HadValue,
            command.ReceivedUtc);

        return eventKey;
    }

    private static void ValidatePayload(AnalyzerFormFieldEventCapture command)
    {
        if (command.FormKey == Guid.Empty)
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.FormKey),
                "formKey must be a non-empty Guid.");
        }
        if (command.FieldKey == Guid.Empty)
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.FieldKey),
                "fieldKey must be a non-empty Guid.");
        }
        if (!Enum.IsDefined(typeof(AnalyzerFormFieldEventType), command.EventType))
        {
            throw new AnalyzerFormPayloadValidationException(
                nameof(command.EventType),
                "eventType must be a defined AnalyzerFormFieldEventType value.");
        }

        // HadValue invariant: set only on FieldUnfocus.
        switch (command.EventType)
        {
            case AnalyzerFormFieldEventType.FieldFocus:
                if (command.HadValue is not null)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.HadValue),
                        "FieldFocus rows must not carry hadValue.");
                }
                break;

            case AnalyzerFormFieldEventType.FieldUnfocus:
                if (command.HadValue is null)
                {
                    throw new AnalyzerFormPayloadValidationException(
                        nameof(command.HadValue),
                        "FieldUnfocus rows require hadValue (true | false).");
                }
                break;
        }
    }
}
