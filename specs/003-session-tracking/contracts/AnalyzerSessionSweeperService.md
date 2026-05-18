# Contract — `AnalyzerSessionSweeperService`

**Feature**: `003-session-tracking`
**Date**: 2026-05-18
**Stability**: internal. Background hosted service. Not part of any consumer-facing contract; documented here because its closure semantic (`endUtc = lastActivityUtc + inactivityTimeout`) is load-bearing for session-duration analytics.

## Namespace

```
Analyzer.Features.Sessions.Infrastructure.Sweeper.AnalyzerSessionSweeperService
```

## Shape

```csharp
namespace Analyzer.Features.Sessions.Infrastructure.Sweeper;

internal sealed class AnalyzerSessionSweeperService : BackgroundService
{
    public AnalyzerSessionSweeperService(
        IServiceScopeFactory scopeFactory,
        AnalyzerSessionCacheStore cacheStore,
        IOptionsMonitor<AnalyzerSessionOptions> options,
        TimeProvider timeProvider,
        ILogger<AnalyzerSessionSweeperService> logger);

    protected override Task ExecuteAsync(CancellationToken stoppingToken);
}
```

Mirrors slice-002's `AnalyzerEventReceiptWriteDispatcher` shape — `BackgroundService` base, `IServiceScopeFactory` to open per-tick scopes for repository access (the repository is scoped, so each tick gets a fresh `IAnalyzerSessionRepository` instance with its own NPoco scope), `IOptionsMonitor` for runtime-reloadable config, `TimeProvider` for testability.

## DI registration

| Aspect | Value |
|---|---|
| **Lifetime** | **Singleton** (hosted service). One instance per host runtime. `AddHostedService<AnalyzerSessionSweeperService>()` |
| **Implementation** | `Analyzer.Features.Sessions.Infrastructure.Sweeper.AnalyzerSessionSweeperService` (internal sealed) |
| **Composition site** | `AnalyzerComposer.Compose` — `services.AddHostedService<AnalyzerSessionSweeperService>();` |

## Behavior

### Lifecycle

- **Start**: registered by `AddHostedService`; the host calls `StartAsync` after `IUmbracoBuilder` composition completes. Logs `"Analyzer session sweeper started"` at Information level.
- **Run**: `ExecuteAsync` loops until `stoppingToken` fires.
- **Stop**: host shutdown triggers `stoppingToken`; the loop exits at the next loop-top check or `Task.Delay` cancellation; logs `"Analyzer session sweeper stopped"` at Information level.

### Loop body (normative)

```
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        var options = _options.CurrentValue
        var inactivity = TimeSpan.FromMinutes(Math.Max(1, options.InactivityTimeoutMinutes))
        var now = _timeProvider.GetUtcNow()
        var cutoff = now - inactivity
        var batchSize = Math.Max(1, options.SweepBatchSize)

        using var scope = _scopeFactory.CreateScope()
        var repository = scope.ServiceProvider.GetRequiredService<IAnalyzerSessionRepository>()

        var closedKeys = await repository.SweepEligibleAsync(
            cutoff, inactivity, batchSize, stoppingToken)

        foreach (var key in closedKeys)
            _cacheStore.InvalidateBySessionKey(key)

        if (closedKeys.Count > 0)
            _logger.LogDebug(
                "Analyzer session sweeper closed {Count} sessions",
                closedKeys.Count)
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
    catch (Exception ex) { _logger.LogError(ex, "Analyzer session sweeper tick failed"); }

    try
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.SweepIntervalSeconds))
        await Task.Delay(interval, stoppingToken)
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
}
```

### Close semantic (load-bearing)

`SweepEligibleAsync` issues:

```sql
DECLARE @inactivitySeconds INT = <seconds>
UPDATE TOP (@batchSize) analyzerSession
SET isActive = 0,
    endUtc = DATEADD(SECOND, @inactivitySeconds, lastActivityUtc)
OUTPUT INSERTED.sessionKey
WHERE isActive = 1 AND lastActivityUtc < @cutoff
```

