using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.CustomEvents.Infrastructure.Persistence;

/// <summary>
/// Slice 004 — NPoco-backed <see cref="IAnalyzerCustomEventRepository"/>.
/// Opens a nested <c>IScopeProvider.CreateScope()</c> per call — when
/// the caller has already opened an outer scope (cascade step inside
/// Customizer's <c>AnonymizeVisitorProfileHandler</c>), the nested
/// scope enlists in the outer transaction and rolls back atomically
/// on a throw (matches slice-002 receipt + slice-003 session repo
/// pattern).
/// </summary>
internal sealed class AnalyzerCustomEventRepository : IAnalyzerCustomEventRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerCustomEventRepository> _logger;

    public AnalyzerCustomEventRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerCustomEventRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task InsertAsync(AnalyzerCustomEventDto dto, CancellationToken ct)
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
            $"DELETE FROM {Constants.Database.AnalyzerCustomEvent} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }
}
