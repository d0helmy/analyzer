using Analyzer.Analytics;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Sessions.Infrastructure.Persistence;

/// <summary>
/// NPoco-backed <see cref="IAnalyzerSessionRepository"/>. Opens a
/// nested <c>IScopeProvider.CreateScope()</c> per call — when an outer
/// scope is already open (cascade step inside Customizer's
/// <c>AnonymizeVisitorProfileHandler</c>), the nested scope enlists
/// and rolls back atomically on a throw (matches slice-002 pattern).
/// </summary>
internal sealed class AnalyzerSessionRepository : IAnalyzerSessionRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerSessionRepository> _logger;

    public AnalyzerSessionRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerSessionRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task<AnalyticsSession?> GetLatestActiveAsync(
        Guid visitorProfileKey,
        string deviceKey,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();
        var dto = await scope.Database
            .FirstOrDefaultAsync<AnalyzerSessionDto>(
                $"WHERE visitorProfileKey = @0 AND deviceKey = @1 AND isActive = 1 " +
                $"ORDER BY lastActivityUtc DESC",
                visitorProfileKey,
                deviceKey)
            .ConfigureAwait(false);
        scope.Complete();

        return dto is null ? null : ToProjection(dto);
    }

    public async Task InsertAsync(AnalyzerSessionDto session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();
        await scope.Database.InsertAsync(session).ConfigureAwait(false);
        scope.Complete();
    }

    public async Task<SessionExtendResult> ExtendAsync(
        Guid sessionKey,
        DateTimeOffset newLastActivityUtc,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();

        // Single round-trip: UPDATE then SELECT post-update columns
        // in the same scope. SQL Server supports OUTPUT INSERTED.* but
        // NPoco doesn't expose a typed wrapper across providers, so we
        // do UPDATE + SELECT inside the same NPoco scope (the row-level
        // lock is held by the open scope, so the SELECT reads our own
        // write — no race window).
        var affected = await scope.Database.ExecuteAsync(
            $"UPDATE {Constants.Database.AnalyzerSession} " +
            $"SET lastActivityUtc = @0, pageviewCount = pageviewCount + 1 " +
            $"WHERE sessionKey = @1 AND isActive = 1",
            newLastActivityUtc,
            sessionKey).ConfigureAwait(false);

        if (affected == 0)
        {
            // Either the row was concurrently closed or never existed.
            // Bubble — the resolver re-reads via GetLatestActiveAsync.
            scope.Complete();
            throw new InvalidOperationException(
                $"ExtendAsync matched zero rows for SessionKey={sessionKey:N}; " +
                $"session may have been closed concurrently.");
        }

        var post = await scope.Database
            .SingleAsync<SessionExtendDto>(
                $"SELECT startUtc AS StartUtc, pageviewCount AS PageviewCount " +
                $"FROM {Constants.Database.AnalyzerSession} " +
                $"WHERE sessionKey = @0",
                sessionKey)
            .ConfigureAwait(false);

        scope.Complete();
        return new SessionExtendResult(post.StartUtc, post.PageviewCount);
    }

    public async Task CloseAsync(
        Guid sessionKey,
        DateTimeOffset logicalCloseUtc,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var scope = _scopeProvider.CreateScope();
        await scope.Database.ExecuteAsync(
            $"UPDATE {Constants.Database.AnalyzerSession} " +
            $"SET isActive = 0, endUtc = @0 " +
            $"WHERE sessionKey = @1 AND isActive = 1",
            logicalCloseUtc,
            sessionKey).ConfigureAwait(false);
        scope.Complete();
    }

    private static AnalyticsSession ToProjection(AnalyzerSessionDto dto) =>
        new(
            SessionKey: dto.SessionKey,
            VisitorProfileKey: dto.VisitorProfileKey,
            StartUtc: dto.StartUtc,
            LastActivityUtc: dto.LastActivityUtc,
            EndUtc: dto.EndUtc,
            PageviewCount: dto.PageviewCount,
            IsActive: dto.IsActive);

    /// <summary>
    /// NPoco-mapped projection holder for the post-extend SELECT.
    /// </summary>
    private sealed class SessionExtendDto
    {
        public DateTimeOffset StartUtc { get; set; }
        public int PageviewCount { get; set; }
    }
}
