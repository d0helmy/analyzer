# Phase 0 Research: Sessions

**Slice**: 003 — session tracking
**Date**: 2026-05-18
**Constitution**: v1.1.1
**Input**: [`spec.md`](spec.md) + [`plan.md`](plan.md) Technical Context

The spec carries no `[NEEDS CLARIFICATION]` markers — its two load-bearing decisions (`deviceKey` derivation; cascade-step semantic) are documented as Assumptions per the no-stopping directive. This document captures the design decisions grounded against the existing Customizer + Analyzer codebases, the alternatives considered, and the rationale, so `/speckit-tasks` can drive directly into implementation.

---

## §1 — Session-resolution mechanism

**Decision**: introduce an internal contract `Analyzer.Features.Sessions.Application.IAnalyzerSessionResolver` with a single method `ValueTask<SessionResolutionResult> ResolveAsync(Guid visitorProfileKey, string? userAgent, DateTimeOffset receivedUtc, CancellationToken ct)`. The slice-002 `PageviewCapturedHandler` calls it **before** building the receipt + enqueuing the write op. The `userAgent` argument is sourced from `notification.Pageview.UserAgent` (cross-product prerequisite, see [`customizer-prereq.md`](customizer-prereq.md) — Customizer's `PageviewCaptureMiddleware` captures UA synchronously on the request thread and threads it through the `PageviewCaptured` notification as the 10th positional record param). This is reliable regardless of handler thread timing because the UA value is part of the immutable `Pageview` record carried by the notification, not a read off the (potentially-disposed) `HttpContext`. The resolver does (in order):

1. Compute `deviceKey = DeviceKeyHasher.Compute(userAgent)` (§5).
2. Check the in-memory LRU cache (`AnalyzerSessionCacheStore`; §2) for `(visitorProfileKey, deviceKey)`.
3. If cache hit AND `cacheEntry.LastActivityUtc + inactivityTimeout >= receivedUtc` → **extend**: `repository.ExtendAsync(cacheEntry.SessionKey, receivedUtc)` returns the post-update `(StartUtc, PageviewCount)` (single UPDATE with `OUTPUT INSERTED.startUtc, INSERTED.pageviewCount` on SQL Server; SELECT-after-UPDATE in same scope for SQLite); update the cache entry's `LastActivityUtc`. Project to `AnalyticsSession` client-side from the cache entry + returned columns (NO second SELECT). Return `SessionResolutionResult { cacheEntry.SessionKey, projection }`.
4. If cache hit AND stale → **close + open**: `repository.CloseAsync(cacheEntry.SessionKey, cacheEntry.LastActivityUtc + inactivityTimeout)` (UPDATE `isActive = false, endUtc = …`); invalidate cache entry; fall through to step 5.
5. If cache miss → `repository.GetLatestActiveAsync(visitorProfileKey, deviceKey)`:
   - Found AND fresh → extend with `OUTPUT`-returned post-update columns; project client-side; cache; return.
   - Found AND stale → close (as step 4), fall through to step 6.
   - Not found → step 6.
6. **Open**: `repository.InsertAsync(new AnalyzerSessionDto { …, isActive = true, … })`. On unique-violation against the partial unique index `UX_analyzerSession_active_visitor_device` → re-read with `GetLatestActiveAsync` (a concurrent dispatcher won the race; attach to its session — also extends with `OUTPUT` columns).
7. Write the new session to the cache; return its `SessionKey` + the just-constructed projection (no SELECT needed; the projection's columns are all known at insert time).

**Per-request SQL budget** (FR-009 ≤ 2 indexed statements per resolution after A7 remediation):

| Path | Statements | Notes |
|---|---|---|
| Cache hit + extend | 1 UPDATE (with OUTPUT) | post-update columns returned in one round-trip; projection built client-side |
| Cache miss + fresh DB row + extend | 1 SELECT + 1 UPDATE (with OUTPUT) | |
| Cache hit + stale + open new | 1 UPDATE (close) + 1 INSERT | partial-unique-collision retry adds 1 SELECT + 1 UPDATE in the rare race case |
| Cache miss + no row + open new | 1 SELECT + 1 INSERT | partial-unique-collision retry same as above |
| Cache miss + stale row + open new | 1 SELECT + 1 UPDATE (close) + 1 INSERT | bounded ≤ 3; lazy-close branch authorised by FR-009 |

The handler then builds the receipt with `SessionKey` populated and enqueues the write op as before.

**Rationale**: the read-extend-or-open flow is the standard pattern for analytics session resolution and matches the spec's FR-002 step-by-step description verbatim. Locating the resolver call in the handler (NOT inside the dispatcher's batch flush) is load-bearing: the receipt's `SessionKey` FK must be valid by the time the dispatcher inserts the receipt row, and the dispatcher flushes batches asynchronously. The synchronous-on-handler-thread bound is acceptable because (a) cache hit costs ~1 ms (single UPDATE), (b) FR-010 budgets ≤ 3 ms p95 delta which the cache amortises across the steady-state working set, (c) the receipt's enqueue is fire-and-forget per slice-002 contract — the session-side writes are the only synchronous DB cost added.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Resolve sessions inside the dispatcher's batch flush | Reorders the FK availability — the receipt would have a `sessionKey` that wasn't durable when the receipt insert ran. Concurrent dispatchers would race. Also breaks the per-pageview state-store update (handler can't store a session reference it doesn't have). |
| Resolve sessions in a separate `INotificationAsyncHandler<PageviewCaptured>` | Two handlers race against the same notification on different `Task.Run` threads; the receipt handler can't observe the session handler's result. Would require an in-memory map keyed by `Pageview.Key`, with eviction policy and concurrency concerns. The single-handler-orchestrates-both approach is simpler. |
| Push session resolution into the queue write op (op carries `userAgent`; dispatcher resolves) | Same FK-availability problem as above. Also defers a CPU + SQL cost to a background thread that's already the throughput bottleneck. |
| Eager-create-then-merge (always insert a fresh session, reconcile later) | Multiplies row counts; complicates aggregation queries. Violates the "exactly one active session per `(visitor, device)`" invariant the spec elevates as load-bearing for slice 005 + 010. |

---

## §2 — In-memory active-session cache (LRU)

**Decision**: wrap `Microsoft.Extensions.Caching.Memory.MemoryCache` in `AnalyzerSessionCacheStore`. Backed by a `MemoryCache` constructed with `MemoryCacheOptions { SizeLimit = options.CacheCapacity }` (default 10 000 entries); each entry has `Size = 1`. Key: `string` of shape `$"{visitorProfileKey:N}|{deviceKey}"`. Value: `record AnalyticsSessionCacheEntry(Guid SessionKey, DateTimeOffset LastActivityUtc, DateTimeOffset OpenedUtc)`. Sliding-expiration set to `inactivityTimeout * 2` so a session evicted between pageviews still falls back to a fresh DB read; absolute-expiration not set (the sweeper handles long-tail closes).

The store exposes:

- `bool TryGet(visitorProfileKey, deviceKey, out entry)` — wraps `MemoryCache.TryGetValue`.
- `void UpdateActivity(visitorProfileKey, deviceKey, sessionKey, lastActivityUtc)` — overwrites the entry; refreshes sliding expiration.
- `void Invalidate(visitorProfileKey, deviceKey)` — `MemoryCache.Remove`.
- `void InvalidateBySessionKey(sessionKey)` — needed by the sweeper's invalidation broadcast when it closes a row. Implemented by walking the cache's keys (O(N) but N ≤ CacheCapacity).

**Rationale**: `MemoryCache` is concurrent-by-design (per .NET 8+ docs; lock-free reads, lock-on-write); already a transitive dependency of ASP.NET Core's `IDistributedCache` adapter — no new NuGet package added. The `SizeLimit + Size=1` pattern enforces a bounded entry count without needing a hand-rolled LRU. Sliding expiration of `inactivityTimeout * 2` is the natural keepalive window (an entry that's been silent for longer than the inactivity timeout is no longer authoritative; falling back to a DB read at that point is correct, not a performance penalty). The `InvalidateBySessionKey` O(N) walk is acceptable because (a) the sweeper runs every 60s by default, not per-pageview, (b) cache capacity is bounded.

