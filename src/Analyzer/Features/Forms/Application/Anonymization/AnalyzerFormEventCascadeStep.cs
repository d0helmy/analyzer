using Analyzer.Features.Forms.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application.Anonymization;

/// <summary>
/// Slice 005 / FR-010 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by hard-deleting
/// the visitor's <c>analyzerFormEvent</c> rows. Matches slice-002's
/// <c>AnalyzerEventReceiptCascadeStep</c> + slice-004's
/// <c>AnalyzerCustomEventCascadeStep</c> precedent — form lifecycle
/// rows are per-row engagement signals without aggregate-load-bearing
/// state, so hard-delete is the right per-table choice (Principle IV
/// v1.1.1 authorises).
/// </summary>
/// <remarks>
/// Throw rolls back the entire anonymisation transaction
/// unconditionally — the outer <c>scope.Complete()</c> only runs
/// after every cascade step succeeds.
/// </remarks>
internal sealed class AnalyzerFormEventCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerFormEventRepository _repository;
    private readonly ILogger<AnalyzerFormEventCascadeStep> _logger;

    public AnalyzerFormEventCascadeStep(
        IAnalyzerFormEventRepository repository,
        ILogger<AnalyzerFormEventCascadeStep> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerFormEventCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        await _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AnalyzerFormEvent cascade-delete completed for VisitorProfileKey={VisitorKey}",
            visitorProfileKey);
    }
}
