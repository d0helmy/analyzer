# Quickstart — Slice 003: Sessions

**Feature**: `003-session-tracking`
**Audience**: a developer (or future agent session) opening this slice to implement, review, or extend it.
**Prereq**: slice 002 on `main` (`cc78f80`); slice 001 on `main`; Customizer's `PageviewCaptured` (slice 011, `05e989c`) and `IAnonymizationCascadeStep` (slice 007, already shipped) on Customizer's `main`; Docker Desktop running for the Aspire SQL container.

This quickstart is the load-bearing "you have the spec + plan, now what" doc. It gives you the **shortest path** from a fresh clone to a green slice-003 implementation, with the gotchas that aren't obvious from reading the contracts.

---

## TL;DR

```bash
# 1. Prereqs
docker info >/dev/null                                  # Docker must be running
dotnet --version                                        # need .NET 10 SDK

# 2. Clone + branch (this slice's branch already exists)
git clone git@github.com:d0helmy/analyzer.git
cd analyzer
git checkout 003-session-tracking

# 3. Boot the dev SQL container (Aspire AppHost; persistent volume per slice-001 lesson #19)
dotnet run --project aspire/Analyzer.AppHost --launch-profile https &
# Container takes ~5 s on a warm volume, minutes on first pull.

# 4. Build + run unit tests
dotnet build Analyzer.slnx
dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj \
    --no-build --configuration Release \
    -- -trait- "Category=Integration" -trait- "Category=Perf"

# 5. Run integration tests (opt-in; need the Aspire container OR Testcontainers)
dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj \
    --no-build --configuration Release \
    -- -trait "Category=Integration"

# 6. Run perf-smoke (opt-in)
dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj \
    --no-build --configuration Release \
    -- -trait "Category=Perf"

# 7. Regenerate the pinning baseline (one-time, after public surface additions)
ANALYZER_REGENERATE_SNAPSHOTS=1 dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj \
    --filter "FullyQualifiedName~PublicSurfacePinningTests"
# Then run the test WITHOUT the env var to confirm byte-match.

# 8. Verify in browser
# Aspire dashboard URL printed at boot, e.g. https://localhost:17120/login?t=<token>
# Open https://localhost:44364/umbraco — default install creds:
#   dev@analyzer.local / 1234567890aA!
# Render any front-end page as the dev user; query analyzerSession + analyzerEventReceipt:
#   SELECT TOP 10 * FROM analyzerSession ORDER BY startUtc DESC;
#   SELECT TOP 10 * FROM analyzerEventReceipt ORDER BY receivedUtc DESC;
# Should show one session row per (visitor, browser) burst; receipts carrying sessionKey FK.
```

---

## Reading order (if you want to understand before changing anything)

1. **`spec.md`** — the why. Three user stories (P1 resolve-and-attach, P2 cascade-soft-anonymise, P3 sweeper), Assumptions (deviceKey hash, soft-anonymise semantic).
2. **`plan.md`** — the what + where. Technical Context, Constitution Check (all 10 gates pass), Project Structure (new `Features/Sessions/` slice).
3. **`research.md`** — the why-this-way. Each design decision (session resolution flow, LRU cache, partial unique index, soft-anonymise, sweeper close semantic) with alternatives + the Customizer / slice-002 source-file references that ground it.
4. **`data-model.md`** — concrete schema: columns, types, indexes, the partial unique index, FK + sweep index, the `AnalyticsSession` record shape, the `AnalyticsEventReceipt.SessionKey` additive property.
5. **`contracts/*.md`** — what each new type does and how it's tested. Read `IAnalyticsEventStateProvider.md` first (revised); then `AnalyticsSession.md`; then the internal contracts (`AnalyzerSessionResolver.md`, `AnalyzerSessionCascadeStep.md`, `AnalyzerSessionSweeperService.md`).
6. **`tasks.md`** (produced by `/speckit-tasks`, NOT this command) — the ordered task list.

---

## The five things that aren't obvious from the docs

### 1. Session resolution is **synchronous** to the handler thread; receipts stay async.

The receipt write op continues to go through slice-002's bounded queue (async dispatch). The session resolution does **not** — it runs synchronously inside `PageviewCapturedHandler.HandleAsync` BEFORE the receipt is enqueued, because the receipt's `sessionKey` FK requires the session row to be durable at enqueue time.

