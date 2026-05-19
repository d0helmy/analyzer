using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Scroll.Application.Anonymization;

/// <summary>
/// Slice 006 / FR-009 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by hard-deleting
/// the visitor's <c>analyzerScrollSample</c> rows. Matches the
/// slice-002 / slice-004 / slice-005 hard-delete cascade-step
/// precedent — scroll-milestone rows are per-pageview engagement
/// signals without aggregate-load-bearing state, so hard-delete is
/// the right per-table choice (Principle IV v1.1.1 authorises).
/// </summary>
/// <remarks>
/// Throw rolls back the entire anonymisation transaction
/// unconditionally — the outer <c>scope.Complete()</c> only runs
/// after every cascade step succeeds. SC-004 budget: 1 000 rows in
/// ≤ 200 ms via <c>IDX_analyzerScrollSample_visitor</c>.
/// </remarks>
internal sealed class AnalyzerScrollSampleCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerScrollSampleRepository _repository;
    private readonly ILogger<AnalyzerScrollSampleCascadeStep> _logger;

    public AnalyzerScrollSampleCascadeStep(
        IAnalyzerScrollSampleRepository repository,
        ILogger<AnalyzerScrollSampleCascadeStep> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerScrollSampleCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        await _repository.DeleteByVisitorAsync(visitorProfileKey, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AnalyzerScrollSample cascade-delete completed for VisitorProfileKey={VisitorKey}",
            visitorProfileKey);
    }
}
