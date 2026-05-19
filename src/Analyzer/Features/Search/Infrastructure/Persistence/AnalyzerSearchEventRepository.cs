using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Search.Infrastructure.Persistence;

/// <summary>
/// Slice 007 — NPoco-backed
/// <see cref="IAnalyzerSearchEventRepository"/>. Mirrors slice-006's
/// scroll-sample repo: nested-scope semantics participate in
/// Customizer's anonymisation outer scope (cascade step), so a
/// downstream throw rolls back the insert atomically.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ResolvePageviewVisitorBindingAsync"/> reads
/// <c>customizerPageview</c> via raw SQL — no Customizer DTO import
/// (Principle III). Projects a single nullable
/// <c>visitorProfileKey</c>; one indexed read on the pageview's
/// primary key.
/// </para>
/// <para>
/// No idempotency guard on <see cref="InsertAsync"/> — search events
/// have no <c>(pageviewKey, normalisedQuery)</c> unique index per
/// research §R7. Re-running the same search is a distinct engagement
/// signal (spec Edge Cases).
/// </para>
/// </remarks>
internal sealed class AnalyzerSearchEventRepository : IAnalyzerSearchEventRepository
{
    private readonly IScopeProvider _scopeProvider;

    public AnalyzerSearchEventRepository(IScopeProvider scopeProvider) =>
        _scopeProvider = scopeProvider;

    public async Task InsertAsync(AnalyzerSearchEventDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();
        await scope.Database.InsertAsync(dto).ConfigureAwait(false);
        scope.Complete();
    }

    public Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Execute(
            $"DELETE FROM {Constants.Database.AnalyzerSearchEvent} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }

    public Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerSearchEvent} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.FromResult(count);
    }

    public Task<PageviewBinding?> ResolvePageviewBindingAsync(Guid pageviewKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        // Raw SQL read — Principle III: no import of the Customizer-owned
        // PageviewDto. customizerPageview uses a surrogate INT FK
        // (visitorProfileFk → customizerVisitorProfile.id) rather than a
        // Guid FK to .[key]; the join below resolves the surrogate to
        // the public Guid the handler compares against.
        var rows = scope.Database.Fetch<PageviewBindingRow>(
            "SELECT vp.[key] AS VisitorProfileKey, pv.[contentKey] AS ContentKey " +
            "FROM [customizerPageview] pv " +
            "INNER JOIN [customizerVisitorProfile] vp ON vp.[id] = pv.[visitorProfileFk] " +
            "WHERE pv.[key] = @0",
            pageviewKey);
        scope.Complete();
        if (rows.Count == 0)
        {
            return Task.FromResult<PageviewBinding?>(null);
        }
        var row = rows[0];
        return Task.FromResult<PageviewBinding?>(
            new PageviewBinding(row.VisitorProfileKey, row.ContentKey));
    }

    private sealed class PageviewBindingRow
    {
        public Guid VisitorProfileKey { get; set; }
        public Guid ContentKey { get; set; }
    }
}