Consequences:

- The handler now performs ≤ 3 indexed SQL statements synchronously per pageview (cache hit + extend = 1 UPDATE; cache miss + open = 1 SELECT + 1 INSERT; lazy-close = +1 UPDATE).
- The LRU cache (`AnalyzerSessionCacheStore`) reduces SQL pressure under steady state — 99% of pageviews from a warm working set hit the cache and produce a single UPDATE.
- Spec FR-010 budgets ≤ 3 ms p95 delta vs slice-002 baseline; the perf-smoke test verifies it.

### 2. The partial unique index is the **only** race-safety mechanism.

The spec's "concurrent dispatchers may open duplicate sessions" edge case is handled at the DB layer:

```sql
CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerSession_active_visitor_device]
ON [analyzerSession] ([visitorProfileKey], [deviceKey])
WHERE [isActive] = 1
```

There is **no application-level lock**. The resolver's insert-catch-re-read idiom is the canonical pattern (research §4). If you find yourself wanting a `lock` or `SemaphoreSlim`, **stop and read `research.md` §4** — that's been considered and rejected.

SQLite doesn't reliably emit the partial unique index from NPoco grammar. The migration skips it on SQLite (lesson #39). CI uses SQL Server via Testcontainers; the partial-unique-index assertion is reachable there.

### 3. The cascade step **soft-anonymises**, it does NOT hard-delete.

Slice-002's `AnalyzerEventReceiptCascadeStep` hard-deletes — receipts are per-row attribution, removable. Slice-003's `AnalyzerSessionCascadeStep` soft-anonymises — sessions carry aggregate value (visit counts, average pages per session) that's load-bearing for slice 005 + slice 010.

Constitution Principle IV v1.1.1 explicitly authorises this per-table choice. If you read older notes mentioning "delete" or "anonymise" interchangeably, the spec's Assumption #2 has the binding decision: **soft-anonymise**.

The cascade step UPDATEs `anonymizedUtc = now, deviceKey = ''` for rows `WHERE visitorProfileKey = @key AND anonymizedUtc IS NULL`. Idempotent re-runs are a no-op (the predicate excludes already-anonymised rows). Atomic rollback inside Customizer's outer scope works exactly as slice-002's receipt cascade — a throw rolls back all session UPDATEs along with the visitor row update.

### 4. The sweeper closes sessions with **logical** time, not wall-clock time.

`endUtc = lastActivityUtc + inactivityTimeout` is the **logical** session close time — when the inactivity window expired, not when the sweeper observed it. Spec Assumption #5 is load-bearing for session-duration metrics: using `now` (sweeper's wall-clock) would inflate every long-tail session's duration by up to one sweep interval.

If you find yourself writing `endUtc = _timeProvider.GetUtcNow()` in the sweeper's UPDATE statement, stop and re-read `research.md` §8 — that's wrong. The UPDATE statement uses `DATEADD(SECOND, @inactivitySeconds, lastActivityUtc)`.

### 5. `AnalyticsEventReceipt.SessionKey` is an **init-only** property, NOT a positional ctor param.

Adding a 5th positional parameter to the slice-002 record would binary-break callers compiled against the 4-arg constructor. Init-only properties on records ARE additive per Principle X.

In code:

```csharp
// WRONG (binary-breaking):
public sealed record AnalyticsEventReceipt(
    Guid Id, Guid PageviewKey, Guid VisitorProfileKey,
    DateTimeOffset ReceivedUtc, Guid? SessionKey);  // BAD

// CORRECT (additive):
public sealed record AnalyticsEventReceipt(
    Guid Id, Guid PageviewKey, Guid VisitorProfileKey,
    DateTimeOffset ReceivedUtc)
{
    public Guid? SessionKey { get; init; }  // GOOD
}
```

Handler construction uses `with`-expressions: `new AnalyticsEventReceipt(id, pvKey, visKey, utc) with { SessionKey = sessionKey }`.

The pinning baseline regen captures the new property line; the slice-002 positional ctor signature stays identical. This is what makes the change MINOR-additive instead of breaking.

---

