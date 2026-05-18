# Implementation Plan: Sessions

**Branch**: `003-session-tracking` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-session-tracking/spec.md`

## Summary

Slice 003 turns the slice-002 receipt subscription into a session-aware capture pipeline. Five concrete things, ordered by spec user-story priority:

1. **Resolve sessions on every pageview** (US1, P1): inside `PageviewCapturedHandler` (slice 002), call a new `IAnalyzerSessionResolver` BEFORE enqueuing the receipt write op. The resolver reads-or-opens the active session for the `(visitorProfileKey, deviceKey)` pair against the new `analyzerSession` table, returns its `sessionKey`, and carries that key on the existing `AnalyzerEventReceiptWriteOp` payload so the receipt row that lands in the dispatcher batch has the FK populated. Session-side writes are synchronous to the handler thread (NOT the bounded queue) because the receipt's FK depends on the session row being durable at enqueue time.
2. **Persist sessions** (US1, P1): new `analyzerSession` table; new `AnalyzerSessionDto` + `IAnalyzerSessionRepository` + `AnalyzerSessionRepository` (read latest active by key, insert, extend, close); migration `M0002_AddAnalyzerSessionTableAndReceiptSessionKey` creates the table AND adds a nullable `sessionKey` column to `analyzerEventReceipt` with a non-clustered index. Pre-slice-003 receipts keep `sessionKey = null` (no back-fill).
3. **Cache active sessions** (US1, P1, FR-010): an in-memory bounded LRU (`Microsoft.Extensions.Caching.Memory.MemoryCache`-backed wrapper) keys `(visitorProfileKey, deviceKey) → AnalyticsSessionCacheEntry { SessionKey, LastActivityUtc, ExpiresUtc }`. Configurable capacity per `Analyzer:Session:CacheCapacity`. Cache invalidation on close + on cascade-anonymisation. Race-safety via the DB-level partial unique index — cache miss → DB read → if-still-no-row → INSERT → catch unique-violation → re-read.
4. **Soft-anonymise cascade step** (US2, P2): new `AnalyzerSessionCascadeStep : IAnonymizationCascadeStep`. Sets `anonymizedUtc = now`, blanks `deviceKey`, leaves the rest intact. Distinct from slice-002's hard-delete on receipts because session-level aggregates (visit counts per content node, average pages per session) are load-bearing for slice 005 + slice 010. Principle IV v1.1.1 authorises this per-table choice.
5. **Sweeper** (US3, P3): new `AnalyzerSessionSweeperService : BackgroundService`. On a configurable cadence, scans `isActive = true AND lastActivityUtc + inactivityTimeout < now`, paginates in `SweepBatchSize`-sized batches, closes each row with `endUtc = lastActivityUtc + inactivityTimeout` (logical, NOT wall-clock — protects session-duration metrics).

Public-surface additions (additive only):

- `Analyzer.Analytics.AnalyticsSession` (new public record).
- `IAnalyticsEventStateProvider.CurrentSession` (new member; slice 002's `CurrentRequestReceipt` member unchanged).

Both are picked up by the slice-002 `PublicSurfacePinningTests`; the baseline regenerates with a Sync Impact-style note.

The migration runs under Umbraco's standard migration pipeline, appended to the existing `AnalyzerMigrationPlan` after `M0001`. Integration tests reuse slice-002's `AnalyzerIntegrationTestBase` against a real SQL Server container (Aspire-persistent locally; Testcontainers in CI), tagged `Category=Integration` so CI continues to opt them out (lessons #31 + #32).

**Cross-product prerequisite**: a paired Customizer-side change adds `string? UserAgent` as the 10th positional record param on `Customizer.Features.Visitors.Domain.Pageview`, captured synchronously on the request thread by `PageviewCaptureMiddleware`. See [`customizer-prereq.md`](customizer-prereq.md) for the self-contained Customizer-side TODO. This prerequisite is the result of `/speckit-analyze` finding C1 — without it, slice 003's `deviceKey` source (`HttpContext.Request.Headers.UserAgent`) is unreliable under typical fire-and-forget handler timing. Slice 003's MVP cannot merge before the Customizer change lands. Integration tests will fail until merged; unit tests work against a synthetic `Pageview.UserAgent` stub.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (server). No client-side change in slice 003 (backoffice bundle stays slice-001's empty token; reports + content app arrive at slices 005/012).

**Primary Dependencies**:

- `Umbraco.Cms.Core` + `Umbraco.Cms.Web.Common` 17.3.5 (already declared by slice 001; no bump).
- `Customizer` project reference (slice-001 lesson #4: `<ProjectReference>` until Customizer is NuGet-published).
- `Umbraco.Cms.Infrastructure` (`AsyncMigrationBase`, `IScopeProvider`).
- `Microsoft.Extensions.Caching.Memory` (transitive via ASP.NET Core; no new package added — `MemoryCache` is the LRU substrate, sized via `MemoryCacheOptions.SizeLimit`).
- `Microsoft.Extensions.Options` (existing usage; `IOptionsMonitor<AnalyzerSessionOptions>` is the runtime-reloadable binding shape).
- No new transitive dependency; no Central Package Management entry change required.

**Storage**:

- **Owned by Analyzer (new)**: `analyzerSession` table — one row per session (a bounded sequence of pageviews by one visitor on one device within `InactivityTimeoutMinutes`). Hard FK to `customizerVisitorProfile(key)`. Partial unique index `(visitorProfileKey, deviceKey) WHERE isActive = 1` enforces exactly-one-active-session per visitor+device. See `data-model.md` §1.
- **Owned by Analyzer (modified)**: `analyzerEventReceipt` gains a nullable `sessionKey` column + `IDX_analyzerEventReceipt_sessionKey` index. Soft FK (no DB-level constraint) — session row is opened synchronously to the handler thread, so the FK is always durable for new receipts; soft-FK is forward-compatibility for receipts whose session is later anonymised (cascade clears `deviceKey` but preserves `sessionKey`).
- **Read-only by Analyzer**: `customizerVisitorProfile` (Customizer-owned).
- **Migration history**: `AnalyzerMigrationPlan` chains `M0002_AddAnalyzerSessionTableAndReceiptSessionKey` after `M0001`. Idempotency via `TableExists` + `ColumnExists` guards (same shape as Customizer's `M0009`).

**Testing**:

- **Unit**: xUnit v3 + FluentAssertions. Targets: `AnalyzerSessionResolver` (cache-hit-extend, cache-miss-DB-read-extend, stale-session-close-and-open, concurrent race-safety via unique-violation retry, `IOptionsMonitor` reload), `AnalyzerSessionCacheStore` (LRU eviction at capacity, invalidation on close, invalidation on cascade), `AnalyzerSessionCascadeStep` (sets `anonymizedUtc`, clears `deviceKey`, preserves aggregates, idempotent re-run, zero-row no-op), `AnalyzerSessionSweeperService` (closes eligible rows, leaves active rows, idempotent on already-closed rows, exception in tick logs at error and continues loop), `PageviewCapturedHandler` (slice-002; gains a regression test that the resolved `sessionKey` is carried on the write op).
- **Integration**: SQL Server via slice-002's `AnalyzerIntegrationTestBase`. Covers: end-to-end pageview → resolver opens session → receipt persists with FK (US1 AS1), extends within timeout (US1 AS2), closes-and-opens at timeout boundary (US1 AS3), concurrent dispatch produces exactly one session (US1 AS4), `M0002` is idempotent + back-fill-free for pre-existing receipts (US1 AS6), cascade soft-anonymises A and leaves B (US2 AS1), cascade throw rolls back atomically (US2 AS2), sweeper closes eligible rows with logical `endUtc` (US3 AS1), sweeper leaves active rows (US3 AS2), sweeper idempotent on already-closed (US3 AS3), sweeper survives a poisoned tick (US3 AS4 + dispatcher precedent).
- **Public-surface pinning**: extends slice-002's `PublicSurfacePinningTests` baseline. Regenerated as part of this slice; the diff captures (a) `AnalyticsSession` as a new public record under `Analyzer.Analytics`, (b) `CurrentSession` as a new member on `IAnalyticsEventStateProvider`. A Sync Impact-style note in `spec.md` (Assumptions §pinning regen) documents the additive change.
- **Performance**: a single `Analyzer.Tests.Perf.SessionThroughputSmokeTests` (separate from slice 002's perf-smoke; same trait `Category=Perf`) runs 60 seconds of 1000 pv/s synthetic load with the session resolver in the path and asserts (1) p95 request-thread latency delta vs slice-002-only baseline ≤ 3 ms (SC-003), (2) no resolver write blocks the request thread > 10 ms, (3) zero duplicate active sessions emerged.

**Target Platform**: same as slice 002 — Umbraco CMS 17.x hosts on .NET 10. SQL Server is the production data store; Aspire AppHost persistent volume is the local-dev substrate; Testcontainers.MsSql is the CI substrate. SQLite is supported for compositional tests only (FK + partial-unique-index declarations skipped on SQLite per lesson #39).

**Project Type**: same as slice 002 — Razor Class Library (no client artifacts in slice 003).

**Performance Goals**:

- Session-resolution path on the handler thread:
  - Cache hit + extend: ≤ 1 indexed UPDATE; expected < 1 ms with warm SQL connection.
  - Cache miss + open: ≤ 1 indexed SELECT + ≤ 1 INSERT; expected < 3 ms on cold cache, < 1 ms on warm.
  - Lazy-close (US1 AS3): adds ≤ 1 additional UPDATE; bounded by the partial-unique-index existence — never more than two writes per resolution.
- Sweeper tick: bounded by `SweepBatchSize` (default 1000); target ≤ 100 ms per batch on the reference machine.
- Cascade soft-anonymise: ≤ 200 ms for ≤ 1 000 sessions on a single visitor key (SC-004), via a single indexed `UPDATE … WHERE visitorProfileKey = @key`.
- p95 request-thread latency regression vs slice-002 baseline: ≤ 3 ms (SC-003).
- No regression of Customizer's 1000 pv/s sustained / 5000 pv/s peak envelope (FR-010; Principle VIII).

**Constraints**:

- Session-side writes synchronous to the handler thread (NOT the bounded queue). Cache miss falls to the database synchronously — no asynchronous prefetch.
- Hard FK on `analyzerSession.visitorProfileKey`; soft (indexed, unconstrained) FK on `analyzerEventReceipt.sessionKey`.
- Soft-anonymise on cascade (per-table choice authorised by Principle IV v1.1.1).
- No Customizer file modified by this slice (Principle III).
- No new outbox event types (no async cross-product work).
- No backoffice surface, no management API endpoints, no client-bundle change. Aggregation surfaces land in slice 005 + slice 010.
- `deviceKey` is a server-side resolution artefact derived from `User-Agent` — NOT exposed on the public `AnalyticsSession` record; consumers attributing sessions to devices use a future slice's `UserAgent`-bearing column on the receipt.
- All session-related options reloadable at runtime via `IOptionsMonitor<AnalyzerSessionOptions>` (no host restart to retune).

**Scale/Scope**:

- One PR. Target 2–3 developer-days. ~14–18 new server-side source files (resolver, cache store, repository contract + impl, DTO, options, cascade step, sweeper, AnalyticsSession record, migration), ~3 modified slice-002 files (handler — add resolver call; write op — add `SessionKey`; state-provider interface — add `CurrentSession`; state-provider impl + store — add `CurrentSession`; receipt DTO — add `SessionKey` column; receipt repository — write the new column; composer — register new services), ~14–18 test files (unit + integration + perf-smoke). Pinning baseline file is rewritten.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Per `.specify/memory/constitution.md` **v1.1.1**.

| Principle | Gate question | Slice-003 verdict |
|---|---|---|
| I — EntraID-Only Identity (NON-NEGOTIABLE) | Does any new collection path record data without an authenticated EntraID identity? | **PASS** — the resolver opens a session only when the receipt's `visitorProfileKey` is non-empty; slice-002's handler already skips notifications with `VisitorProfileKey = Guid.Empty` and slice 003 inherits that skip. `deviceKey` derives deterministically from the request's `User-Agent` and is NEVER an identity surface (no UA implies no identity — UA is a request-shape attribute on an already-authenticated request). Customizer's middleware never publishes `PageviewCaptured` for unauthenticated requests. |
| II — Spec-Grounded Scope with Declared Drops | Are any out-of-scope FR prefixes cited as parity targets? | **PASS** — spec cites only `FR-EVT-01`, `FR-EVT-02`, `FR-EVT-03`, `FR-COL-01`, `FR-COL-04`. None of the dropped prefixes (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection, §6.2 cookie consent) are cited. The reference doc's `FR-DIM-*` device-dimension family is explicitly out-of-scope for slice 003 — the deviceKey is a private server-side resolution artefact, NOT a public device dimension. |
| III — Customizer Substrate, No Retrofit | Does any change modify Customizer's pinned public surface OR introduce a name collision with a Customizer public type? | **PASS** — `Analyzer.Analytics.AnalyticsSession` is a new Analyzer-owned public record; no Customizer type with that name exists. `AnalyzerSessionCascadeStep` implements Customizer's `IAnonymizationCascadeStep` extension contract — by design; that interface is published exactly for this kind of extension. Slice 003 adds a member to `Analyzer.Analytics.IAnalyticsEventStateProvider` (Analyzer-owned, not pinned by Customizer) — additive per Principle X. Zero Customizer file modified by this slice. The `FK_analyzerEventReceipt_VisitorProfile` declared in `M0001` is not touched. |
| IV — Additive-Only Storage, Cascade-Step Anonymisation | Does the new Analyzer table lack a cascade-step registration or a FK to Customizer substrate? | **PASS** — `analyzerSession` ships with `AnalyzerSessionCascadeStep` registered at composer time (FR-006). Hard FK to `customizerVisitorProfile(key)` per FR-003. Slice 003's per-table choice is **soft-anonymise** (set `anonymizedUtc`, blank `deviceKey`, preserve `pageviewCount` + `startUtc` + `endUtc`); Principle IV v1.1.1 explicitly authorises delete / soft-delete / re-projection per slice. Rationale for the choice (session-level aggregates load-bearing for slice 005 + 010) is documented in spec Assumptions and `research.md` §3. The `analyzerEventReceipt` table's modification is additive — adding a nullable column does not regress slice-002's cascade contract (the cascade-step still hard-deletes the same rows). |
| V — Slice-Driven Delivery via Speckit | Did the change reach this point via the speckit slice flow? Was Constitution Check applied at plan time? | **PASS** — `/speckit-specify` → (`/speckit-clarify` not run; spec's two Assumptions are documented in lieu of clarifications per the no-stopping directive) → `/speckit-plan` (this gate). `/speckit-tasks` and `/speckit-implement` follow. |
| VI — Software Engineering Excellence | SOLID + vertical-slice layout? Tests cover all domain rules + extension contracts? | **PASS** — new code lives under `src/Analyzer/Features/Sessions/{Application,Infrastructure}/`, mirroring `Features/Events/` (slice 002) and Customizer's `Features/<Domain>/` symmetry. Test discipline (unit + integration + pinning + perf-smoke) is codified above; every FR has at least one acceptance scenario. The new `IAnalyticsEventStateProvider.CurrentSession` member is exercised by integration tests against the same scoped-DI assertions slice 002 codified for `CurrentRequestReceipt`. |
| VII — Security by Design | New management/reporting surface lacks RBAC/audit/UPN role-gating? | **PASS (vacuous)** — slice 003 ships no management surface, no reporting surface, no UPN-displaying view. The cascade-step's input is the original `visitorProfileKey` (a Guid) supplied by Customizer's orchestrator; no UPN is read by Analyzer code in this slice. `deviceKey` is a derived hash of `User-Agent`, NOT an identity; it does not require role-gating. The future slice-005 content app and slice-010 reports will surface session aggregates and inherit Principle VII gating at that time. |
| VIII — Performance & Scalability First | Hot-path code: global locks, sync I/O, N+1, OR bypass of Customizer's outbox? | **PASS** — synchronous DB writes are bounded (≤ 1 SELECT + ≤ 1 UPDATE for cache hit; ≤ 1 SELECT + ≤ 1 INSERT for open; ≤ 1 additional UPDATE for lazy-close — bounded total ≤ 3 indexed statements per resolution), all under indexed predicates. LRU cache reduces SQL pressure under steady state. No global locks: the `MemoryCache` is concurrent-by-design; the partial unique index serialises only the race-collision case at the DB layer (rare). No outbox events emitted (slice 003 is in-process). The sweeper paginates batches; never holds a single statement open across the full eligible set. |
| IX — Umbraco-Native & Operator-First | New backoffice UI uses non-`@umbraco-cms/backoffice` primitives, OR operator workflow requires code? | **PASS (vacuous)** — no backoffice UI in slice 003. The anonymisation operator workflow Analyzer participates in is reached via Customizer's existing slice-007 surface, which Analyzer plugs into through `IAnonymizationCascadeStep`. The future slice-005 content app delivers session aggregates to operators using Umbraco-native primitives. |
| X — Extensibility by Design | New public-extension contract has documented DI lifetime, behaviour-compatible custom-impl story, pinning coverage? | **PASS** — `IAnalyticsEventStateProvider` gains the `CurrentSession` member additively; the interface remains scoped (slice-002 contract; unchanged). The new `AnalyticsSession` record is pinned in the slice-002 pinning baseline (regenerated as part of this slice with a Sync Impact-style note in `spec.md` Assumptions). The internal `IAnalyzerSessionResolver` is NOT in the pinned namespace list per slice-002 Clarifications Q3 (it is an internal contract under `Analyzer.Features.Sessions.*`, replaceable behind the interface but not part of the consumer-facing public surface). |

**Result**: all ten gates PASS. No Complexity Tracking entries required. Proceeding to Phase 0.

### Post-design re-evaluation (after Phase 1)

After producing `research.md`, `data-model.md`, `contracts/IAnalyticsEventStateProvider.md`, `contracts/AnalyticsSession.md`, `contracts/AnalyzerSessionResolver.md`, `contracts/AnalyzerSessionCascadeStep.md`, `contracts/AnalyzerSessionSweeperService.md`, and `quickstart.md`, the Constitution Check is re-applied. Re-evaluation findings (2026-05-18; constitution v1.1.1):

- Principle I: still PASS. `data-model.md` confirms `analyzerSession` carries no `oid`/`upn` columns — only `visitorProfileKey` (FK to Customizer's profile) and `deviceKey` (UA hash; non-identity). The resolver short-circuits when `visitorProfileKey == Guid.Empty` is observed (defence in depth; slice-002's handler already filtered this case).
- Principle II: still PASS. Phase-1 artifacts cite the same in-scope `FR-*` set as the spec; no out-of-scope prefixes leak in. The `data-model.md` device-attribution discussion is internal-only — `deviceKey` is NOT exposed on the public `AnalyticsSession` record (`contracts/AnalyticsSession.md` confirms).
- Principle III: still PASS. The new public types (`AnalyticsSession` + `CurrentSession` member) are Analyzer-owned. The `AnalyzerSessionCascadeStep` extends Customizer's published `IAnonymizationCascadeStep` — its documented extension surface. No Customizer file is modified. The new migration declares FK + partial unique index via raw SQL in the body, NOT via `[ForeignKey]` attribute (data-model §1 pinned decision, matching `M0001` precedent).
- Principle IV: still PASS. `data-model.md` documents the hard FK to `customizerVisitorProfile`, the partial unique index `(visitorProfileKey, deviceKey) WHERE isActive = 1`, and the per-table choice = soft-anonymise. `contracts/AnalyzerSessionCascadeStep.md` documents the soft-anonymise semantic + the idempotent re-run behaviour. `analyzerEventReceipt`'s additive `sessionKey` column does not alter the slice-002 cascade contract.
- Principle V: still PASS. This plan completes the planning phase; `/speckit-tasks` is the next phase.
- Principle VI: still PASS. `tasks.md` (generated by `/speckit-tasks`) will codify the unit + integration + pinning + perf-smoke test layers; the contract docs already pin behaviour expectations.
- Principle VII: still PASS (vacuous). No management surface introduced by Phase-1 artifacts.
- Principle VIII: still PASS. `research.md` §2 confirms the in-memory LRU cache is `MemoryCache`-backed (concurrent-by-design, no global locks). §4 confirms the partial unique index drives race-safety with zero application-layer locking. §8 confirms the sweeper's `SweepBatchSize` keeps individual statements bounded.
- Principle IX: still PASS (vacuous). No backoffice UI in slice 003.
- Principle X: still PASS. `contracts/IAnalyticsEventStateProvider.md` (revised) documents the added `CurrentSession` member; `contracts/AnalyticsSession.md` documents the new public record's shape. Pinning baseline regeneration is a deliberate slice-003 task with the Sync Impact note in `spec.md`.

No new Complexity Tracking entries. The plan is consistent with the constitution post-design.

## Project Structure

### Documentation (this feature)

```text
specs/003-session-tracking/
├── plan.md                                            # this file (Phase 2 output of /speckit-plan)
├── spec.md                                            # committed at c55eda4
├── checklists/
│   └── requirements.md                                # committed at c55eda4
├── research.md                                        # Phase 0 output (this command)
├── data-model.md                                      # Phase 1 output (this command)
├── quickstart.md                                      # Phase 1 output (this command)
├── contracts/
│   ├── IAnalyticsEventStateProvider.md                # Phase 1 output (REVISED — adds CurrentSession)
│   ├── AnalyticsSession.md                            # Phase 1 output (NEW)
│   ├── AnalyzerSessionResolver.md                    # Phase 1 output (NEW)
│   ├── AnalyzerSessionCascadeStep.md                  # Phase 1 output (NEW)
│   └── AnalyzerSessionSweeperService.md               # Phase 1 output (NEW)
└── tasks.md                                           # Phase 2 output (/speckit-tasks — NOT this command)
```

### Source Code (repository root)

Adds a new vertical slice `Features/Sessions/` alongside slice 001's `Features/Visitors/` + slice 002's `Features/Events/`. Mirrors the same `{Application,Infrastructure}/` layout. The `Domain/` folder is intentionally absent at slice 003 (the `AnalyticsSession` consumer-facing record lives in `Analyzer.Analytics` next to the pinned `AnalyticsEventReceipt`, per slice-002's U2 decision precedent).

```text
src/Analyzer/
├── Analyzer.csproj                                    # unchanged
├── Constants.cs                                       # +Database.AnalyzerSession
├── Composers/
│   ├── AnalyzerComposer.cs                            # +SessionResolver / cache store / repository / cascade step / sweeper / options; extends slice-002 handler reg
│   ├── AnalyzerSchemaComposer.cs                      # unchanged (migration plan reg already there; plan grows via M0002 chain)
│   └── AnalyzerCompositionException.cs                # unchanged
├── Analytics/
│   ├── IAnalyticsEventStateProvider.cs                # MODIFIED: +CurrentSession property
│   ├── AnalyticsEventStateProvider.cs                 # MODIFIED: +CurrentSession projection from store
│   ├── AnalyticsEventReceipt.cs                       # unchanged (slice-002 record)
│   └── AnalyticsSession.cs                            # NEW: public immutable record (pinned)
├── Features/
│   ├── Common/                                        # NEW: shared internal helpers across slices
│   │   └── Persistence/
│   │       └── UniqueConstraintViolationDetector.cs   # NEW: extracted from slice-002 AnalyzerEventReceiptRepository; shared with slice-003 AnalyzerSessionRepository
│   ├── Visitors/                                      # unchanged from slice 001
│   ├── Events/
│   │   ├── Application/
│   │   │   ├── PageviewCapturedHandler.cs             # MODIFIED: call IAnalyzerSessionResolver before enqueue; carry sessionKey on write op
│   │   │   ├── AnalyticsEventStateStore.cs            # MODIFIED: +CurrentSession field + SetCurrentSession; existing receipt field unchanged
│   │   │   └── Anonymization/
│   │   │       └── AnalyzerEventReceiptCascadeStep.cs # unchanged
│   │   └── Infrastructure/
│   │       ├── Persistence/
│   │       │   ├── AnalyzerEventReceiptDto.cs         # MODIFIED: +SessionKey nullable Guid column
│   │       │   ├── IAnalyzerEventReceiptRepository.cs # unchanged signature (InsertAsync(AnalyticsEventReceipt) already carries everything via the record)
│   │       │   └── AnalyzerEventReceiptRepository.cs  # MODIFIED: maps Receipt.SessionKey → DTO.SessionKey on insert; consumes shared UniqueConstraintViolationDetector (T011)
│   │       └── Dispatcher/
│   │           ├── AnalyzerEventReceiptWriteOp.cs     # unchanged shape; carries the existing AnalyticsEventReceipt (sessionKey lives on the record per AnalyticsEventReceipt addition below)
│   │           ├── AnalyzerEventReceiptWriteQueue.cs  # unchanged
│   │           ├── AnalyzerEventReceiptWriteDispatcher.cs # unchanged
│   │           └── AnalyzerWriteQueueOptions.cs       # unchanged
│   └── Sessions/                                      # NEW: slice-003 vertical slice
│       ├── Application/
│       │   ├── IAnalyzerSessionResolver.cs            # NEW: internal contract — read-extend-or-open
│       │   ├── AnalyzerSessionResolver.cs             # NEW: orchestrates cache + repository + lazy-close + race-safety
│       │   ├── AnalyzerSessionCacheStore.cs           # NEW: MemoryCache-backed LRU wrapper (concurrent, sized)
│       │   ├── DeviceKeyHasher.cs                     # NEW: truncated SHA-256(UA) helper
│       │   └── Anonymization/
│       │       └── AnalyzerSessionCascadeStep.cs      # NEW: soft-anonymise; IAnonymizationCascadeStep impl
│       └── Infrastructure/
│           ├── Persistence/
│           │   ├── AnalyzerSessionDto.cs              # NEW: NPoco DTO
│           │   ├── IAnalyzerSessionRepository.cs      # NEW: contract — GetLatestActive, Insert, Extend, Close, Soft-anonymise, Sweep
│           │   └── AnalyzerSessionRepository.cs       # NEW: NPoco impl; nested IScopeProvider scopes
│           ├── Configuration/
│           │   └── AnalyzerSessionOptions.cs          # NEW: IOptions<>-bound; InactivityTimeoutMinutes / SweepIntervalSeconds / SweepBatchSize / CacheCapacity
│           └── Sweeper/
│               └── AnalyzerSessionSweeperService.cs   # NEW: BackgroundService
└── Migrations/
    ├── AnalyzerMigrationPlan.cs                       # MODIFIED: chains M0002 after M0001
    └── M0002_AddAnalyzerSessionTableAndReceiptSessionKey.cs  # NEW: AsyncMigrationBase; TableExists+ColumnExists idempotent; raw-SQL FK + partial unique index (SQL Server only)

