using Analyzer.Features.Forms.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application.Anonymization;

/// <summary>
/// Slice 005 US2 / FR-010 — fifth
/// <see cref="IAnonymizationCascadeStep"/> registration. Hard-deletes
/// the visitor's <c>analyzerFormFieldEvent</c> rows inside
/// Customizer's outer scope; ordering with the other cascade steps is
/// irrelevant (disjoint tables).
/// </summary>
internal sealed class AnalyzerFormFieldEventCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerFormFieldEventRepository _repository;
    private readonly ILogger<AnalyzerFormFieldEventCascadeStep> _logger;

    public AnalyzerFormFieldEventCascadeStep(
        IAnalyzerFormFieldEventRepository repository,
        ILogger<AnalyzerFormFieldEventCascadeStep> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerFormFieldEventCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        await _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AnalyzerFormFieldEvent cascade-delete completed for VisitorProfileKey={VisitorKey}",
            visitorProfileKey);
    }
}
