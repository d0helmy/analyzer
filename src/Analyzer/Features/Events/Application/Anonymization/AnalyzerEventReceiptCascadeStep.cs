using Analyzer.Features.Events.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;

namespace Analyzer.Features.Events.Application.Anonymization;

/// <summary>
/// Slice 002 / FR-006 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by hard-deleting
/// the visitor's <c>analyzerEventReceipt</c> rows. Matches Customizer's
/// <c>GoalReachedCascadeStep</c> precedent (delete, not re-key).
/// </summary>
/// <remarks>
/// Throw rolls back the entire anonymisation transaction
/// unconditionally — the outer <c>scope.Complete()</c> only runs after
/// every cascade step succeeds (research §3; contract doc
/// <c>AnalyzerEventReceiptCascadeStep.md</c>).
/// </remarks>
internal sealed class AnalyzerEventReceiptCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerEventReceiptRepository _repository;

    public AnalyzerEventReceiptCascadeStep(IAnalyzerEventReceiptRepository repository) =>
        _repository = repository;

    public Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct) =>
        _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct);
}