(SQLite equivalent uses a CTE: `UPDATE … WHERE rowid IN (SELECT rowid … LIMIT @n)`; the `OUTPUT` clause is replaced by a follow-up SELECT against `endUtc` to retrieve the closed `sessionKey`s, OR — simpler — the SELECT runs BEFORE the UPDATE to gather the keys, then the UPDATE closes them. Either is correct for SQLite-on-dev; CI doesn't exercise this path against SQLite.)

`endUtc = lastActivityUtc + inactivityTimeout` is the **logical** close time. NOT `now` (the sweeper's wall-clock). This is the spec's Assumption #5 load-bearing decision:

- Session duration metrics depend on `endUtc - startUtc`.
- Using `now` would inflate every long-tail session's duration by up to one full sweeper interval (e.g., for a 60-second sweep interval, every session sweepable on the FIRST tick after its inactivity expired observes a duration of `inactivity + small_observation_lag`; for a sweep that runs once per minute on hourly-inactive sessions, the inflation is bounded but non-zero).
- The logical close time is the analytically-correct time the session ended; the sweeper merely observes that the end already happened.

### Determinism / idempotence

- Idempotent across ticks: a session that's already `isActive = 0` doesn't match the predicate `WHERE isActive = 1 AND lastActivityUtc < @cutoff` — the UPDATE affects zero rows for already-closed sessions.
- Idempotent across instances: two Umbraco hosts running the sweeper produce the same UPDATE on the same rows; both compute the same `endUtc = lastActivityUtc + inactivity` (deterministic from the row's `lastActivityUtc` field); SQL Server serialises the row-level writes; both report the same `closedKeys`.
- Cache invalidation is idempotent (`InvalidateBySessionKey` on a missing key is a no-op).

### Thread safety

- One sweeper instance per host. Loop body is sequential — no internal concurrency.
- Repository call opens a per-tick NPoco scope; no cross-tick state held.
- Cache invalidation per closed key; each call is thread-safe against concurrent `MemoryCache` reads from the resolver.

### Error handling

| Error | Behaviour |
|---|---|
| `OperationCanceledException` with shutdown token | Break out of the loop; log `"Analyzer session sweeper stopped"`. |
| `OperationCanceledException` without shutdown token | Should not happen; if it does (e.g., a downstream cancellation token slipped in via `IOptionsMonitor`), treat as a poisoned tick → log error → continue loop. |
| Any other `Exception` in the tick body | Log at Error level; `Task.Delay(interval)` follows; loop continues. The sweeper is the long-tail correctness mechanism — a missed tick is recoverable on the next one. |
| `ChannelClosedException` / `ObjectDisposedException` on the cache | Should not happen during normal operation — the cache lives for the lifetime of the host; if the cache is disposed mid-tick (host shutdown race), the loop's outer `OperationCanceledException` catch handles it. |

### Configuration responsiveness

`SweepIntervalSeconds` and `SweepBatchSize` are read via `IOptionsMonitor<AnalyzerSessionOptions>` at the top of each tick. Operators can:

- Halve `SweepIntervalSeconds` from 60 → 30 → next tick runs at 30s cadence (current `Task.Delay` is not interrupted; the next interval picks up the new value).
- Increase `SweepBatchSize` from 1000 → 10000 → next tick processes up to 10000 rows.

`InactivityTimeoutMinutes` is also read at the top of each tick. **Important**: changing it does NOT migrate existing session boundaries — sessions already closed by the previous timeout keep their previous `endUtc`. The new timeout applies only from the next eligible-tick computation. (Spec edge case explicit.)

`CacheCapacity` is NOT read in the sweeper — the cache's capacity is fixed at first construction.

## Multi-instance behaviour

When deployed behind a load balancer with N Umbraco instances, each instance runs its own sweeper. Per-tick behaviour:

- Both instances scan the same rows.
- Each UPDATE batches `SweepBatchSize` rows; concurrent batches on disjoint rows complete independently.
- Concurrent batches on the same row → SQL Server serialises; both updates produce the same `endUtc` (deterministic from the row); both report success.
- No data corruption, no double-close, no missing close.

The `OUTPUT INSERTED.sessionKey` returns the keys both instances tried to close — both invalidate the same cache keys (idempotent).

There is **no distributed lock** required. The spec edge case is explicit about this.

## Tests proving conformance

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `Unit/Features/Sessions/Sweeper/AnalyzerSessionSweeperServiceTests.ClosesEligibleSessions` | Sweep with mocked repository returns N keys; cache invalidated N times. | US3 AS1 |
| `…LeavesActiveSessionsAlone` | Mocked repository returns 0 keys (no rows match the predicate); no cache invalidation. | US3 AS2 |
| `…IdempotentOnAlreadyClosed` | Repository's predicate excludes already-closed rows by design (test directly against an in-memory fake of the repository). | US3 AS3 |
| `…SwallowsTickExceptionAndContinues` | Inject a repository that throws on the first tick, succeeds on the second. Assert the loop continues; the second tick's success is logged. | US3 AS4 (matches dispatcher precedent) |
| `…OptionsReloadAdjustsInterval` | `IOptionsMonitor` reload changes `SweepIntervalSeconds`; the next `Task.Delay` uses the new value. | FR-008 |
| `Integration/Sessions/SweeperBackgroundServiceTests.LogicalCloseTimeNotWallClock` | Real container; small inactivity timeout; assert `endUtc = lastActivityUtc + inactivity` (not the sweeper's run time). | US3 independent test + SC-005 |
| `…ClosesAllEligibleWithinTwoIntervals` | Real container; multiple eligible rows; assert 100% closed within `2 × SweepIntervalSeconds`. | SC-005 |

## Versioning

`AnalyzerSessionSweeperService` is **internal**. Its public surface is the side effects it has on `analyzerSession` rows; those side effects are part of the FR-007 contract documented in the slice's spec. Replacing the sweeper with an alternative would be done by removing the hosted-service registration (`services.RemoveAll<IHostedService>(...)` against the impl type) and registering a replacement — not a common operation; documented but not a stable contract.
