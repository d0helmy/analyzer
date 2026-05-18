# Implementation Plan: Pageview Subscription + Analytics-Event State Provider

**Branch**: `002-pageview-subscription` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/002-pageview-subscription/spec.md`

## Summary

Slice 002 turns the slice-001 skeleton into Analyzer's first data-recording slice. It does five concrete things, ordered by spec user-story priority:

1. **Subscribe** (US1, P1): register an `INotificationAsyncHandler<PageviewCaptured>` against Customizer's slice-011 notification (already on `main` at `05e989c`). The handler resolves through slice 001's `IVisitorIdentifier` for any side-effects that need the in-process identity, but the bound `Pageview.VisitorProfileKey` is the authoritative FK target.
2. **Persist** (US1, P1): enqueue an event-receipt operation onto a bounded `Channel<T>` (`AnalyzerEventReceiptWriteQueue`); a hosted `AnalyzerEventReceiptWriteDispatcher` background service flushes batches into a new `analyzerEventReceipt` table. The queue mirrors Customizer's `VisitorWriteQueue` shape (`BoundedChannelFullMode.Wait` + `TryWrite`-with-drop-log) so the loss profile matches FR-025. At-most-once delivery (Clarifications Q2).
3. **Cascade-delete** (US2, P2): register an `AnalyzerEventReceiptCascadeStep : IAnonymizationCascadeStep` that **deletes** the visitor's receipt rows inside Customizer's outer NPoco scope (matches Customizer's `GoalReachedCascadeStep` precedent, *not* re-key — that earlier draft of the spec mis-cited the precedent and has been corrected).
4. **Expose state** (US3, P3): publish the new public type `Analyzer.Analytics.IAnalyticsEventStateProvider`, registered scoped, with one member at this slice — the current request's captured event-receipt reference (or `null` if the subscriber has not yet completed for the current request). The provider sits on a request-scoped backing store populated by the subscriber when (rarely) the handler completes before the request thread leaves the rendering pipeline.
5. **Pin** (US3, P3, FR-009): a `PublicSurfacePinningTests` snapshot covers `IAnalyticsEventStateProvider` (new) + `IVisitorIdentifier` + `BaseVisitorIdentifier` (from slice 001, pinned now per Clarifications Q3). Scope excludes the cascade-step impl, the receipt entity DTO, and the composer.

The migration `M0001_AddAnalyzerEventReceiptTable` runs under Umbraco's standard migration pipeline, composed by an `AnalyzerSchemaComposer` (separate from `AnalyzerComposer` — same composer pattern Customizer uses for its `M00xx` series).

Integration tests run against a real SQL Server container via the Aspire AppHost's persistent volume (slice 001 lesson #19), not a SQLite seam — because the cascade step's atomic-rollback semantic under Customizer's outer scope is the load-bearing assertion and SQLite cannot faithfully replicate it.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (server). No client-side change in slice 002 (the backoffice bundle's empty token from slice 001 remains; reports + content app arrive at slices 005/012).

**Primary Dependencies**:

- `Umbraco.Cms.Core` + `Umbraco.Cms.Web.Common` 17.3.5 (already declared by slice 001; no bump).
- `Customizer` project reference (slice-001 lesson #4: not NuGet-published yet — `<ProjectReference>` until it is).
- `Umbraco.Cms.Infrastructure` (Migration base classes, `IScopeProvider`, `AsyncMigrationBase`).
- `System.Threading.Channels` (in-process bounded queue; same primitive Customizer's `VisitorWriteQueue` uses).
- No new transitive dependency introduced; no Central Package Management entry change required.

**Storage**:

- **Owned by Analyzer (new)**: `analyzerEventReceipt` table — one row per processed `PageviewCaptured` notification. Hard FK to `customizerVisitorProfile(key)`. Indexed unconstrained reference to `customizerPageview(key)` (soft FK; pageview row may not persist under back-pressure). Indexed `ReceivedUtc` for future date-range pruning (Clarifications Q1).
- **Read-only by Analyzer**: `customizerPageview`, `customizerVisitorProfile` (Customizer-owned; never written by Analyzer).
- **Migration history**: Analyzer's migration plan registers `M0001_AddAnalyzerEventReceiptTable`; idempotency via `TableExists` guard (same shape as Customizer's `M0009` precedent).

**Testing**:

- **Unit**: xUnit v3 + FluentAssertions. Targets: `PageviewCapturedHandler` (idempotency on duplicate `Pageview.Key`, swallow-and-log on exception, `Guid.Empty` skip-with-warning), `AnalyzerEventReceiptWriteQueue` (drop-on-full returns false + caller logs), `AnalyzerEventReceiptCascadeStep` (delete-by-visitor-key, zero-row no-op).
- **Integration**: SQL Server via Aspire AppHost's persistent container (slice-001 lesson #19). Cover: end-to-end pageview→receipt write (US1 AS1), back-pressure drop (US1 AS2), duplicate dispatch idempotency (US1 AS3), cascade-step deletes rows for visitor A and leaves B untouched inside Customizer's outer scope (US2 AS1), cascade-step throw rolls back the whole anonymisation atomically (US2 AS2), scoped DI lifetime + cross-request isolation for `IAnalyticsEventStateProvider` (US3 AS1, AS2).
- **Public-surface pinning**: `PublicSurfacePinningTests` covers `IAnalyticsEventStateProvider` + `IVisitorIdentifier` + `BaseVisitorIdentifier`. Baseline file checked in. Diff-on-change fails the test; intentional updates regenerate with a justification in the slice's spec.
- **Throughput**: a perf-smoke test runs 60 seconds of 1000 pv/s synthetic load (in-process publishers) and asserts ≥ 99% receipts persisted + p95 baseline-vs-with-Analyzer delta ≤ 2 ms (SC-002). This is a CI-gated test on a known reference machine; perf flakes do not block PRs (annotated `[Trait("Category","Perf")]`).

**Target Platform**: same as slice 001 — Umbraco CMS 17.x hosts on .NET 10. SQL Server is the production data store (slice-001 lesson #19); the Aspire AppHost's persistent SQL container is the dev + integration-test substrate.

**Project Type**: same as slice 001 — Razor Class Library (no client artifacts in slice 002).

**Performance Goals**:

- Subscriber handler enqueue cost on the dispatch thread: < 1 ms (single `Channel.TryWrite` + log) — no DB, no I/O.
- Dispatcher batch flush: bounded by SQL round-trip time; target ≤ 50 ms per batch of 100 receipts on the reference machine.
- Cascade-step delete: ≤ 200 ms for ≤ 10 000 rows on a single visitor key (SC-003), via a single indexed `DELETE … WHERE VisitorProfileKey = @key`.
- p95 request-thread latency regression vs Customizer-only baseline: ≤ 2 ms (SC-002).
- No regression of Customizer's 1000 pv/s sustained / 5000 pv/s peak envelope (FR-010; Principle VIII).

**Constraints**:

- At-most-once delivery (Clarifications Q2). No persistent queue, no synchronous-insert fallback.
- Hard FK on `VisitorProfileKey`; soft FK (indexed reference column only) on `PageviewKey` — forced by Customizer's notification firing independently of pageview persistence.
- Hard delete on cascade (matches `GoalReachedCascadeStep` precedent, *not* re-key).
- No Customizer file modified by this slice.
- No new outbox event types (no async cross-product work yet).
- No backoffice surface, no management API endpoints, no client-bundle change.

**Scale/Scope**:

- One PR. Target 2–3 developer-days. ~12–18 server-side source files (composer, handler, queue, dispatcher, repository, entity DTO, migration, schema composer, cascade step, state provider + impl), ~10–14 test files (unit + integration + perf-smoke + pinning).
- Anticipated 12 further slices on this product per inter-product contract §4.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Per `.specify/memory/constitution.md` **v1.1.0**.

| Principle | Gate question | Slice-002 verdict |
|---|---|---|
| I — EntraID-Only Identity (NON-NEGOTIABLE) | Does any new collection path record data without an authenticated EntraID identity? | **PASS** — the subscriber writes only when `Pageview.VisitorProfileKey != Guid.Empty` (FR-012 cites `FR-ID-05`). Customizer's middleware does not publish `PageviewCaptured` for unauthenticated requests, so the configuration-error fallback (`Guid.Empty`) is the only edge case and is explicitly skip-with-warning per spec edge cases. |
| II — Spec-Grounded Scope with Declared Drops | Are any out-of-scope FR prefixes cited as parity targets? | **PASS** — spec cites only `FR-COL-01`, `FR-COL-04`, `FR-IDP-04`, `FR-ID-05`, `NFR-PER-01..03`, `NFR-SEC-04`. None of the dropped prefixes (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection, §6.2 cookie consent) are cited. |
| III — Customizer Substrate, No Retrofit | Does any change modify Customizer's pinned public surface OR introduce a name collision with a Customizer public type? | **PASS** — `Analyzer.Analytics.IAnalyticsEventStateProvider` is deliberately distinct from Customizer's pinned `Customizer.Analytics.IAnalyticsStateProvider` (per inter-product contract D3). `AnalyzerEventReceiptCascadeStep` implements Customizer's `IAnonymizationCascadeStep` extension contract — by design; this is the contract's documented extension surface. Zero Customizer file modified. |
| IV — Additive-Only Storage, Cascade-Step Anonymisation | Does the new Analyzer table lack a cascade-step registration or a FK to Customizer substrate? | **PASS** — `analyzerEventReceipt` ships with `AnalyzerEventReceiptCascadeStep` registered at composer time (FR-006). Hard FK to `customizerVisitorProfile(key)` per FR-002. The principle's "re-keys it deterministically" wording is interpreted as "participates in the erasure action by doing what's table-appropriate" — Customizer's own `GoalReachedCascadeStep` hard-deletes, so hard-delete is the precedent. (Constitution wording is consistent with this interpretation; no amendment required.) |
| V — Slice-Driven Delivery via Speckit | Did the change reach this point via the speckit slice flow? Was Constitution Check applied at plan time? | **PASS** — `/speckit-specify` → `/speckit-clarify` (3 Qs resolved) → `/speckit-plan` (this gate). `/speckit-tasks` and `/speckit-implement` follow. |
| VI — Software Engineering Excellence | SOLID + vertical-slice layout? Tests cover all domain rules + extension contracts? | **PASS** — code lives under `src/Analyzer/Features/Events/{Application,Domain,Infrastructure}/` mirroring Customizer's vertical-slice layout. Test discipline (unit + integration + pinning + perf-smoke) is codified in the Testing section above; every FR has at least one acceptance scenario backing it. |
| VII — Security by Design | New management/reporting surface lacks RBAC/audit/UPN role-gating? | **PASS (vacuous)** — slice 002 ships no management surface, no reporting surface, no UPN-displaying view. The cascade-step's input is the original `visitorProfileKey` (a Guid) supplied by Customizer's orchestrator; no UPN is read by Analyzer code in this slice. |
| VIII — Performance & Scalability First | Hot-path code: global locks, sync I/O, N+1, OR bypass of Customizer's outbox? | **PASS** — subscriber enqueues into a lock-free `Channel<T>` (`SingleReader=true`, `SingleWriter=false`) and returns; no DB on dispatch thread. The dispatcher batches inserts. Cascade-step performs a single indexed `DELETE … WHERE VisitorProfileKey = @key`. No outbox events emitted (FR-011 reaffirms the discipline; no work to route through it yet). |
| IX — Umbraco-Native & Operator-First | New backoffice UI uses non-`@umbraco-cms/backoffice` primitives, OR operator workflow requires code? | **PASS (vacuous)** — no backoffice UI in slice 002. Anonymisation operator workflow is reached via Customizer's existing slice-007 surface, which Analyzer plugs into through `IAnonymizationCascadeStep`. |
| X — Extensibility by Design | New public-extension contract has documented DI lifetime, behaviour-compatible custom-impl story, pinning coverage? | **PASS** — `IAnalyticsEventStateProvider` is registered **scoped** (FR-007), pinned per FR-009 + SC-005. A custom `IAnalyticsEventStateProvider` is behaviour-compatible because the interface is the only contract callers depend on. Pinning baseline also covers `IVisitorIdentifier` + `BaseVisitorIdentifier` (slice-001's deferred coverage now lands per Clarifications Q3). |

**Result**: all ten gates PASS. No Complexity Tracking entries required. Proceeding to Phase 0.

### Post-design re-evaluation (after Phase 1)

After producing `research.md`, `data-model.md`, `contracts/IAnalyticsEventStateProvider.md`, `contracts/PageviewCapturedHandler.md`, `contracts/AnalyzerEventReceiptCascadeStep.md`, and `quickstart.md`, the Constitution Check is re-applied. Re-evaluation findings (2026-05-18; constitution v1.1.0):

- Principle I: still PASS. `data-model.md` confirms the receipt row carries no `oid`/`upn` columns — only `VisitorProfileKey`. The handler's `Guid.Empty` skip is an FR-required behaviour, not a silent fallback.
- Principle II: still PASS. Phase-1 artifacts cite the same in-scope `FR-*` set as the spec; no out-of-scope prefixes leak in.
- Principle III: still PASS. The new contract namespace `Analyzer.Analytics.IAnalyticsEventStateProvider` is distinct from Customizer's pinned `IAnalyticsStateProvider`. No Customizer file is modified. The cascade-step implementation lives in `Analyzer.Features.Events.Application.Anonymization` — clearly Analyzer-owned despite implementing a Customizer-published interface.
- Principle IV: still PASS. `data-model.md` documents the hard FK on `VisitorProfileKey` and the soft pageview reference. `contracts/AnalyzerEventReceiptCascadeStep.md` documents the hard-delete semantic with the precedent reference. No Analyzer table without cascade-step registration.
- Principle V: still PASS. This plan completes the planning phase; `/speckit-tasks` is the next phase.
- Principle VI: still PASS. `tasks.md` (generated by `/speckit-tasks`) will codify the unit + integration + pinning + perf-smoke test layers; the contract docs already pin behaviour expectations.
- Principle VII: still PASS (vacuous). No management surface introduced by Phase-1 artifacts.
- Principle VIII: still PASS. `research.md` confirms the bounded-queue + dispatcher pattern matches Customizer's `VisitorWriteQueue` reference and stays off the request thread. No outbox event types added.
- Principle IX: still PASS (vacuous). No backoffice UI in slice 002.
- Principle X: still PASS. `contracts/IAnalyticsEventStateProvider.md` documents scoped DI lifetime + the behaviour-compatibility contract. Pinning baseline is checked in.

No new Complexity Tracking entries. The plan is consistent with the constitution post-design.

## Project Structure

### Documentation (this feature)

```text
specs/002-pageview-subscription/
├── plan.md                                            # this file (Phase 2 output of /speckit-plan)
├── spec.md                                            # committed at 703e470
├── checklists/
│   └── requirements.md                                # committed at 703e470
├── research.md                                        # Phase 0 output (this command)
├── data-model.md                                      # Phase 1 output (this command)
├── quickstart.md                                      # Phase 1 output (this command)
├── contracts/
│   ├── IAnalyticsEventStateProvider.md                # Phase 1 output
│   ├── PageviewCapturedHandler.md                     # Phase 1 output
│   └── AnalyzerEventReceiptCascadeStep.md             # Phase 1 output
└── tasks.md                                           # Phase 2 output (/speckit-tasks — NOT this command)
```

### Source Code (repository root)

Adds a new vertical slice `Features/Events/` alongside slice 001's `Features/Visitors/`. Mirrors Customizer's `Features/<Domain>/{Application,Domain,Infrastructure}/` layout.

```text
src/Analyzer/
├── Analyzer.csproj                                    # unchanged
├── Constants.cs                                       # add Database.AnalyzerEventReceipt table-name constant
├── Composers/
│   ├── AnalyzerComposer.cs                            # add IAnalyticsEventStateProvider scoped registration; add PageviewCapturedHandler async-handler registration
│   ├── AnalyzerSchemaComposer.cs                      # NEW: registers Analyzer's IMigrationPlan + the schema-history table identifier
│   └── AnalyzerCompositionException.cs                # unchanged
├── Analytics/                                         # NEW: namespace mirrors Customizer.Analytics for symmetry
│   ├── IAnalyticsEventStateProvider.cs                # NEW: public interface — scoped per-request read contract
│   ├── AnalyticsEventReceipt.cs                       # NEW: public immutable record (pinned alongside the interface — /speckit-analyze finding U2)
│   └── AnalyticsEventStateProvider.cs                 # NEW: internal scoped impl backed by request-scoped state
├── Features/
│   ├── Visitors/                                      # unchanged from slice 001
│   └── Events/                                        # NEW: vertical slice
│       ├── Application/
│       │   ├── PageviewCapturedHandler.cs             # NEW: INotificationAsyncHandler<PageviewCaptured>
│       │   ├── AnalyticsEventStateStore.cs            # NEW: request-scoped backing store the handler writes + the state-provider reads
│       │   └── Anonymization/
│       │       └── AnalyzerEventReceiptCascadeStep.cs # NEW: internal sealed; implements Customizer.IAnonymizationCascadeStep
│       ├── Domain/                                    # (intentionally empty at slice 002; future-slice extension point — AnalyticsEventReceipt moved to Analyzer.Analytics per /speckit-analyze finding U2)
│       └── Infrastructure/
│           ├── Persistence/
│           │   ├── AnalyzerEventReceiptDto.cs         # NEW: NPoco DTO mapped to analyzerEventReceipt table
│           │   ├── IAnalyzerEventReceiptRepository.cs # NEW: repository contract
│           │   └── AnalyzerEventReceiptRepository.cs  # NEW: repository impl (insert, delete-by-visitor)
│           └── Dispatcher/
│               ├── AnalyzerEventReceiptWriteOp.cs     # NEW: queue payload record
│               ├── AnalyzerEventReceiptWriteQueue.cs  # NEW: bounded Channel<T> wrapper (FullMode.Wait + TryWrite)
│               ├── AnalyzerEventReceiptWriteDispatcher.cs # NEW: BackgroundService that drains the queue and batches inserts
│               └── AnalyzerWriteQueueOptions.cs       # NEW: IOptions<>-bound config (capacity, batch size, flush interval)
└── Migrations/
    ├── AnalyzerMigrationPlan.cs                       # NEW: IMigrationPlan listing M0001
    └── M0001_AddAnalyzerEventReceiptTable.cs          # NEW: AsyncMigrationBase; TableExists idempotency

