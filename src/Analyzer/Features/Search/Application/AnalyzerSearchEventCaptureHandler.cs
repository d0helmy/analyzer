using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Search.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Search.Application;

/// <summary>
/// Slice 007 — orchestrates one in-request internal-search submission
/// capture. Identity gate (FR-007) → normalisation
/// (<see cref="IAnalyzerSearchQueryNormaliser"/>) → visitor-bound
/// <c>pageviewKey</c> check (research §R3) → session resolution
/// (slice 003, dispatched as
/// <see cref="SessionActivityKind.SearchEvent"/>) → repo insert →
/// state-store append → audit emit (PII-redacted per FR-009 / SC-006).
/// </summary>
internal sealed class AnalyzerSearchEventCaptureHandler : IAnalyzerSearchEventCaptureHandler
{
    private readonly IAnalyzerSearchQueryNormaliser _normaliser;
    private readonly IAnalyzerSessionResolver _resolver;
    private readonly IAnalyzerSearchEventRepository _repository;
    private readonly AnalyticsEventStateStore _stateStore;
    private readonly IAnalyzerSearchEventAuditor _auditor;
    private readonly ILogger<AnalyzerSearchEventCaptureHandler> _logger;

    public AnalyzerSearchEventCaptureHandler(
        IAnalyzerSearchQueryNormaliser normaliser,
        IAnalyzerSessionResolver resolver,
        IAnalyzerSearchEventRepository repository,
        AnalyticsEventStateStore stateStore,
        IAnalyzerSearchEventAuditor auditor,
        ILogger<AnalyzerSearchEventCaptureHandler> logger)
    {
        _normaliser = normaliser;
        _resolver = resolver;
        _repository = repository;
        _stateStore = stateStore;
        _auditor = auditor;
        _logger = logger;
    }

    public async Task<AnalyticsSearchEvent> HandleAsync(
        AnalyzerSearchEventCapture command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // 1) Identity gate — defensive; the controller already rejects
        //    the unauthorised case before we get here.
        if (!command.Actor.IsAvailable || command.Actor.Key == Guid.Empty)
        {
            throw new UnauthorizedAccessException(
                "Search-event capture requires an authenticated EntraID actor.");
        }

        // 2) Payload validation — semantic invariants the model binder
        //    cannot express. The controller catches the empty-/oversize
        //    rawQuery case at model binding; this is defence in depth.
        if (string.IsNullOrWhiteSpace(command.RawQuery))
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.RawQuery),
                "rawQuery must be a non-empty string after trim.");
        }
        if (command.RawQuery.Length > 256)
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.RawQuery),
                "rawQuery must not exceed 256 characters.");
        }
        if (command.ResultCount < 0)
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.ResultCount),
                "resultCount must be a non-negative integer.");
        }
        if (command.PageviewKey == Guid.Empty)
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.PageviewKey),
                "pageviewKey must be a non-empty Guid.");
        }

        // 3) Normalisation — single in-process pass; defence-in-depth
        //    reject if the normaliser collapses input to empty (a
        //    custom IAnalyzerSearchQueryNormaliser could do so for a
        //    legitimately non-empty raw input).
        var normalised = _normaliser.Normalise(command.RawQuery);
        if (string.IsNullOrEmpty(normalised))
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.RawQuery),
                "IAnalyzerSearchQueryNormaliser produced an empty output for a non-empty input.");
        }
        if (normalised.Length > 256)
        {
            // Cap at the DB column length — a custom normaliser could
            // theoretically expand the string. CHECK constraint at the
            // DB layer would reject; we surface a friendlier 400.
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.RawQuery),
                "IAnalyzerSearchQueryNormaliser produced an output exceeding 256 characters.");
        }

        // 4) Visitor-bound pageviewKey check (research §R3 + FR-008).
        //    Reject 400 when the pageview does not exist, or belongs to
        //    a different visitor. The lookup also returns the row's
        //    contentKey so the server (not the client) sets the
        //    captured row's contentKey — defends against a client
        //    forging arbitrary content-key correlations.
        var pageviewBinding = await _repository
            .ResolvePageviewBindingAsync(command.PageviewKey, ct)
            .ConfigureAwait(false);
        if (pageviewBinding is null)
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.PageviewKey),
                "pageviewKey does not exist.");
        }
        if (pageviewBinding.Value.VisitorProfileKey != command.Actor.Key)
        {
            throw new AnalyzerSearchPayloadValidationException(
                nameof(command.PageviewKey),
                "pageviewKey does not belong to the resolved visitor.");
        }
        var contentKey = pageviewBinding.Value.ContentKey;

        // 5) Session resolution — search submissions are engagement;
        //    dispatch via SearchEvent (Touch — advances lastActivityUtc
        //    only).
        var resolution = await _resolver.ResolveAsync(
            command.Actor.Key,
            command.UserAgent,
            command.ReceivedUtc,
            SessionActivityKind.SearchEvent,
            ct).ConfigureAwait(false);

        var eventKey = Guid.NewGuid();

        // 6) Persist.
        var dto = new AnalyzerSearchEventDto
        {
            Id = Guid.NewGuid(),
            EventKey = eventKey,
            VisitorProfileKey = command.Actor.Key,
            SessionKey = resolution.SessionKey,
            PageviewKey = command.PageviewKey,
            ContentKey = contentKey,
            RawQuery = command.RawQuery,
            NormalisedQuery = normalised,
            ResultCount = command.ResultCount,
            ReceivedUtc = command.ReceivedUtc,
        };
        await _repository.InsertAsync(dto, ct).ConfigureAwait(false);

        // 7) State-store append.
        var projection = new AnalyticsSearchEvent
        {
            EventKey = eventKey,
            VisitorProfileKey = command.Actor.Key,
            SessionKey = resolution.SessionKey,
            PageviewKey = command.PageviewKey,
            ContentKey = contentKey,
            RawQuery = command.RawQuery,
            NormalisedQuery = normalised,
            ResultCount = command.ResultCount,
            ReceivedUtc = command.ReceivedUtc,
        };
        _stateStore.AppendSearchEvent(projection);

        // 8) Audit — PII-redacted by design; the auditor template
        //    excludes RawQuery + NormalisedQuery.
        _auditor.AuditAccepted(
            command.Actor,
            eventKey,
            command.PageviewKey,
            command.ResultCount,
            command.ReceivedUtc);

        return projection;
    }
}
