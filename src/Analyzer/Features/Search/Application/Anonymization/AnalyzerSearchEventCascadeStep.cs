using Analyzer.Features.Search.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Search.Application.Anonymization;

/// <summary>
/// Slice 007 / FR-010 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by hard-deleting
/// the visitor's <c>analyzerSearchEvent</c> rows.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cascade-disposition divergence from contract D8</b>: contract
/// §3 D8 listed this table with disposition "re-key"; this slice
/// ships <b>hard-delete</b>. Justified by FR-SRC-04 (queries are PII;
/// re-keying retains the literal <c>rawQuery</c> + <c>normalisedQuery</c>
/// strings attached to a pseudonymous identifier — still a record of
/// "this person searched for $X" from any informed adversary's
/// standpoint). Principle IV v1.1.1's participation-pattern menu
/// (delete / soft-delete / re-projection) explicitly authorises
/// per-table choice. See spec Clarifications §2 + research §R8 +
/// the contract follow-up note in the slice 007 PR description.
/// </para>
/// <para>
/// Throw rolls back the entire anonymisation transaction
/// unconditionally — the outer <c>scope.Complete()</c> only runs after
/// every cascade step succeeds. SC-004 budget: 1 000 rows in ≤ 200 ms
/// via <c>IDX_analyzerSearchEvent_visitor</c>.
/// </para>
/// </remarks>
internal sealed class AnalyzerSearchEventCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerSearchEventRepository _repository;
    private readonly ILogger<AnalyzerSearchEventCascadeStep> _logger;

    public AnalyzerSearchEventCascadeStep(
        IAnalyzerSearchEventRepository repository,
        ILogger<AnalyzerSearchEventCascadeStep> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerSearchEventCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        await _repository.DeleteByVisitorAsync(visitorProfileKey, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "AnalyzerSearchEvent cascade-delete completed for VisitorProfileKey={VisitorKey} (PII per FR-SRC-04)",
            visitorProfileKey);
    }
}