Spec Assumption note: cross-instance cache coherence is NOT maintained. Two Umbraco instances behind a load balancer may briefly hold different active-session pointers for the same `(visitor, device)`; the DB-level partial unique index (§4) serialises the collision and the losing instance falls back to a fresh read.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `ConcurrentDictionary<Tuple<Guid,string>, …>` | No bounded eviction policy out of the box; would require a hand-rolled LRU layer (linked list + lock or `Interlocked.CompareExchange` on a doubly-linked queue). MemoryCache solves this. |
| `LRUCache` from a third-party NuGet (e.g., BitFaster.Caching) | New dependency; MemoryCache already covers the need. Diff-reduction with Customizer is preferred. |
| Redis / distributed cache | Cross-instance coherence is explicitly out of scope (Assumption); no operational benefit at this scale; adds infrastructure complexity and a network dependency for the hot path. |
| Per-request scoped cache | Wrong scope — sessions span requests by definition. Per-request cache would never hit. |
| No cache; always hit the DB | Spec FR-010 ≤ 3 ms p95 delta budget would be at risk on a cold visitor working set. Customizer's `IVisitorProfileStore` pattern (warm-cache hot visitor profiles) is the binding precedent. |

---

## §3 — Cascade-step pattern (soft-anonymise, atomic-rollback)

**Decision**: `AnalyzerSessionCascadeStep : IAnonymizationCascadeStep` is an `internal sealed` class under `Analyzer.Features.Sessions.Application.Anonymization`. Its `ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)` delegates to `IAnalyzerSessionRepository.SoftAnonymizeByVisitorKeyAsync(visitorProfileKey, ct)`, which runs a single indexed UPDATE:

```sql
UPDATE analyzerSession
SET anonymizedUtc = SYSUTCDATETIME(),
    deviceKey = ''
WHERE visitorProfileKey = @visitorProfileKey
  AND anonymizedUtc IS NULL
```

