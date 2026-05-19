using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// Slice 005 US2 — NPoco-backed
/// <see cref="IAnalyzerFormFieldEventRepository"/>. Mirrors the
/// lifecycle repo's nested-scope semantics so the cascade DELETE
/// participates in Customizer's outer transaction.
/// </summary>
internal sealed class AnalyzerFormFieldEventRepository : IAnalyzerFormFieldEventRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerFormFieldEventRepository> _logger;

    public AnalyzerFormFieldEventRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerFormFieldEventRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task InsertAsync(AnalyzerFormFieldEventDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();
        await scope.Database.InsertAsync(dto).ConfigureAwait(false);
        scope.Complete();
    }

    public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Execute(
            $"DELETE FROM {Constants.Database.AnalyzerFormFieldEvent} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }
}