src/Analyzer.Tests/
├── Analyzer.Tests.csproj                              # unchanged (Testcontainers already there for slice 002)
├── Unit/
│   └── Features/
│       ├── Events/
│       │   └── Application/
│       │       └── PageviewCapturedHandlerTests.cs    # MODIFIED: assert resolver invoked + sessionKey carried on write op
│       └── Sessions/
│           ├── Application/
│           │   ├── AnalyzerSessionResolverTests.cs    # cache-hit-extend, cache-miss-open, stale-close-and-open, race-safety, OptionsMonitor reload
│           │   ├── AnalyzerSessionCacheStoreTests.cs  # LRU eviction at capacity, invalidation on close, invalidation on cascade
│           │   ├── DeviceKeyHasherTests.cs            # stable across same-UA requests, distinct UAs → distinct keys, empty UA tolerated
│           │   └── Anonymization/
│           │       └── AnalyzerSessionCascadeStepTests.cs  # soft-anonymise sets anonymizedUtc + blanks deviceKey, preserves aggregates, idempotent, zero-row no-op
│           └── Sweeper/
│               └── AnalyzerSessionSweeperServiceTests.cs  # closes eligible, leaves active, idempotent on already-closed, swallows-tick-error
├── Integration/
│   ├── PageviewSubscription/                          # slice-002 corpus unchanged
│   │   ├── EndToEndCaptureTests.cs                    # MODIFIED: assert sessionKey on persisted receipt
│   │   └── BackPressureDropTests.cs                   # unchanged
│   ├── Sessions/
│   │   ├── ResolveAndAttachTests.cs                   # US1 AS1 + AS2 + AS3 + AS6 against real SQL
│   │   ├── ConcurrentDispatchRaceSafetyTests.cs       # US1 AS4
│   │   ├── CascadeSoftAnonymiseTests.cs               # US2 AS1
│   │   ├── CascadeRollbackTests.cs                    # US2 AS2
│   │   └── SweeperBackgroundServiceTests.cs           # US3 AS1 + AS2 + AS3
│   ├── Anonymization/                                 # slice-002 corpus unchanged
│   │   ├── CascadeDeleteTests.cs                      # unchanged
│   │   └── CascadeRollbackTests.cs                    # unchanged
│   └── StateProvider/
│       ├── ScopedLifetimeTests.cs                     # MODIFIED: also assert CurrentSession lifetime
│       └── CrossRequestIsolationTests.cs              # MODIFIED: assert sessions don't leak across requests
├── PublicSurface/
│   ├── PublicSurfacePinningTests.cs                   # unchanged (test code)
│   └── Baselines/
│       └── Analyzer-public-surface.txt                # REGENERATED with AnalyticsSession + CurrentSession; Sync Impact note in spec.md
└── Perf/
    ├── ThroughputSmokeTests.cs                        # unchanged (slice-002 baseline)
    └── SessionThroughputSmokeTests.cs                 # NEW: SC-003: 1000 pv/s × 60s with resolver; ≤ 3 ms p95 delta; [Trait("Category","Perf")]
```

**Structure Decision**: extend the existing feature-folder vertical-slice layout under `src/Analyzer/Features/Sessions/` (mirrors `Features/Events/` from slice 002 and `Features/Visitors/` from slice 001). Public consumer-facing types (`AnalyticsSession`) join `AnalyticsEventReceipt` in `Analyzer.Analytics` — the pinned namespace per slice-002 Clarifications Q3. Tests stay in the existing `Analyzer.Tests` project with new sub-folders mirroring the production tree. No new test project added; no new NuGet package added (`MemoryCache` is a transitive dependency of ASP.NET Core).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*Constitution Check produced zero violations. This section is intentionally empty.*