## File-creation order (suggested for implementation)

Driven by dependency direction; avoids "can't build yet because X depends on Y." If you follow `tasks.md` (from `/speckit-tasks`) the ordering is already locked; this list is the conceptual progression.

1. **Constants + Configuration + Public records**
   - `Constants.Database.AnalyzerSession`
   - `AnalyzerSessionOptions`
   - `AnalyticsSession` (public record under `Analyzer.Analytics`)
   - `AnalyticsEventReceipt.SessionKey` init-only property (additive to slice-002 record)
2. **DTO + Migration**
   - `AnalyzerSessionDto`
   - `M0002_AddAnalyzerSessionTableAndReceiptSessionKey`
   - `AnalyzerEventReceiptDto.SessionKey` column (additive)
   - `AnalyzerMigrationPlan` — chain `M0002` after `M0001`
3. **Repository + Cache + Hasher**
   - `IAnalyzerSessionRepository` + `AnalyzerSessionRepository`
   - `AnalyzerSessionCacheStore` + `AnalyticsSessionCacheEntry`
   - `DeviceKeyHasher`
   - Optional: extract `IsUniqueConstraintViolation` to a shared internal helper consumed by both slice-002 and slice-003 repositories.
4. **Resolver**
   - `IAnalyzerSessionResolver` + `AnalyzerSessionResolver`
5. **Cascade step**
   - `AnalyzerSessionCascadeStep`
6. **Sweeper**
   - `AnalyzerSessionSweeperService`
7. **State store + provider extensions**
   - Extend `AnalyticsEventStateStore` with `SetCurrentSession` + `CurrentSession`
   - Extend `IAnalyticsEventStateProvider` with `CurrentSession`
   - Extend `AnalyticsEventStateProvider` with the projection
8. **Handler integration**
   - Extend `PageviewCapturedHandler.HandleAsync` to call the resolver before building the receipt; carry `SessionKey` on the receipt via the `with`-expression; extend `TryUpdateInFlightStateStore(receipt, session)`.
9. **Repository update**
   - Extend `AnalyzerEventReceiptRepository.InsertAsync` to map `receipt.SessionKey` to the DTO column.
10. **Composer wiring**
    - Extend `AnalyzerComposer.Compose` to register all new services + the second cascade step.
11. **Tests** (each phase tests the just-built layer)
    - Unit tests as you go.
    - Integration tests after composer is wired.
    - Pinning baseline regen + byte-match assertion.
    - Perf-smoke test (opt-in trait).

---

## Verifying you're done