The repository ALSO calls `AnalyzerSessionCacheStore.InvalidateByVisitorKey(visitorProfileKey)` after the scope completes, to evict any active-session cache entries for that visitor. Registered via `builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerSessionCascadeStep>()` in `AnalyzerComposer` alongside slice-002's `AnalyzerEventReceiptCascadeStep` registration.

**Rationale**: the soft-anonymise choice is the spec's load-bearing Assumption — session-level aggregates (visit counts per content node, average pages per session) are load-bearing for slice 005's content app and slice 010's reports, and hard-deleting rows would create artificial dips in those aggregates whenever a single visitor is anonymised. Principle IV v1.1.1 explicitly authorises this per-table choice — clarifying the cascade-step semantic menu (delete / soft-delete / re-projection) precisely to support this kind of decision.

The `WHERE anonymizedUtc IS NULL` predicate makes the operation idempotent: a second cascade run on the same visitor is a no-op (zero rows affected). The cache-invalidate-after-scope-complete is the correct ordering — if the UPDATE rolls back (cascade orchestrator throws), the cache must NOT be invalidated, or the next pageview would open a duplicate active session for the un-anonymised visitor.

The slice-002 receipt cascade hard-deletes, slice-003 sessions soft-anonymise. **The two are disjoint** — receipts live on the per-row attribution axis (where individual-attribution is the value), sessions live on the aggregate axis (where aggregate counts are the value). The cross-product analytics rollups in slices 005 + 010 project from sessions, not receipts, so removing per-row receipt rows doesn't affect aggregate counts.

