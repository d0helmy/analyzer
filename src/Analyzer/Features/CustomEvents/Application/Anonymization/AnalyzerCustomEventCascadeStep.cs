using Analyzer.Features.CustomEvents.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.CustomEvents.Application.Anonymization;

/// <summary>
/// Slice 004 US2 / FR-009 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by hard-deleting
/// the visitor's <c>analyzerCustomEvent</c> rows. Matches slice-002's
/// <c>AnalyzerEventReceiptCascadeStep</c> precedent — custom events
/// are per-row engagement signals without aggregate-load-bearing
/// state, so hard-delete is the right per-table choice (Principle IV
/// v1.1.1 authorises).
/// </summary>
/// <remarks>
/// Throw rolls back the entire anonymisation transaction
/// unconditionally — the outer <c>scope.Complete()</c> only runs after
/// every cascade step succeeds (research §3; contract doc
/// <c>AnalyzerCustomEventCascadeStep.md</c>).
/// </remarks>
internal sealed class AnalyzerCustomEventCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerCustomEventRepository _repository;
    private readonly ILogger<AnalyzerCustomEventCascadeStep> _logger;

    public AnalyzerCustomEventCascadeStep(
        IAnalyzerCustomEventRepository repository,
        ILogger<AnalyzerCustomEventCascadeStep> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerCustomEventCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        await _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AnalyzerCustomEvent cascade-delete completed for VisitorProfileKey={VisitorKey}",
            visitorProfileKey);
    }
}