| Check | How |
|---|---|
| All FRs covered | Cross-reference `spec.md` FR-001..FR-013 against `tasks.md` — every FR has at least one task. |
| All SCs measurable | SC-001/002 via integration tests, SC-003 via perf-smoke, SC-004 via cascade integration test, SC-005 via sweeper integration test, SC-006 via concurrent-dispatch integration test, SC-007 via pinning test + baseline diff, SC-008 by inspection (does every US AS have a test?). |
| Constitution gates green | `plan.md` Constitution Check section — all 10 PASS, no Complexity Tracking entries. Re-evaluation after Phase 1 also PASS. |
| Spec Assumptions documented | The two load-bearing Assumptions (deviceKey hash, soft-anonymise semantic) are called out in `spec.md` Assumptions + grounded in `research.md` §3 + §5. |
| Pinning baseline regen + diff reviewed | `Analyzer-public-surface.txt` shows: new `AnalyticsSession` type block; new `CurrentSession` member on `IAnalyticsEventStateProvider`; new `SessionKey` init-only property on `AnalyticsEventReceipt`. Slice-002 positional ctor of `AnalyticsEventReceipt` unchanged. |
| Local dev verified | Open `https://localhost:44364/umbraco`, render several pages as the dev user across two browsers (Chrome + Edge or similar), query `analyzerSession` — exactly one row per (visitor, deviceKey) burst, with `pageviewCount = N`, `isActive = 1`. Query `analyzerEventReceipt` — every row has `sessionKey` populated and FK-pointing at a row in `analyzerSession`. |
| Anonymisation flow verified | Invoke Customizer's anonymise-visitor command for visitor A. Re-query `analyzerSession WHERE visitorProfileKey = A` — rows have `anonymizedUtc` non-null, `deviceKey = ''`, `pageviewCount` + `startUtc` + `endUtc` preserved. Receipts for A are 0 (slice-002 hard-delete). Session-level aggregates remain queryable. |
| Sweeper verified | Set `Analyzer:Session:InactivityTimeoutMinutes = 1`. Render one page. Wait ~70 seconds. Query — the session row has `isActive = 0` and `endUtc = startUtc + 1 minute` (NOT the sweeper's run time, which would be ~30–60s later). |

---

## When something goes wrong

| Symptom | Likely cause | Fix |
|---|---|---|
| `M0002` runs twice on a fresh re-deploy and throws on duplicate table | `TableExists` guard missing or wrong constant name. | Confirm `TableExists(Constants.Database.AnalyzerSession)` is the guard. See data-model §3. |
| `M0002` fails with "Cannot find object 'analyzerSession' on partial unique index" | Migration body ordered wrong — raw-SQL FK / partial index issued before `Create.Table<AnalyzerSessionDto>().Do()`. | Reorder: create table first, then declare FK + partial index. data-model §3. |
| Integration test asserts `pageviewCount = 1` but the test sees 2 | Concurrent dispatchers ran twice during the same test fixture; the resolver's extend path incremented twice. | Either (a) seed the visitor once and don't re-run the fixture mid-test, or (b) assert with a tolerance — the spec doesn't require strict `count = 1` under concurrent dispatch, only "exactly one **row**." |
| Sweeper closes a session but the cache still returns it on the next pageview | Cache invalidation missed the returned `sessionKey`. | Verify `SweepEligibleAsync` returns the affected `sessionKey`s; the sweeper iterates them and calls `InvalidateBySessionKey`. |
| Test of cascade rollback observes the session UPDATE persisted | Cache invalidation ran BEFORE the outer scope completed; the cache state diverges from the rolled-back DB state. | Cascade step's cache invalidation MUST run AFTER the repository call returns; the outer scope's commit/rollback is observable only after the orchestrator's `Complete()` or scope dispose. Re-check ordering in `AnalyzerSessionCascadeStep.ExecuteAsync`. |
| Pinning baseline diff has unexpected lines (not in spec Sync Impact note) | Some internal type accidentally leaked into a pinned namespace, or an existing public type changed shape. | Read the diff line by line. If unintended, revert the leaking change. If intended, extend the spec's Assumptions section with the new line and regenerate. Don't `git checkout` the baseline without reverting the underlying change. |
| `dotnet run` for tests fails with "test host not found" | Used `dotnet test` instead of `dotnet run --project ... -- <args>`. | xUnit v3 MTP requires `dotnet run`. Lesson #33. |
| Perf-smoke flakes intermittently | Real load on a shared machine is non-deterministic; first-pass cold-cache penalty dominates. | Run twice; report the second run. Don't tune p95 to the first-pass measurement. |
| `MemoryCache.SizeLimit` not respected | Forgot to set `Size = 1` on the cache entry's `MemoryCacheEntryOptions`. | Every `MemoryCache.Set` call MUST set `Size = 1`; otherwise eviction is disabled. |

---

## Cross-product hygiene

- **Do not modify any file under `../customizer/`** while implementing this slice. Principle III is strict. Customizer's `PageviewCaptured` and `IAnonymizationCascadeStep` are both prerequisites that have already shipped.
- **Do not import Customizer internals.** Pinned surface: `IPersonalizationProfile`, `IAnalyticsStateProvider` (Customizer's), `PageviewCaptured`, `Pageview`, `IAnonymizationCascadeStep`, `IScopeProvider`. Anything else is internal; use raw SQL in the migration body for any cross-table FK declaration (lessons #38, #39).
- **Update `.remember/remember.md`** at end-of-session with anything you learned. The handoff doc is how the next session gets the load-bearing context.

---

## When in doubt

Read slice 002. Every pattern this slice uses (composer wiring, scoped DI, bounded queue, hosted services, NPoco DTO + raw-SQL FK, cascade-step inside outer scope, pinning baseline) is already there in slice-002 production-validated form. Slice 003 extends rather than diverges. Diverge only when you have a written justification in `research.md`.