Step ordering: both `AnalyzerEventReceiptCascadeStep` and `AnalyzerSessionCascadeStep` register as `IAnonymizationCascadeStep` and run inside Customizer's outer NPoco scope. They operate on disjoint tables; ordering is irrelevant. Customizer's `AnonymizeVisitorProfileHandler` invokes registered cascade steps in registration order (per Customizer slice 007 contract); the order is not load-bearing for correctness.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Hard-delete (match slice 002's receipt precedent) | Spec Assumption explicitly rejects this — destroys session-level aggregates load-bearing for slice 005 + 010. CCPA right-to-delete is satisfied by removing the identifying `deviceKey` and marking the row anonymised; the aggregate columns (`pageviewCount`, timestamps) carry no PII. |
| Re-key (move rows to an anonymised visitor key) | Customizer keeps the same `customizerVisitorProfile.Key` on anonymisation (it overwrites `IdentityRef` from `oid:…` to `anonymized:…` while preserving the row). There is no "anonymised key" to re-key TO. |
| Delete `deviceKey` only, leave `anonymizedUtc` null | Loses the queryable marker that distinguishes pre-anonymisation rows from post-anonymisation rows. Auditors will ask "how many sessions were anonymised this quarter"; the column makes that a trivial query. |
| Skip the cascade step entirely | Constitution Principle IV v1.1.1 — every new Analyzer table MUST register a cascade step. Constitution Check fails. |

---

## §4 — Concurrent-pageview race-safety

**Decision**: enforce the "exactly one active session per `(visitorProfileKey, deviceKey)`" invariant at the DB layer via a partial unique non-clustered index:

```sql
CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerSession_active_visitor_device]
ON [analyzerSession] ([visitorProfileKey], [deviceKey])
WHERE [isActive] = 1
```

The resolver's open path is **insert-with-collision-retry**:

```csharp
try
{
    await _repository.InsertAsync(newSession, ct);
    return newSession.SessionKey;
}
catch (DbException ex) when (IsUniqueConstraintViolation(ex))  // reuse slice-002 detection
{
    // Concurrent dispatch won the race; re-read what they wrote and extend it.
    var winner = await _repository.GetLatestActiveAsync(visitorProfileKey, deviceKey, ct);
    if (winner is not null)
    {
        await _repository.ExtendAsync(winner.SessionKey, receivedUtc, ct);
        _cacheStore.UpdateActivity(visitorProfileKey, deviceKey, winner.SessionKey, receivedUtc);
        return winner.SessionKey;
    }
    throw;  // unique violation but no row found — shouldn't happen; bubble up.
}
```

`IsUniqueConstraintViolation` is **the slice-002 helper from `AnalyzerEventReceiptRepository`** — provider-agnostic, checks `ErrorCode` against SQL Server 2627/2601 + SQLite 19, falls back to ANSI SQLSTATE `23xxx`. Extract into a shared internal `Analyzer.Features.Common.Persistence.UniqueConstraintViolationDetector` (or equivalent location) so both repositories use the same predicate.

SQLite skip path: partial unique indexes ARE supported in SQLite (3.8.0+), BUT Umbraco's NPoco grammar doesn't reliably emit them. Migration body uses raw `CREATE UNIQUE INDEX … WHERE` SQL on SQL Server; on SQLite, skip the partial index AND rely on application-layer serialisation (the single-instance dev path; CI doesn't exercise concurrent-pageview races against SQLite).

**Rationale**: the partial unique index is the cheapest correctness mechanism — no application-layer locking, no distributed lock, no semaphore. The collision is observable as a unique-violation, which the resolver catches and treats as "someone else opened the session; attach to theirs." This is the same idiom slice 002 uses for receipt idempotency (`research.md` §8 there).

Under steady-state load, the partial unique index is **never** violated (each visitor+device has at most one active session at all times, by construction). The collision case is the millisecond-scale race between two simultaneous pageview dispatches for the same `(visitor, device)` opening sessions in parallel — observed under stress tests, never observed under normal traffic.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Application-layer `lock` on `(visitor, device)` | Cross-process semantics broken (two Umbraco instances would each have their own lock). Adds contention point in steady state. |
| Distributed lock (Redis / SQL row lock) | Operational complexity; adds an external dependency for a problem the DB index already solves cleanly. |
| `MERGE … WHEN MATCHED … WHEN NOT MATCHED …` statement | Cross-provider grammar differences (SQL Server vs PostgreSQL `INSERT … ON CONFLICT`); complicates repository contract. The insert-catch-re-read idiom is portable. |
| Single-writer per-visitor channel (Customizer's `VisitorWriteQueue` precedent) | Throughput regression — sessions are per-`(visitor, device)`, not per-visitor; serialising every visitor's sessions through one channel is overkill. The partial unique index already serialises the rare collision. |

---

## §5 — `deviceKey` derivation

**Decision**: `DeviceKeyHasher.Compute(string? userAgent)` returns a stable 16-hex-character lowercase string derived from `userAgent` via:

```csharp
public static string Compute(string? userAgent)
{
    var normalised = (userAgent ?? string.Empty).Trim();
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalised));
    // Truncate to first 8 bytes (16 hex chars). UA cardinality per
    // organisation is ≤ a few hundred — 16 hex collisions are
    // astronomically unlikely at this scale.
    return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
}
```

The UA value is sourced from `notification.Pageview.UserAgent` (cross-product prerequisite — Customizer captures it on the request thread; see [`customizer-prereq.md`](customizer-prereq.md)). A null or whitespace-only UA hashes the empty string deterministically, producing a fixed sentinel device key. This is rare (every real HTTP client sends a UA; the only path that wouldn't is a misconfigured test, a non-HTTP code path, or a pageview captured before the Customizer-side UA prereq lands) and explicitly tolerated by the spec's edge-case note.

**Rationale**: spec Assumption #1 binds the choice. Truncated SHA-256 over the UA string is reproducible across requests, has bounded cardinality (≤ a few hundred distinct UA strings per organisation observed in practice — Chrome, Edge, Firefox, Safari × major version × OS family), and requires zero new storage. No cookie surface (product invariant from `CLAUDE.md`); no fingerprint-grade entropy (only the UA, which is already in the request). The UA reaches Analyzer via the immutable `Pageview` record on the notification, NOT via `IHttpContextAccessor` — the latter would be unreliable under typical fire-and-forget timing (handler runs on a `Task.Run` thread after the request scope is disposed; `HttpContextAccessor.HttpContext` returns null per ASP.NET Core's `HttpContextHolder` clearing pattern). See `/speckit-analyze` finding C1 for the deeper architectural rationale.

16-hex-character truncation gives a 64-bit hash space, enough that birthday-paradox collisions only become non-trivial above ~4 billion distinct UAs (far beyond any plausible intranet workload). Customizer's slice-003 visitor-profile cache uses 16-hex as the storage form for hashed UA values where it surfaces them; matching that form keeps the two products' debugging surfaces symmetric.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Persistent server-issued cookie (random Guid per device) | Violates product invariant "no cookie-consent / opt-out surface." Adds a backoffice operational surface (cookie purge, cookie rotation). |
| Read UA from `IHttpContextAccessor.HttpContext.Request.Headers.UserAgent` at handler-entry | UNRELIABLE under fire-and-forget timing — handler typically runs after request scope is disposed; `HttpContext` returns null. Was the original plan in this slice's draft; revised after `/speckit-analyze` finding C1 surfaced the issue. Now sourced from `notification.Pageview.UserAgent` (Customizer captures on request thread; immutable record carries the value through to the handler). |
| Browser fingerprinting (UA + Accept-Language + IP + screen-res via JS) | Outsizes the privacy posture for an intranet; the deploying organisation's compliance team would object. CCPA right-to-delete obligation also makes finer fingerprinting less defensible. |
| Full SHA-256 (256 bits) instead of truncated | Storage cost negligible difference; reduced readability in audit logs and `analyzerSession` queries; not motivated by collision-rate concerns at this scale. |
| MD5 truncation | Cryptographic deprecation flag in static analyzers (SHA-256 is the default modern choice); no performance difference at UA-string length. |
| Per-IP keying | Multiple users behind NAT (typical corporate intranet) collapse to one device; violates the spec's invariant. |
| `ITrackingProtectionService` / IUA-CH client-hints | Future direction; intranet browsers may not send IUA-CH headers reliably; harder to reason about cardinality. Out of scope for slice 003. |

---

## §6 — `IAnalyticsEventStateProvider.CurrentSession` addition

**Decision**: extend the slice-002 `IAnalyticsEventStateProvider` interface with an additional read-only member:

```csharp
public interface IAnalyticsEventStateProvider
{
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }  // slice 002
    AnalyticsSession? CurrentSession { get; }              // slice 003 — NEW
}
```

The backing store (`AnalyticsEventStateStore`) gains a parallel `CurrentSession` field + `SetCurrentSession(AnalyticsSession session)` mutator. The slice-002 handler's existing `TryUpdateInFlightStateStore(receipt)` is extended to also call `SetCurrentSession(session)` (constructed from the resolver's return value + repository read).

**Rationale**: the public read surface for in-process consumers of session state. Slice 005's content app and slice 010's reports both need a `(currentRequestReceipt, currentSession)` pair to attribute the current pageview correctly. Per Principle X (Extensibility by Design), additive interface changes are permitted in MINOR releases. The pinning baseline regenerates accordingly.

The `CurrentSession` return is `null` in the same cases as `CurrentRequestReceipt` is null: on a pageview request itself, the handler may not have completed before the request scope is disposed (Customizer's fire-and-forget dispatch). The in-request consumer pattern (slice 004's `analyzer.send(...)` flow) sees a populated `CurrentSession`.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Separate interface `IAnalyticsSessionStateProvider` | Each slice adding its own provider interface produces N injection points per consumer. Customizer's pattern unifies into one `IAnalyticsStateProvider`; Analyzer mirrors that. |
| Return the session inside `AnalyticsEventReceipt` (as a nested record) | The receipt and session are independent — a receipt may exist without an in-process session (post-anonymisation; the receipt was hard-deleted at slice-002 anonymisation; sessions persist as soft-anonymised aggregates). Coupling them in the projection is wrong. |
| Defer to slice 005 (when the content app needs it) | Forces a slice-005 baseline regeneration with a slice-003 change cause — confusing audit trail. Better to land the additive member with the slice that introduces sessions. |

---

## §7 — Migration pattern (M0002)

**Decision**: `M0002_AddAnalyzerSessionTableAndReceiptSessionKey : AsyncMigrationBase`. Extends Analyzer's existing migration plan (chained after `M0001`). Idempotent via `TableExists` + `ColumnExists` guards. Migration body:

1. **Create the `analyzerSession` table** via `Create.Table<AnalyzerSessionDto>().Do()`, guarded by `TableExists(Constants.Database.AnalyzerSession) is false`. The DTO carries `[Index(IndexTypes.UniqueNonClustered)]` on `sessionKey` and `[Index(IndexTypes.NonClustered)]` on `(isActive, lastActivityUtc)` + on `visitorProfileKey`. The partial unique index `UX_analyzerSession_active_visitor_device` is **not** declarable via NPoco's `[Index]` attribute (no `WHERE` clause support); it's added via raw SQL in the migration body.
2. **Declare the hard FK** `FK_analyzerSession_VisitorProfile (visitorProfileKey) REFERENCES customizerVisitorProfile(key)` via raw SQL, skipping SQLite per lesson #39.
3. **Declare the partial unique index** via raw SQL: `CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerSession_active_visitor_device] ON [analyzerSession] ([visitorProfileKey], [deviceKey]) WHERE [isActive] = 1`. SQL Server only; skipped on SQLite (lesson #39).
4. **Add the `sessionKey` column** to `analyzerEventReceipt` if `ColumnExists(Constants.Database.AnalyzerEventReceipt, "sessionKey") is false`. Use `Alter.Table(Constants.Database.AnalyzerEventReceipt).AddColumn("sessionKey").AsGuid().Nullable().Do()`. NO back-fill (per FR-004 — pre-sessions cohort keeps `sessionKey = null`).
5. **Add the index** on the new column: `Create.Index("IDX_analyzerEventReceipt_sessionKey").OnTable(Constants.Database.AnalyzerEventReceipt).OnColumn("sessionKey").Ascending().Do()`.

The migration plan in `AnalyzerMigrationPlan.cs` chains:

```csharp
From(string.Empty)
    .To<M0001_AddAnalyzerEventReceiptTable>("0001-AddAnalyzerEventReceiptTable")
    .To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey");
```

**Rationale**: `AsyncMigrationBase` + `TableExists`/`ColumnExists` guards + raw-SQL escape for non-trivial schema declarations is the established pattern from `M0001` (slice 002) and Customizer's `M0009`. Partial unique indexes are a SQL Server feature NPoco doesn't model; raw SQL is the canonical solution. The two changes (new table + additive column) are deliberately bundled in one migration because they are logically one slice — pre-emptively splitting would force operators to track two migration states for one logical schema change.

No back-fill is correct per FR-004: pre-slice-003 receipts have no session attribution to recover. Adding a null column does not lock the table for long on SQL Server (modern SQL Server adds nullable columns as a metadata-only operation — no row rewrite needed).

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Split into two migrations (M0002 = session table; M0003 = receipt column) | Couples operators to two migration phases for one slice; no rollback granularity benefit because both migrations are additive and idempotent. |
| Back-fill `sessionKey` on the migration | Pre-slice-003 receipts have no `(visitorProfileKey, deviceKey)` pair to attribute against — `deviceKey` was never computed for them. Any back-fill would be synthetic. The "no back-fill; pre-sessions cohort" decision is explicit in FR-004. |
| Declare partial unique index via NPoco | NPoco's `[Index]` attribute doesn't model `WHERE` clauses; would silently emit a full unique index, which would forbid more than one session row per `(visitor, device)` across the table's history (wrong — historical closed sessions stack up). Raw SQL is required. |
| Use an Umbraco `Database.Execute` UPSERT to back-fill from `customizerPageview` joins | Cross-product join with a `User-Agent` source we never persisted — no `User-Agent` column on `customizerPageview`. Synthetic again. |

---

## §8 — Sweeper background-service pattern

**Decision**: `AnalyzerSessionSweeperService : BackgroundService`, hosted-service lifetime (`AddHostedService<>`). Loop body:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        var options = _options.CurrentValue;
        var inactivity = TimeSpan.FromMinutes(options.InactivityTimeoutMinutes);
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - inactivity;
        var closed = await _repository.SweepEligibleAsync(
            cutoff: cutoff,
            inactivityTimeout: inactivity,
            batchSize: options.SweepBatchSize,
            stoppingToken);
        if (closed > 0)
        {
            _logger.LogDebug("Analyzer session sweeper closed {Count} sessions", closed);
        }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        break;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Analyzer session sweeper tick failed");
    }

    try
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.CurrentValue.SweepIntervalSeconds));
        await Task.Delay(interval, stoppingToken);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
        break;
    }
}
```

`SweepEligibleAsync` issues a single batch UPDATE:

```sql
UPDATE TOP (@batchSize) analyzerSession
SET isActive = 0,
    endUtc = DATEADD(SECOND, @inactivitySeconds, lastActivityUtc)
