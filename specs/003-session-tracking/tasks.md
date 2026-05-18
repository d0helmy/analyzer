---

description: "Task list for slice 003 — sessions"
---

# Tasks: Sessions

**Input**: Design documents from `/specs/003-session-tracking/`

**Prerequisites**: plan.md (loaded), spec.md (loaded), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Plan.md Testing section explicitly requires unit + integration + pinning + perf-smoke coverage; test tasks ARE included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. Phases run in priority order (P1 = MVP → P2 → P3 → Polish).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to ([US1], [US2], [US3]); Setup / Foundational / Polish phases carry no story label
- Include exact file paths in descriptions

## Path Conventions

Razor Class Library at `src/Analyzer/`. Test project at `src/Analyzer.Tests/`. Paths are relative to repo root unless absolute. Mirrors slice-001 + slice-002 layout.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prerequisites are in place from slices 001 + 002; this phase confirms the development environment.

- [ ] T001 Confirm slice-002 + slice-001 commits are reachable from this branch's base (`git log --oneline main..HEAD` lists only this slice's commits; `cc78f80` and `ab4285c` are on `main`).
- [ ] T002 Verify Aspire AppHost + SQL container reachable (`docker info` succeeds; `dotnet run --project aspire/Analyzer.AppHost --launch-profile https` boots; `https://localhost:44364/umbraco` returns HTTP 200; install creds `dev@analyzer.local / 1234567890aA!`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Constants, configuration, public records, persistence DTO and migration. Every user story depends on these landing first.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [ ] T003 Add `Database.AnalyzerSession = "analyzerSession"` constant in `src/Analyzer/Constants.cs` (next to existing `AnalyzerEventReceipt`).
- [ ] T004 [P] Create `AnalyzerSessionOptions` (`InactivityTimeoutMinutes=30, SweepIntervalSeconds=60, SweepBatchSize=1000, CacheCapacity=10000`) in `src/Analyzer/Features/Sessions/Infrastructure/Configuration/AnalyzerSessionOptions.cs` per data-model §7.
- [ ] T005 [P] Create public `AnalyticsSession` record (7 positional properties: `SessionKey, VisitorProfileKey, StartUtc, LastActivityUtc, EndUtc?, PageviewCount, IsActive`) with XML docs per data-model §5 in `src/Analyzer/Analytics/AnalyticsSession.cs`.
- [ ] T006 [P] Extend `src/Analyzer/Analytics/AnalyticsEventReceipt.cs` — append init-only property `public Guid? SessionKey { get; init; }` inside the record body (NOT a positional parameter; preserves slice-002 binary compatibility per research §11). XML doc comment per data-model §4.
- [ ] T007 [P] Create `AnalyzerSessionDto` NPoco DTO in `src/Analyzer/Features/Sessions/Infrastructure/Persistence/AnalyzerSessionDto.cs` per data-model §1 (10 columns with `[Column]` / `[Index]` / `[Length(64)]` / `[NullSetting]` attributes; partial unique index NOT declared via attribute — emitted by raw SQL in M0002).
- [ ] T008 Extend `src/Analyzer/Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptDto.cs` with nullable column `[Column("sessionKey")] [NullSetting(NullSetting=NullSettings.Null)] [Index(IndexTypes.NonClustered, Name="IDX_analyzerEventReceipt_sessionKey")] public Guid? SessionKey { get; set; }` per data-model §2.
- [ ] T009 Create migration `src/Analyzer/Migrations/M0002_AddAnalyzerSessionTableAndReceiptSessionKey.cs` (AsyncMigrationBase): `Create.Table<AnalyzerSessionDto>().Do()` guarded by `TableExists`; raw-SQL FK + partial unique index + sweep index on SQL Server only (SQLite skip via `Database.DatabaseType.GetProviderName()` check per lesson #39); `Alter.Table(...).AddColumn("sessionKey").AsGuid().Nullable().Do()` + `Create.Index("IDX_analyzerEventReceipt_sessionKey")` guarded by `ColumnExists`. Full body per data-model §3.
- [ ] T010 Chain `M0002` after `M0001` in `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs` — add `.To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey")`.
- [ ] T011 [P] Extract `IsUniqueConstraintViolation` from `src/Analyzer/Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptRepository.cs` into a shared internal helper `src/Analyzer/Features/Common/Persistence/UniqueConstraintViolationDetector.cs`; update the slice-002 receipt repository to consume it. (Research §4 deferred item.)

**Checkpoint**: Schema, records, options ready. User-story implementation can begin.

---

## Phase 3: User Story 1 — Sessions resolve & attach (Priority: P1) 🎯 MVP

**Goal**: For every authenticated `PageviewCaptured` notification, the session resolver decides whether the pageview extends an existing in-progress session for the visitor+device or opens a new one. The receipt write op carries the resolved `sessionKey`. `IAnalyticsEventStateProvider.CurrentSession` exposes the in-flight session to in-process consumers.

**Independent Test**: An authenticated EntraID employee navigates to three pages on the same browser within 5 minutes, then waits past the configured inactivity timeout, then navigates to a fourth page. Read `analyzerSession`: exactly two rows for that visitor+device combination (first with `pageviewCount=3, endUtc=last-burst+timeout`; second with `pageviewCount=1, isActive=true`). Read `analyzerEventReceipt`: all four receipts carry a non-null `sessionKey`, three pointing at the first session and one at the second.

### Tests for User Story 1 (write FIRST; fail before implementation)

- [ ] T012 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Application/DeviceKeyHasherTests.cs` — same UA → same 16-hex hash; different UAs → different hashes; null/whitespace UA → deterministic sentinel hash; non-ASCII UA tolerated.
- [ ] T013 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionCacheStoreTests.cs` — TryGet hit/miss; LRU eviction once `SizeLimit` exceeded; Invalidate removes entry; InvalidateBySessionKey walks keys; InvalidateByVisitorKey walks keys; concurrent reads/writes safe.
- [ ] T014 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Infrastructure/Persistence/AnalyzerSessionRepositoryTests.cs` (against an in-memory fake / TestcontainersMsSql fixture) — GetLatestActive returns most-recent active row; Insert throws DbException on partial-unique-index collision; Extend advances `lastActivityUtc` + increments `pageviewCount`; Close idempotent on already-closed row.
- [ ] T015 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.cs` — six scenarios: cache-hit-extend, cache-miss-DB-hit-extend, stale-cache-close-then-new-open, stale-DB-row-close-then-new-open, race-collision-retry-via-IsUniqueConstraintViolation, OptionsMonitor reload changes `InactivityTimeoutMinutes` mid-test, null UA → sentinel deviceKey path.
- [ ] T016 [P] [US1] Integration test `src/Analyzer.Tests/Integration/Sessions/ResolveAndAttachTests.cs` method `EndToEndOpensAndExtends` against real SQL Server (US1 AS1, AS2) — publish 3 `PageviewCaptured` events for one visitor with the same UA within timeout; assert 1 active session row with `pageviewCount=3`; receipts in DB carry the correct `sessionKey`. `[Trait("Category","Integration")]`.
- [ ] T017 [P] [US1] Integration test `…ResolveAndAttachTests.StaleSessionClosedThenNewOpened` (US1 AS3) — publish 1 pageview; use `FakeTimeProvider` to advance past timeout; publish a second pageview; assert 2 rows for the visitor+device (one closed with `endUtc = first.lastActivityUtc + timeout`, one active).
- [ ] T018 [P] [US1] Integration test `…ResolveAndAttachTests.ReceiptCarriesSessionKey` (US1 AS1, AS6) — publish a pageview; flush the dispatcher; query `analyzerEventReceipt` and assert the row's `sessionKey` matches the session's `sessionKey`.
- [ ] T019 [P] [US1] Integration test `src/Analyzer.Tests/Integration/Sessions/ConcurrentDispatchRaceSafetyTests.cs` (US1 AS4) — publish 10 `PageviewCaptured` events for the same `(visitor, UA)` concurrently across thread-pool threads; assert exactly 1 active session row; all 10 receipts attach to it.
- [ ] T020 [P] [US1] Integration test `src/Analyzer.Tests/Integration/Sessions/MigrationIdempotencyTests.cs` (US1 AS6) — pre-seed an `analyzerEventReceipt` row from slice-002 era (sessionKey column absent on the row); run `M0002`; re-run `M0002`; assert no error; pre-existing receipt still has `sessionKey = null`; new schema objects exist (table + FK + partial unique index + sweep index + receipt column + receipt index).

### Implementation for User Story 1

- [ ] T021 [P] [US1] Implement `DeviceKeyHasher` static helper in `src/Analyzer/Features/Sessions/Application/DeviceKeyHasher.cs` per research §5 (SHA-256 over UTF-8(trim(UA ?? "")) truncated to first 8 bytes, hex-encoded lowercase).
- [ ] T022 [P] [US1] Create `IAnalyzerSessionRepository` interface in `src/Analyzer/Features/Sessions/Infrastructure/Persistence/IAnalyzerSessionRepository.cs` with `GetLatestActiveAsync`, `InsertAsync`, `ExtendAsync`, `CloseAsync` (US2's `SoftAnonymizeByVisitorKeyAsync` + US3's `SweepEligibleAsync` are added in their respective phases).
- [ ] T023 [US1] Implement `AnalyzerSessionRepository` in `src/Analyzer/Features/Sessions/Infrastructure/Persistence/AnalyzerSessionRepository.cs` — NPoco DTO ↔ AnalyticsSession projection mapping; nested `IScopeProvider.CreateScope()` per call; raw-SQL SELECT for `GetLatestActiveAsync` (`SELECT TOP 1 * FROM analyzerSession WHERE visitorProfileKey=@0 AND deviceKey=@1 AND isActive=1 ORDER BY lastActivityUtc DESC`); INSERT via `scope.Database.InsertAsync(dto)`; raw-SQL UPDATE for Extend + Close. Reuses `UniqueConstraintViolationDetector` (T011) when bubbling the partial-unique-index throw to the resolver.
- [ ] T024 [P] [US1] Create `AnalyticsSessionCacheEntry` internal record in `src/Analyzer/Features/Sessions/Application/AnalyticsSessionCacheEntry.cs` per data-model §6.
- [ ] T025 [US1] Implement `AnalyzerSessionCacheStore` in `src/Analyzer/Features/Sessions/Application/AnalyzerSessionCacheStore.cs` — singleton; wraps `MemoryCache` with `SizeLimit = options.CurrentValue.CacheCapacity`; key shape `$"{visitorProfileKey:N}|{deviceKey}"`; sliding expiration `inactivityTimeout * 2`; `TryGet`, `UpdateActivity`, `Invalidate`, `InvalidateBySessionKey` (O(N) key walk), `InvalidateByVisitorKey` (O(N) key walk). Implements `IDisposable`.
- [ ] T026 [P] [US1] Create `IAnalyzerSessionResolver` interface + `SessionResolutionResult` readonly record struct in `src/Analyzer/Features/Sessions/Application/IAnalyzerSessionResolver.cs` per contract.
- [ ] T027 [US1] Implement `AnalyzerSessionResolver` in `src/Analyzer/Features/Sessions/Application/AnalyzerSessionResolver.cs` — scoped; orchestrates `DeviceKeyHasher` + `AnalyzerSessionCacheStore` + `IAnalyzerSessionRepository`; implements the seven-step resolution flow from contract `AnalyzerSessionResolver.md`; catches `DbException` via `UniqueConstraintViolationDetector` and retries-via-re-read on collision; ≤ 3 indexed SQL statements per call.
- [ ] T028 [US1] Extend `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` — add `private AnalyticsSession? _currentSession;` field + `public AnalyticsSession? CurrentSession => _currentSession;` accessor + `public void SetCurrentSession(AnalyticsSession session) { ArgumentNullException.ThrowIfNull(session); _currentSession = session; }`.
- [ ] T029 [US1] Extend public interface `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` with the new member `AnalyticsSession? CurrentSession { get; }` per `contracts/IAnalyticsEventStateProvider.md`. Preserve slice-002's `CurrentRequestReceipt` member byte-for-byte; update the XML `<list>` of "slice 003 — …" to reflect the landing.
- [ ] T030 [US1] Extend `src/Analyzer/Analytics/AnalyticsEventStateProvider.cs` with `public AnalyticsSession? CurrentSession => _store.CurrentSession;` projection.
- [ ] T031 [US1] Extend `src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs` — inject `IAnalyzerSessionResolver`; in `HandleAsync`, after the Guid.Empty guards but before constructing the receipt, call `var resolution = await _resolver.ResolveAsync(pv.VisitorProfileKey, _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(), receivedUtc, cancellationToken)`; build the receipt with `with { SessionKey = resolution.SessionKey }`; rename and extend `TryUpdateInFlightStateStore(receipt, session)` to call both `store.SetCurrentReceipt(receipt)` and `store.SetCurrentSession(session)`. If resolver throws, the existing outer catch swallows-and-logs at warning level; the receipt enqueue is skipped.
- [ ] T032 [US1] Update `src/Analyzer/Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptRepository.cs` `InsertAsync` — set `dto.SessionKey = receipt.SessionKey` before the `scope.Database.InsertAsync(dto)` call. No other change.
- [ ] T033 [US1] Wire US1 services in `src/Analyzer/Composers/AnalyzerComposer.cs` — inside `ConfigureServices`: `services.Configure<AnalyzerSessionOptions>(builder.Config.GetSection("Analyzer:Session"))`; `services.AddSingleton<AnalyzerSessionCacheStore>()`; `services.AddScoped<IAnalyzerSessionRepository, AnalyzerSessionRepository>()`; `services.AddScoped<IAnalyzerSessionResolver, AnalyzerSessionResolver>()`. (Cascade step + sweeper land in US2 + US3.)

**Checkpoint**: User Story 1 fully functional and testable independently. Pageviews flow handler → resolver → session table + receipt with FK; `IAnalyticsEventStateProvider.CurrentSession` lights up in-process; concurrent dispatchers race-safe via partial unique index.

---

## Phase 4: User Story 2 — Operator-triggered visitor anonymisation soft-anonymises sessions (Priority: P2)

**Goal**: When an authorised backoffice user invokes Customizer's anonymise-visitor action, Analyzer's session cascade step soft-anonymises the visitor's session rows — `anonymizedUtc = now`, `deviceKey = ''`, aggregates preserved.

**Independent Test**: Seed two visitor profiles A and B; drive a small set of pageviews for each (sessions with `pageviewCount > 1`); invoke Customizer's `AnonymizeVisitorProfileCommand` for visitor A. Re-read `analyzerSession`: A's rows have `anonymizedUtc` populated, `deviceKey` blank, `pageviewCount/startUtc/endUtc` preserved; B's rows unchanged. Receipts for A are gone (slice-002 hard-delete cascade). A's sessions still queryable in aggregate.

### Tests for User Story 2

- [ ] T034 [P] [US2] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Application/Anonymization/AnalyzerSessionCascadeStepTests.cs` — soft-anonymises visitor A only (mocked repo); sets `anonymizedUtc`, blanks `deviceKey`, preserves aggregates; idempotent re-run returns empty `affectedSessionKeys`; zero-row no-op; cache invalidation runs AFTER repository success (asserted via call-order verification).
- [ ] T035 [P] [US2] Integration test `src/Analyzer.Tests/Integration/Sessions/CascadeSoftAnonymiseTests.cs` method `SoftAnonymiseInsideOuterScope` (US2 AS1) — seed visitors A + B with sessions + receipts; invoke `AnonymizeVisitorProfileCommand` for A; assert A's session rows: `anonymizedUtc != null, deviceKey = '', pageviewCount + startUtc + endUtc preserved`; A's receipts: 0 rows (slice-002 hard-delete); B's rows untouched. `[Trait("Category","Integration")]`.
- [ ] T036 [P] [US2] Integration test `src/Analyzer.Tests/Integration/Sessions/CascadeRollbackTests.cs` method `CascadeThrowRollsBackAllSteps` (US2 AS2) — inject a third `IAnonymizationCascadeStep` that throws after Analyzer's session step runs; assert A's session rows revert to pre-anonymisation state; A's receipts also re-appear (slice-002 cascade rolled back); Customizer's visitor row's `IdentityRef` also reverted.

### Implementation for User Story 2

- [ ] T037 [US2] Add `Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)` to `IAnalyzerSessionRepository`; implement in `AnalyzerSessionRepository` — issues single indexed UPDATE `WHERE visitorProfileKey = @0 AND anonymizedUtc IS NULL`; returns the `sessionKey`s of UPDATEd rows (via `OUTPUT INSERTED.sessionKey` on SQL Server; SELECT-then-UPDATE on SQLite fallback).
- [ ] T038 [US2] Implement `AnalyzerSessionCascadeStep` in `src/Analyzer/Features/Sessions/Application/Anonymization/AnalyzerSessionCascadeStep.cs` — internal sealed; implements `Customizer.Features.Visitors.Application.Contracts.Anonymization.IAnonymizationCascadeStep`; ctor injects `IAnalyzerSessionRepository`, `AnalyzerSessionCacheStore`, `ILogger`; `ExecuteAsync` (a) short-circuits on Guid.Empty visitor, (b) calls `repository.SoftAnonymizeByVisitorKeyAsync`, (c) iterates returned keys and calls `cacheStore.InvalidateBySessionKey(key)`, (d) logs at Information level. Full body per contract.
- [ ] T039 [US2] Register `services.AddScoped<IAnonymizationCascadeStep, AnalyzerSessionCascadeStep>()` in `src/Analyzer/Composers/AnalyzerComposer.cs` alongside slice-002's `AnalyzerEventReceiptCascadeStep` registration.

**Checkpoint**: User Stories 1 AND 2 both work independently. Anonymisation cascades soft-anonymise sessions atomically with slice-002's receipt hard-delete + Customizer's visitor row update.

---

## Phase 5: User Story 3 — Sweeper closes inactive sessions automatically (Priority: P3)

**Goal**: A background hosted service scans `analyzerSession` on a configurable cadence and closes rows whose `isActive = true AND lastActivityUtc + inactivityTimeout < now` with `endUtc = lastActivityUtc + inactivityTimeout` (logical, NOT wall-clock).

**Independent Test**: Set `Analyzer:Session:InactivityTimeoutMinutes = 1` and `SweepIntervalSeconds = 5`. Capture one pageview for a visitor. Wait `60 + 5 + buffer` seconds. Read `analyzerSession`: the row has `isActive = false` and `endUtc = startUtc + 1 minute` (NOT the sweeper's run time, which would be ~30–60s later).

### Tests for User Story 3

- [ ] T040 [P] [US3] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Sweeper/AnalyzerSessionSweeperServiceTests.cs` — five scenarios: closes eligible (mocked repo returns N keys → N invalidations + Debug log); leaves active (0 keys → 0 invalidations); idempotent on already-closed (predicate excludes); swallows-tick-exception-and-continues (first tick throws, second tick succeeds, loop alive); `IOptionsMonitor` reload of `SweepIntervalSeconds` changes next `Task.Delay` interval.
- [ ] T041 [P] [US3] Integration test `src/Analyzer.Tests/Integration/Sessions/SweeperBackgroundServiceTests.cs` method `LogicalCloseTimeNotWallClock` (US3 AS1, SC-005) — set `InactivityTimeoutMinutes = 1, SweepIntervalSeconds = 5`; open a session; wait for the sweeper to close it; assert `endUtc - startUtc = 1 minute` (within ±200 ms tolerance for SQL clock skew); NOT `endUtc ≈ now + 1 minute`. `[Trait("Category","Integration")]`.
- [ ] T042 [P] [US3] Integration test `…SweeperBackgroundServiceTests.ClosesAllEligibleWithinTwoIntervals` (SC-005) — seed 10 eligible sessions; assert all 10 closed within `2 × SweepIntervalSeconds`.
- [ ] T043 [P] [US3] Integration test `…SweeperBackgroundServiceTests.LeavesActiveSessionsAlone` (US3 AS2) — seed a session that's still within the inactivity window; run one sweeper tick; assert the row is unchanged.
- [ ] T044 [P] [US3] Integration test `…SweeperBackgroundServiceTests.IdempotentOnAlreadyClosed` (US3 AS3) — pre-close a session via the resolver's lazy-close path; run sweeper; assert the row is untouched.

### Implementation for User Story 3

- [ ] T045 [US3] Add `Task<IReadOnlyList<Guid>> SweepEligibleAsync(DateTimeOffset cutoff, TimeSpan inactivityTimeout, int batchSize, CancellationToken ct)` to `IAnalyzerSessionRepository`; implement in `AnalyzerSessionRepository` — issues `UPDATE TOP (@batchSize) … SET isActive=0, endUtc=DATEADD(SECOND, @inactivitySeconds, lastActivityUtc) OUTPUT INSERTED.sessionKey WHERE isActive=1 AND lastActivityUtc < @cutoff` on SQL Server; SQLite fallback uses SELECT-then-UPDATE bounded by `LIMIT @batchSize`.
- [ ] T046 [US3] Implement `AnalyzerSessionSweeperService : BackgroundService` in `src/Analyzer/Features/Sessions/Infrastructure/Sweeper/AnalyzerSessionSweeperService.cs` — ctor: `IServiceScopeFactory, AnalyzerSessionCacheStore, IOptionsMonitor<AnalyzerSessionOptions>, TimeProvider, ILogger`; `ExecuteAsync` loop body per contract `AnalyzerSessionSweeperService.md` (per-tick scope; bounded batch; logical close time; cache invalidation after success; swallow per-tick errors; `Task.Delay(SweepIntervalSeconds)` between ticks; clean shutdown on `stoppingToken`).
- [ ] T047 [US3] Register `services.AddHostedService<AnalyzerSessionSweeperService>()` in `src/Analyzer/Composers/AnalyzerComposer.cs` alongside slice-002's `AnalyzerEventReceiptWriteDispatcher`.

**Checkpoint**: All three user stories independently functional. Inactive sessions converge to closed state without operator intervention.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: pinning baseline, perf smoke, regression on slice-002 tests, local-dev sample config, quickstart verification.

- [ ] T048 [P] Implement `src/Analyzer.Tests/Perf/SessionThroughputSmokeTests.cs` per plan §Performance Goals + research §1 — 1000 pv/s × 60 s synthetic load through the live `IEventAggregator` with the resolver in the path; assert (1) p95 request-thread latency delta vs slice-002-only baseline ≤ 3 ms (SC-003), (2) no resolver write blocks the request thread > 10 ms, (3) zero duplicate active sessions emerged. `[Trait("Category","Perf")]`.
- [ ] T049 Regenerate the pinning baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` via `ANALYZER_REGENERATE_SNAPSHOTS=1 dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "FullyQualifiedName~PublicSurfacePinningTests"`; verify the diff matches research §10 (3 additive lines: new `AnalyticsSession` type block; `CurrentSession` member on `IAnalyticsEventStateProvider`; `SessionKey` init-only property on `AnalyticsEventReceipt`); rerun WITHOUT the env var and confirm byte-match passes; add a Sync Impact-style note to `specs/003-session-tracking/spec.md` Assumptions §pinning regen documenting the additive change as MINOR per Principle X.
- [ ] T050 [P] Update slice-002 integration test `src/Analyzer.Tests/Integration/PageviewSubscription/EndToEndCaptureTests.cs` to also assert the persisted receipt's `sessionKey` is non-null and matches the active session row (regression coverage; ensures slice-003 didn't accidentally regress slice-002 behaviour).
- [ ] T051 [P] Add `Analyzer:Session` block to `samples/Analyzer.Host/appsettings.json` for local-dev verification (`InactivityTimeoutMinutes:30, SweepIntervalSeconds:60, SweepBatchSize:1000, CacheCapacity:10000`) — confirm the local install GUID still does NOT get committed (lesson #18).
- [ ] T052 Run the full quickstart.md verification steps end-to-end: render pages across two browsers and confirm two device-keyed sessions; invoke anonymise command and confirm soft-anonymise state; set short inactivity (`InactivityTimeoutMinutes=1, SweepIntervalSeconds=5`) and confirm sweeper produces logical-close-time `endUtc`.
- [ ] T053 Run full unit + integration test suite locally (`dotnet build Analyzer.slnx` clean; `dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --configuration Release -- -trait- "Category=Perf"`); document any flakes; confirm CI invocation pattern (`-trait- "Category=Integration" -trait- "Category=Perf"`) still passes.
- [ ] T054 Build clean: `dotnet build Analyzer.slnx` → 0 errors; NU190x warnings expected (slice-002 baseline; upstream Customizer transitive deps; board issue #10). No new warnings introduced.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phases 3–5)**: All depend on Foundational completion.
  - US1 (P1, MVP) → US2 (P2) → US3 (P3) is the priority order; teams may parallelise after Foundational lands.
  - US2 has a soft dependency on US1's resolver only for the integration tests' seed step (the cascade step itself depends only on the repository's session rows, which are populated by US1's flow).
  - US3 has a soft dependency on US1's resolver for the same reason.
- **Polish (Phase 6)**: Depends on all user stories being complete (pinning baseline must capture the full slice-003 public surface; perf-smoke needs the resolver in the path).

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational. The MVP.
- **US2 (P2)**: Depends on Foundational + US1 (cascade step soft-anonymises rows that US1 writes). Independently testable once US1's session rows exist.
- **US3 (P3)**: Depends on Foundational + US1 (sweeper closes rows that US1 writes). Independently testable once US1's session rows exist.

### Within Each User Story

- Tests written first (xUnit v3 conventions; assertions FAIL before impl).
- Hasher + record + interface land before the resolver / cascade / sweeper that orchestrates them.
- Repository before resolver / cascade / sweeper.
- Composer wiring last in each story phase (avoids "registered service has no impl yet" composition error).

### Parallel Opportunities

- **Foundational [P] tasks**: T004 + T005 + T006 + T007 + T011 all independent (different files; T003 must land first as it provides the constant).
- **US1 [P] tests**: T012–T020 all independent (different test files; can be authored in parallel by multiple developers / agents).
- **US1 [P] impls**: T021 + T022 + T024 + T026 all independent (different files; no cross-dependencies).
- **US2 [P] tests**: T034 + T035 + T036 all independent.
- **US3 [P] tests**: T040 + T041 + T042 + T043 + T044 all independent.
- **Polish [P] tasks**: T048 + T050 + T051 all independent of each other (T049 sequences after all impls are done; T052/T053/T054 are sequential validations).

---

## Parallel Example: User Story 1 implementation (after tests author)

```bash
# Foundational already complete. Tests authored (T012–T020). Now in parallel:
Task: "Implement DeviceKeyHasher (T021)"                              # src/Analyzer/Features/Sessions/Application/DeviceKeyHasher.cs
Task: "Create IAnalyzerSessionRepository interface (T022)"            # src/Analyzer/Features/Sessions/Infrastructure/Persistence/IAnalyzerSessionRepository.cs
Task: "Create AnalyticsSessionCacheEntry record (T024)"               # src/Analyzer/Features/Sessions/Application/AnalyticsSessionCacheEntry.cs
Task: "Create IAnalyzerSessionResolver + SessionResolutionResult (T026)" # src/Analyzer/Features/Sessions/Application/IAnalyzerSessionResolver.cs

# Then sequentially (each blocks on what came before):
Task: "Implement AnalyzerSessionRepository (T023)"                    # depends on T022 + T011
Task: "Implement AnalyzerSessionCacheStore (T025)"                    # depends on T024 + T004
Task: "Implement AnalyzerSessionResolver (T027)"                      # depends on T021 + T022 + T023 + T024 + T025 + T026
Task: "Extend AnalyticsEventStateStore (T028)"                        # depends on T005
Task: "Extend IAnalyticsEventStateProvider (T029)"                    # depends on T005
Task: "Extend AnalyticsEventStateProvider impl (T030)"                # depends on T028 + T029
Task: "Extend PageviewCapturedHandler (T031)"                         # depends on T027 + T030 + T006
Task: "Update AnalyzerEventReceiptRepository.InsertAsync (T032)"      # depends on T008
Task: "Wire US1 services in AnalyzerComposer (T033)"                  # depends on all US1 impls
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (verify environment).
2. Complete Phase 2: Foundational (constants, options, records, DTO, migration, plan chain, shared violation detector).
3. Complete Phase 3: User Story 1 (tests fail → impls → tests pass).
4. **STOP and VALIDATE**: end-to-end pageview → resolver → session row + receipt FK; concurrent dispatch race-safety; in-process state provider.
5. The MVP demonstrates sessions are created and continued automatically; ready to demo or proceed.

### Incremental Delivery

1. Complete Setup + Foundational → ship M0002 in isolation if desired (additive; safe).
2. Add User Story 1 → MVP demo: sessions persist; receipts FK to sessions.
3. Add User Story 2 → demo: operator anonymisation soft-anonymises sessions.
4. Add User Story 3 → demo: sweeper closes inactive sessions automatically.
5. Polish → pinning baseline + perf-smoke + quickstart verification → PR.

### Single-Agent Strategy (this slice's expected mode)

With one Claude Code agent + the user:

1. Land Foundational in one logical commit (Phase 2; ~10 tasks).
2. Land US1 MVP in one logical commit (Phase 3; ~22 tasks).
3. Land US2 + US3 + Polish in one logical commit (Phases 4–6; ~21 tasks).
4. Open PR with the three commits + the prior spec/plan/checklist commits already on the branch (`c55eda4`, `f6a3272`).

This follows the slice-002 ship pattern (4 commits on `main` post-rebase): spec → plan → MVP → US2+US3+polish.

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story for traceability.
- Each user story should be independently completable and testable.
- Verify tests fail before implementing (TDD discipline; slice-002 precedent).
- Commit at phase boundaries — `Foundational landed`, `US1 landed (MVP)`, `US2 + US3 + polish landed`.
- Stop at any checkpoint to validate the story independently.
- Avoid: vague tasks, same-file conflicts in `[P]` tasks, cross-story dependencies that break independence.
