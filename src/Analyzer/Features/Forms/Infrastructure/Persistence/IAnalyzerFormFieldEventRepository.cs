namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// Slice 005 US2 — internal repository for the
/// <c>analyzerFormFieldEvent</c> table. Same nested-scope semantics
/// as <see cref="IAnalyzerFormEventRepository"/>: cascade-step
/// participation in Customizer's anonymisation outer scope is the
/// load-bearing case.
/// </summary>
internal interface IAnalyzerFormFieldEventRepository
{
    Task InsertAsync(AnalyzerFormFieldEventDto dto, CancellationToken ct);

    Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct);
}