WHERE isActive = 1
  AND lastActivityUtc < @cutoff
```

Returns the affected row count. Each session is closed with `endUtc = lastActivityUtc + inactivityTimeout` (the **logical** close time, NOT `now` — the spec Assumption explicitly binds this to protect session-duration metrics). On SQL Server, `UPDATE TOP (@n)` is the bounded-batch syntax; SQLite uses `UPDATE … WHERE rowid IN (SELECT rowid … LIMIT @n)`.

After each batch, the sweeper invalidates any cache entries pointing at closed `SessionKey`s via `AnalyzerSessionCacheStore.InvalidateBySessionKey(sessionKey)`. The cache invalidation cost is bounded by the batch size + the cache size; the sweeper enumerates the closed-session keys returned by the UPDATE's `OUTPUT INSERTED.sessionKey` clause (SQL Server) or the equivalent on SQLite.

**Rationale**: the spec's FR-007 binds the shape. Logical-close-time (`lastActivityUtc + inactivityTimeout`) is the load-bearing semantic — using wall-clock `now` would inflate session-duration metrics by up to one sweeper interval per long-tail session, biasing the average. Bounded batches (`SweepBatchSize`) keep individual UPDATE statements well below any reasonable SQL statement timeout.

The exception-swallow-and-continue pattern matches slice-002's `AnalyzerEventReceiptWriteDispatcher` precedent (`research.md` §2 there) — a poisoned tick logs at error level and the loop continues. The sweeper is the long-tail correctness mechanism; a missed tick is recoverable on the next one.

`IOptionsMonitor` is the right binding for runtime-reload: operators can adjust `InactivityTimeoutMinutes` / `SweepIntervalSeconds` / `SweepBatchSize` via `appsettings.json` and the sweeper picks up the new value at its next tick without a host restart.

Multi-instance behaviour: two Umbraco instances both running the sweeper produce idempotent UPDATEs — `isActive = 0` set twice is the same as once. The `endUtc = lastActivityUtc + inactivity` is identical across instances reading the same row. No distributed lock needed (spec edge case explicit).

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `IHostedService` with `Quartz.NET` / `Hangfire` | New dependency for a problem `BackgroundService` solves directly. Customizer doesn't use either. |
| Per-tick distributed lock (only one instance sweeps at a time) | Single-point-of-failure; if the lock-holder crashes mid-sweep, recovery is operationally complex. Idempotent UPDATEs sidestep this. |
| Sweep inside the resolver (close-stale-on-read only; no background) | The "single-pageview session" case (visitor reads one page and never returns) never gets closed — FR-007 explicit. |
| `EndUtc = now` (sweeper observation time) | Inflates session-duration metrics; spec Assumption #5 explicit. Wrong for analytics. |
| Unbounded UPDATE (no `TOP`/`LIMIT`) | Holds a long-running transaction on the eligible-row set; risks SQL timeout under steady-state load. Bounded batches are the standard pattern. |

---

## §9 — `IOptionsMonitor` vs `IOptions` for session config

**Decision**: bind `AnalyzerSessionOptions` via `builder.Services.Configure<AnalyzerSessionOptions>(builder.Config.GetSection("Analyzer:Session"))` and inject `IOptionsMonitor<AnalyzerSessionOptions>` everywhere session config is consumed (resolver, sweeper, cache store). The resolver reads `monitor.CurrentValue.InactivityTimeoutMinutes` once per `ResolveAsync` call; the sweeper reads `monitor.CurrentValue.SweepIntervalSeconds` each loop tick; the cache store reads `monitor.CurrentValue.CacheCapacity` at construction time only (cache capacity is bound to the `MemoryCache` instance — changes to capacity require a cache rebuild, which is deferred until host restart for operational simplicity).

**Rationale**: spec FR-008 binds reloadable config. `IOptionsMonitor` is the correct DI primitive for that (the slice-002 dispatcher already uses it for `AnalyzerWriteQueueOptions`). The single-cache-instance bound on capacity is a known limitation; documented in the contract.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `IOptions<T>` (snapshot at first read) | Doesn't reload — operators would need a host restart to retune the inactivity timeout. Violates FR-008. |
| `IOptionsSnapshot<T>` (per-request snapshot) | Scoped only; resolver + sweeper + cache store are all singleton or hosted services. Wrong DI lifetime. |
| Hand-rolled `IConfiguration.OnChange` callback | Reinvents `IOptionsMonitor` with worse ergonomics. |

---

## §10 — Pinning baseline regeneration

**Decision**: regenerate `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` as part of slice 003. The diff is purely additive:

1. **NEW**: `TYPE Analyzer.Analytics.AnalyticsSession : sealed class : interfaces=[IEquatable<AnalyticsSession>]` (a public sealed record).
2. **MODIFIED**: `TYPE Analyzer.Analytics.IAnalyticsEventStateProvider` gains the line `PROP Analyzer.Analytics.AnalyticsSession CurrentSession { get; }`.
3. **MODIFIED**: `TYPE Analyzer.Analytics.AnalyticsEventReceipt` gains the line `PROP System.Nullable\`1[[System.Guid…]] SessionKey { get; init; }` (the additive init-only property — see §11).

