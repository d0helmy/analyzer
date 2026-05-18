using Analyzer.Analytics;
using Analyzer.Features.CustomEvents.Infrastructure.Persistence;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Sessions.Application;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 — orchestrates one in-request custom-event capture
/// (research §3). The synchronous, request-thread write path:
/// <list type="number">
///   <item>resolve the active session via
///   <see cref="IAnalyzerSessionResolver"/> with
///   <see cref="SessionActivityKind.CustomEvent"/> (advances
///   <c>lastActivityUtc</c>; does NOT bump <c>pageviewCount</c>);</item>
///   <item>insert one <c>analyzerCustomEvent</c> row via
///   <see cref="IAnalyzerCustomEventRepository"/>;</item>
///   <item>opportunistically populate the request-scoped
///   <see cref="AnalyticsEventStateStore"/> with the projection;</item>
///   <item>emit a structured audit-log entry.</item>
/// </list>
/// Returns the new row's <c>eventKey</c> so the controller can return
/// it on the HTTP 202 body.
/// </summary>
internal sealed class CustomEventCaptureHandler : ICustomEventCaptureHandler
{
    private readonly IAnalyzerSessionResolver _resolver;
    private readonly IAnalyzerCustomEventRepository _repository;
    private readonly AnalyticsEventStateStore _stateStore;
    private readonly ICustomEventAuditor _auditor;
    private readonly ILogger<CustomEventCaptureHandler> _logger;

    public CustomEventCaptureHandler(
        IAnalyzerSessionResolver resolver,
        IAnalyzerCustomEventRepository repository,
        AnalyticsEventStateStore stateStore,
        ICustomEventAuditor auditor,
        ILogger<CustomEventCaptureHandler> logger)
    {
        _resolver = resolver;
        _repository = repository;
        _stateStore = stateStore;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<Guid> HandleAsync(CustomEventCapture command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolution = await _resolver.ResolveAsync(
            command.Actor.Key,
            command.UserAgent,
            command.ReceivedUtc,
            SessionActivityKind.CustomEvent,
            ct).ConfigureAwait(false);

        var eventKey = Guid.NewGuid();
        var receiptKey = _stateStore.CurrentRequestReceipt?.Id;

        var dto = new AnalyzerCustomEventDto
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            SessionKey = resolution.SessionKey,
            VisitorProfileKey = command.Actor.Key,
            ReceiptKey = receiptKey,
            Category = command.Category,
            Action = command.Action,
            Label = command.Label,
            Value = command.Value,
            ReceivedUtc = command.ReceivedUtc,
        };

        await _repository.InsertAsync(dto, ct).ConfigureAwait(false);

        var projection = new AnalyticsCustomEvent(
            EventKey: eventKey,
            SessionKey: resolution.SessionKey,
            VisitorProfileKey: command.Actor.Key,
            ReceiptKey: receiptKey,
            Category: command.Category,
            Action: command.Action,
            Label: command.Label,
            Value: command.Value,
            ReceivedUtc: command.ReceivedUtc);
        _stateStore.AppendCustomEvent(projection);

        _auditor.Audit(
            command.Actor,
            eventKey,
            command.Category,
            command.Action,
            command.ReceivedUtc);

        return eventKey;
    }
}
