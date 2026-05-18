using System.Data.Common;
using Analyzer.Analytics;
using Analyzer.Features.Common.Persistence;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Sessions.Application;

/// <summary>
/// Slice 003 — orchestrates the 7-step resolution flow documented in
/// <c>contracts/AnalyzerSessionResolver.md</c>: cache → lazy-close →
/// DB read → extend OR open → race-collision retry. ≤ 3 indexed SQL
/// statements per call under the worst-case path (cache miss + stale
/// row + open new); ≤ 1 UPDATE under the steady-state path (cache hit
/// + fresh).
/// </summary>
internal sealed class AnalyzerSessionResolver : IAnalyzerSessionResolver
{
    private readonly IAnalyzerSessionRepository _repository;
    private readonly AnalyzerSessionCacheStore _cache;
    private readonly IOptionsMonitor<AnalyzerSessionOptions> _options;
    private readonly ILogger<AnalyzerSessionResolver> _logger;

    public AnalyzerSessionResolver(
        IAnalyzerSessionRepository repository,
        AnalyzerSessionCacheStore cache,
        IOptionsMonitor<AnalyzerSessionOptions> options,
        ILogger<AnalyzerSessionResolver> logger)
    {
        _repository = repository;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<SessionResolutionResult> ResolveAsync(
        Guid visitorProfileKey,
        string? userAgent,
        DateTimeOffset receivedUtc,
        SessionActivityKind activityKind,
        CancellationToken ct)
    {
        var deviceKey = DeviceKeyHasher.Compute(userAgent);
        var inactivity = TimeSpan.FromMinutes(
            Math.Max(1, _options.CurrentValue.InactivityTimeoutMinutes));

        // 1) Cache lookup.
        if (_cache.TryGet(visitorProfileKey, deviceKey, out var cached))
        {
            if (cached.LastActivityUtc + inactivity >= receivedUtc)
            {
                // Fresh cache hit → advance activity, project client-side.
                return await AdvanceAndReturn(
                    visitorProfileKey, deviceKey, cached, receivedUtc, activityKind, ct)
                    .ConfigureAwait(false);
            }

            // Stale cache hit → close + fall through to open.
            await _repository.CloseAsync(
                cached.SessionKey,
                cached.LastActivityUtc + inactivity,
                ct).ConfigureAwait(false);
            _cache.Invalidate(visitorProfileKey, deviceKey);
        }

        // 2) Cache miss (or just-invalidated). Read most-recent active
        //    from DB to see if another instance owns the session.
        var dbRow = await _repository.GetLatestActiveAsync(
            visitorProfileKey, deviceKey, ct).ConfigureAwait(false);

        if (dbRow is not null && dbRow.LastActivityUtc + inactivity >= receivedUtc)
        {
            // DB hit + fresh → advance activity, cache, return.
            var entry = new AnalyticsSessionCacheEntry(
                SessionKey: dbRow.SessionKey,
                StartUtc: dbRow.StartUtc,
                LastActivityUtc: dbRow.LastActivityUtc,
                PageviewCount: dbRow.PageviewCount);
            return await AdvanceAndReturn(
                visitorProfileKey, deviceKey, entry, receivedUtc, activityKind, ct)
                .ConfigureAwait(false);
        }

        if (dbRow is not null)
        {
            // DB hit + stale → close + fall through to open new.
            await _repository.CloseAsync(
                dbRow.SessionKey,
                dbRow.LastActivityUtc + inactivity,
                ct).ConfigureAwait(false);
        }

        // 3) Open a new session. activityKind only matters for the
        //    collision-retry path (concurrent open winner needs the
        //    right Extend/Touch dispatch on its own row).
        return await OpenNewAsync(
            visitorProfileKey, deviceKey, receivedUtc, activityKind, ct).ConfigureAwait(false);
    }

    private async ValueTask<SessionResolutionResult> AdvanceAndReturn(
        Guid visitorProfileKey,
        string deviceKey,
        AnalyticsSessionCacheEntry entry,
        DateTimeOffset receivedUtc,
        SessionActivityKind activityKind,
        CancellationToken ct)
    {
        if (activityKind == SessionActivityKind.CustomEvent)
        {
            // Custom-event flow: advance lastActivityUtc only; do NOT
            // increment pageviewCount (Clarification §1). The repository's
            // TouchAsync UPDATE is idempotent on already-closed rows;
            // if the sweeper closed the row between cache-read and
            // touch, the row stays closed and we fall through to open
            // a fresh session.
            await _repository.TouchAsync(
                entry.SessionKey, receivedUtc, ct).ConfigureAwait(false);

            var touchedEntry = entry with { LastActivityUtc = receivedUtc };
            _cache.Set(visitorProfileKey, deviceKey, touchedEntry);

            var touchedProjection = new AnalyticsSession(
                SessionKey: entry.SessionKey,
                VisitorProfileKey: visitorProfileKey,
                StartUtc: entry.StartUtc,
                LastActivityUtc: receivedUtc,
                EndUtc: null,
                PageviewCount: entry.PageviewCount,
                IsActive: true);

            return new SessionResolutionResult(entry.SessionKey, touchedProjection);
        }

        // Pageview flow — advance lastActivityUtc AND increment pageviewCount.
        SessionExtendResult extended;
        try
        {
            extended = await _repository.ExtendAsync(
                entry.SessionKey, receivedUtc, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // Session got closed between cache read and our extend
            // (sweeper or cascade ran on this row). Re-read; if it's
            // gone, open a new one.
            _logger.LogDebug(
                "ExtendAsync matched zero rows for SessionKey={SessionKey:N}; re-resolving",
                entry.SessionKey);
            _cache.Invalidate(visitorProfileKey, deviceKey);
            return await OpenNewAsync(
                visitorProfileKey, deviceKey, receivedUtc, activityKind, ct).ConfigureAwait(false);
        }

        // Update cache + build projection from cached StartUtc +
        // post-update PageviewCount.
        var newEntry = entry with
        {
            LastActivityUtc = receivedUtc,
            PageviewCount = extended.PageviewCount,
        };
        _cache.Set(visitorProfileKey, deviceKey, newEntry);

        var projection = new AnalyticsSession(
            SessionKey: entry.SessionKey,
            VisitorProfileKey: visitorProfileKey,
            StartUtc: extended.StartUtc,
            LastActivityUtc: receivedUtc,
            EndUtc: null,
            PageviewCount: extended.PageviewCount,
            IsActive: true);

        return new SessionResolutionResult(entry.SessionKey, projection);
    }

    private async ValueTask<SessionResolutionResult> OpenNewAsync(
        Guid visitorProfileKey,
        string deviceKey,
        DateTimeOffset receivedUtc,
        SessionActivityKind activityKind,
        CancellationToken ct)
    {
        var newSessionKey = Guid.NewGuid();
        var dto = new AnalyzerSessionDto
        {
            Id = Guid.NewGuid(),
            SessionKey = newSessionKey,
            VisitorProfileKey = visitorProfileKey,
            DeviceKey = deviceKey,
            StartUtc = receivedUtc,
            LastActivityUtc = receivedUtc,
            EndUtc = null,
            PageviewCount = 1,
            IsActive = true,
            AnonymizedUtc = null,
        };

        try
        {
            await _repository.InsertAsync(dto, ct).ConfigureAwait(false);
        }
        catch (DbException ex) when (UniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex))
        {
            // Concurrent dispatcher opened a session for this
            // (visitor, device) — attach to theirs.
            _logger.LogDebug(
                "Concurrent session-open collision for VisitorProfileKey={VisitorKey:N}; attaching to winner",
                visitorProfileKey);

            var winner = await _repository.GetLatestActiveAsync(
                visitorProfileKey, deviceKey, ct).ConfigureAwait(false);
            if (winner is null)
            {
                // Shouldn't happen — partial unique index fired but
                // no row exists. Re-throw the original DbException to
                // surface the anomaly.
                throw;
            }

            var winnerEntry = new AnalyticsSessionCacheEntry(
                SessionKey: winner.SessionKey,
                StartUtc: winner.StartUtc,
                LastActivityUtc: winner.LastActivityUtc,
                PageviewCount: winner.PageviewCount);
            return await AdvanceAndReturn(
                visitorProfileKey, deviceKey, winnerEntry, receivedUtc, activityKind, ct)
                .ConfigureAwait(false);
        }

        // Insert succeeded. Cache + project.
        var entry = new AnalyticsSessionCacheEntry(
            SessionKey: newSessionKey,
            StartUtc: receivedUtc,
            LastActivityUtc: receivedUtc,
            PageviewCount: 1);
        _cache.Set(visitorProfileKey, deviceKey, entry);

        var projection = new AnalyticsSession(
            SessionKey: newSessionKey,
            VisitorProfileKey: visitorProfileKey,
            StartUtc: receivedUtc,
            LastActivityUtc: receivedUtc,
            EndUtc: null,
            PageviewCount: 1,
            IsActive: true);

        return new SessionResolutionResult(newSessionKey, projection);
    }
}
