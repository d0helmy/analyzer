using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// Slice 005 — NPoco-backed
/// <see cref="IAnalyzerFormEventRepository"/>. Mirrors slice-004's
/// custom-event repo: nested-scope semantics participate in
/// Customizer's anonymisation outer scope (cascade step) and in the
/// sweeper's per-pass scope (abandonment materialiser), so a
/// downstream throw rolls back the inserts atomically.
/// </summary>
internal sealed class AnalyzerFormEventRepository : IAnalyzerFormEventRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerFormEventRepository> _logger;

    public AnalyzerFormEventRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerFormEventRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct)
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
            $"DELETE FROM {Constants.Database.AnalyzerFormEvent} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnclosedStartTuple>> ListUnclosedStartsForSessionsAsync(
        IReadOnlyCollection<Guid> sessionKeys,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionKeys);
        ct.ThrowIfCancellationRequested();

        if (sessionKeys.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<UnclosedStartTuple>>(Array.Empty<UnclosedStartTuple>());
        }

        using var scope = _scopeProvider.CreateScope();

        // Build the SELECT manually so parameters can be inlined safely
        // via NPoco's @0 placeholder. NPoco's Fetch<dynamic> with IN @N
        // requires the IEnumerable to be a top-level parameter; we pass
        // sessionKeys directly via a single @0 placeholder which NPoco
        // expands when the value is an IEnumerable<Guid>.
        const string sql =
            "SELECT s.sessionKey AS SessionKey, " +
            "       s.formKey AS FormKey, " +
            "       s.visitorProfileKey AS VisitorProfileKey, " +
            "       s.contentKey AS ContentKey, " +
            "       s.receivedUtc AS StartReceivedUtc " +
            "FROM analyzerFormEvent s " +
            "WHERE s.sessionKey IN (@0) " +
            "  AND s.eventType = 1 " +
            "  AND NOT EXISTS ( " +
            "      SELECT 1 FROM analyzerFormEvent x " +
            "      WHERE x.sessionKey = s.sessionKey " +
            "        AND x.formKey = s.formKey " +
            "        AND x.eventType = 2) " +
            "  AND NOT EXISTS ( " +
            "      SELECT 1 FROM analyzerFormEvent x " +
            "      WHERE x.sessionKey = s.sessionKey " +
            "        AND x.formKey = s.formKey " +
            "        AND x.eventType = 3)";

        var rows = scope.Database.Fetch<UnclosedStartRow>(sql, sessionKeys.ToArray());
        scope.Complete();

        var result = new List<UnclosedStartTuple>(rows.Count);
        foreach (var r in rows)
        {
            result.Add(new UnclosedStartTuple(
                SessionKey: r.SessionKey,
                FormKey: r.FormKey,
                VisitorProfileKey: r.VisitorProfileKey,
                ContentKey: r.ContentKey,
                StartReceivedUtc: r.StartReceivedUtc));
        }

        return Task.FromResult<IReadOnlyList<UnclosedStartTuple>>(result);
    }

    public async Task InsertAbandonsBulkAsync(
        IReadOnlyList<AnalyzerFormEventDto> abandons,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(abandons);
        ct.ThrowIfCancellationRequested();

        if (abandons.Count == 0)
        {
            return;
        }

        using var scope = _scopeProvider.CreateScope();
        foreach (var dto in abandons)
        {
            await scope.Database.InsertAsync(dto).ConfigureAwait(false);
        }
        scope.Complete();
    }

    /// <summary>
    /// Internal POCO used solely as NPoco's <c>Fetch&lt;T&gt;</c>
    /// projection target. Properties match the SELECT column aliases
    /// above so NPoco's by-name binder populates them without an
    /// explicit mapper.
    /// </summary>
    private sealed class UnclosedStartRow
    {
        public Guid SessionKey { get; set; }
        public Guid FormKey { get; set; }
        public Guid VisitorProfileKey { get; set; }
        public Guid ContentKey { get; set; }
        public DateTimeOffset StartReceivedUtc { get; set; }
    }
}