Regen procedure (per slice-002 lesson #37): `ANALYZER_REGENERATE_SNAPSHOTS=1 dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "FullyQualifiedName~PublicSurfacePinningTests"`. The slice's spec carries a Sync Impact-style note in Assumptions (§pinning regen) documenting the additive change; Principle X classifies it as MINOR-additive (no breaking member rename or removal).

**Rationale**: deliberately writing the baseline diff into the slice's PR makes the public-surface change reviewable. Auto-regeneration without spec acknowledgement is a class of bug Customizer's analogous test was designed to prevent. The slice-002 `PublicSurfacePinningTests` test code itself doesn't need to change — its baseline-or-regen logic is unchanged.

**Alternatives considered**: none — slice 002 established the regen pattern; slice 003 inherits it.

---

## §11 — `AnalyticsEventReceipt.SessionKey` shape

**Decision**: extend `Analyzer.Analytics.AnalyticsEventReceipt` with an additive init-only property:

```csharp
public sealed record AnalyticsEventReceipt(
    Guid Id,
    Guid PageviewKey,
    Guid VisitorProfileKey,
    DateTimeOffset ReceivedUtc)
{
    /// <summary>
    /// Soft FK to <c>analyzerSession.sessionKey</c>. Populated by
    /// slice-003's session resolver before the receipt is enqueued.
    /// Null for receipts persisted by slice-002 deployments (pre-sessions
    /// cohort — no back-fill).
    /// </summary>
    public Guid? SessionKey { get; init; }
}
```

NOT a positional parameter — positional parameters generate a new constructor signature, which would binary-break callers compiled against slice 002. The init-only property pattern is binary-compatible additively; slice-002 callers continue to construct `new AnalyticsEventReceipt(id, pvKey, visKey, utc)` and the property defaults to null. Slice-003 code constructs `new AnalyticsEventReceipt(...) with { SessionKey = sessionKey }`.

**Rationale**: Principle X — "MINOR releases MAY add behaviour-compatible members." A positional-param extension is a *breaking* change (new ctor signature); an init-only property is *additive* (the prior ctor signature is preserved). Pinning baseline picks up the new property line; consumer code reading the receipt sees `SessionKey` populated for slice-003-and-later rows, null for slice-002-only rows.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Add `Guid? SessionKey` as a positional ctor parameter | Binary-breaking against slice-002 callers. Violates Principle X. |
| Don't put SessionKey on the public record; carry it only on the internal DTO + write op | Loses the consumer correlation in `CurrentRequestReceipt` — a slice-004+ consumer reading the receipt would have to do a separate `CurrentSession` lookup and compare SessionKeys. Coupling at the record level is cleaner. |
| Return a new wrapper record `AnalyticsEventReceiptWithSession(receipt, sessionKey)` | API noise; consumers want one record, not two. The init-only property is the idiomatic shape. |

---

## §12 — Integration-test substrate

**Decision**: reuse slice-002's `AnalyzerIntegrationTestBase` verbatim. New integration test classes live under `src/Analyzer.Tests/Integration/Sessions/` and inherit from the same base, with `[Trait("Category", "Integration")]` so CI's exclusion filter (`-trait- "Category=Integration"`; lessons #31 + #32) keeps them out of the PR-blocking run. Local-dev runs invoke them via the Aspire AppHost's persistent SQL container.

**Rationale**: the test base is already field-tested across slice-002's eight integration tests. Adding new test classes is the cheapest path; no schema-isolation rework needed. The migration plan now chains `M0002` after `M0001`, so the test base's existing "run migrations once at fixture init" path picks up the new schema automatically.

**Alternatives considered**: none — the slice-002 substrate solves the problem.

---

## §13 — Composer ordering

**Decision**: slice 003 extends the existing `AnalyzerComposer.ConfigureServices` method with the new registrations:

```csharp
services.Configure<AnalyzerSessionOptions>(
    builder.Config.GetSection("Analyzer:Session"));

services.AddSingleton<AnalyzerSessionCacheStore>();
services.AddScoped<IAnalyzerSessionRepository, AnalyzerSessionRepository>();
services.AddScoped<IAnalyzerSessionResolver, AnalyzerSessionResolver>();
services.AddHostedService<AnalyzerSessionSweeperService>();

// US2 cascade step — add second IAnonymizationCascadeStep registration
services.AddScoped<IAnonymizationCascadeStep, AnalyzerSessionCascadeStep>();
```

No `[ComposeAfter]` change needed — the existing `[ComposeAfter(typeof(VisitorAnalyticsComposer))]` covers ordering against Customizer. Within Analyzer, registrations are order-independent (no service depends on another's registration order at composition time).

**Rationale**: minimal-diff principle. The composer is already the central wiring point; adding service descriptors in the existing `ConfigureServices` body keeps registration discoverable. The cascade step registration sits next to slice-002's cascade step registration, making the cascade-step landscape obvious to readers.

**Alternatives considered**: split into a new `SessionComposer` class — rejected because the existing single-composer-with-fail-fast-Customizer-check pattern is the Customizer-symmetric shape; splitting would diverge.

---

## §14 — Reference inventory

Source files in `../customizer/src/Customizer/` referenced by this research:

| Path | Purpose in this slice |
|---|---|
| `Features/Visitors/Application/Contracts/PageviewCaptured.cs` | Notification record + subscriber contract docstring (§1). |
| `Features/Visitors/Application/Contracts/Anonymization/IAnonymizationCascadeStep.cs` | Cascade-step contract (§3). |
| `Features/Visitors/Application/Commands/AnonymizeVisitorProfileCommand.cs` | Outer-scope orchestration; confirms throw-rolls-back semantics across multiple registered cascade steps (§3). |
| `Features/Goals/Application/Anonymization/GoalReachedCascadeStep.cs` | Hard-delete cascade-step precedent (counter-example; slice 003 deliberately diverges per §3). |
| `Features/Visitors/Infrastructure/VisitorProfileStore.cs` | LRU-cache-of-visitor-profiles precedent (warm-set pattern; §2). |
| `Migrations/M0009_AddDocumentTypeSegmentationTables.cs` | `AsyncMigrationBase` + raw-SQL FK pattern (§7). |
| `Composers/VisitorAnalyticsComposer.cs` | Composer wiring of multi-component domain (§13). |

Files in `src/Analyzer/` referenced by this research (slice 001 + 002 baseline):

| Path | Purpose in this slice |
|---|---|
| `Composers/AnalyzerComposer.cs` | Existing composer (extended; §13). |
| `Features/Events/Application/PageviewCapturedHandler.cs` | Existing handler (extended with resolver call; §1, §6). |
| `Features/Events/Application/AnalyticsEventStateStore.cs` | Existing state store (extended with CurrentSession field; §6). |
| `Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptRepository.cs` | `IsUniqueConstraintViolation` helper to share with sessions repository (§4). |
| `Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptDto.cs` | Extended with `SessionKey` column (§11; data-model.md §3). |
| `Migrations/AnalyzerMigrationPlan.cs` | Extended to chain M0002 (§7). |
| `Migrations/M0001_AddAnalyzerEventReceiptTable.cs` | Raw-SQL FK + SQLite skip precedent (§7). |
| `Analytics/IAnalyticsEventStateProvider.cs` | Public interface extended with `CurrentSession` (§6). |
| `Analytics/AnalyticsEventReceipt.cs` | Public record extended with init-only `SessionKey` (§11). |

No Customizer file is modified by this slice. All Customizer references are read-only.

---

## §15 — Open items deferred from research to `/speckit-tasks`

These are implementation details that the task generation phase will pin down concretely; none affects the plan's gate status or the data model.

- Default `CacheCapacity` (10 000 proposed in spec; tunable via `Analyzer:Session:CacheCapacity`).
- Default `SweepBatchSize` (1000 proposed in spec; tunable via `Analyzer:Session:SweepBatchSize`).
- Sliding-expiration multiplier for cache entries (`inactivityTimeout * 2` proposed; pin in task).
- Whether the perf-smoke test should also exercise the lazy-close path (US1 AS3) at sustained load — proposed yes, marked as a task open item.
- Whether to extract `IsUniqueConstraintViolation` to `Analyzer.Features.Common.Persistence` (shared with slice-002 receipt repository) or duplicate it inside the sessions repository — proposed extract.
- `Constants.Database.AnalyzerSession` string literal value: `"analyzerSession"` (matches slice-002's convention).
- `AnalyticsSession` record property names: spec FR-011 lists them — pin verbatim in `data-model.md`.