src/Analyzer.Tests/
├── Analyzer.Tests.csproj                              # add Testcontainers? NO — use existing Aspire container (slice-001 lesson #19); add xunit InternalsVisibleTo if new internals introduced
├── Unit/
│   └── Features/Events/Application/
│       ├── PageviewCapturedHandlerTests.cs            # idempotency, swallow-and-log, Guid.Empty skip
│       ├── AnalyzerEventReceiptWriteQueueTests.cs     # drop-on-full returns false
│       └── Anonymization/
│           └── AnalyzerEventReceiptCascadeStepTests.cs # zero-row no-op (against in-memory repo fake)
├── Integration/
│   ├── PageviewSubscription/
│   │   ├── EndToEndCaptureTests.cs                    # US1 AS1 + AS3 against real SQL container
│   │   └── BackPressureDropTests.cs                   # US1 AS2: notification fires with parent pageview row absent
│   ├── Anonymization/
│   │   ├── CascadeDeleteTests.cs                      # US2 AS1: visitor A rows deleted, B untouched
│   │   └── CascadeRollbackTests.cs                    # US2 AS2: throw → outer scope rolls back atomically
│   └── StateProvider/
│       ├── ScopedLifetimeTests.cs                     # US3 AS1
│       └── CrossRequestIsolationTests.cs              # US3 AS2
├── PublicSurface/
│   ├── PublicSurfacePinningTests.cs                   # snapshot test
│   └── Baselines/
│       └── Analyzer-public-surface.txt                # checked-in baseline (US3 AS3, SC-005)
└── Perf/
    └── ThroughputSmokeTests.cs                        # SC-002: 1000 pv/s × 60s; ≤ 2 ms p95 delta; [Trait("Category","Perf")]
```

**Structure Decision**: feature-folder vertical-slice layout (`Features/Events/{Application,Domain,Infrastructure}/`) mirroring Customizer for symmetry. The `Analytics/` namespace at the package root is intentional — it parallels `Customizer.Analytics` so the consumer-facing contract namespace is short, matches the inter-product contract D3 reference (`Analyzer.Analytics.IAnalyticsEventStateProvider`), and stays separate from the internal implementation in `Features/Events/`. Tests stay in the existing `Analyzer.Tests` project with new sub-folders for each test layer (Unit, Integration, PublicSurface, Perf). No new test project added.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*Constitution Check produced zero violations. This section is intentionally empty.*
