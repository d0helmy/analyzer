# Implementation Plan: Custom Events

**Branch**: `004-custom-events` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/004-custom-events/spec.md`

## Summary

Slice 004 turns slice 003's session resolver into Analyzer's first in-request capture path beyond pageviews. Five concrete things, ordered by spec user-story priority:

1. **Client API entry point** (US1, P1): a small TypeScript module exposed as `window.analyzer.send(kind, category, action, label?, value?)` on the existing App_Plugins bundle (slice 001's empty-token bundle gains its first real export). The entry point is a thin `fetch`-based wrapper around the management endpoint with anti-forgery + cookie-credential handling. Returns `Promise<{ eventKey }>` per Clarification §2.

2. **Management endpoint** (US1, P1): an Umbraco backoffice-route-prefixed POST controller (`AnalyzerCustomEventController`) that authenticates via the standard Umbraco backoffice session, validates the payload, resolves the active session via `IAnalyzerSessionResolver` synchronously (in-request — first reliable consumer of `CurrentSession`), `TouchAsync`-es the session (Clarification §1; new repo method that updates `lastActivityUtc` without bumping `pageviewCount`), persists one `analyzerCustomEvent` row, updates the request-scoped `AnalyticsEventStateStore.AppendCustomEvent`, emits an audit-log entry, and returns HTTP 202 with the new row's `eventKey`.

3. **Persistence** (US1, P1): new `analyzerCustomEvent` table with the FRs-pinned schema. Hard FKs to `analyzerSession.sessionKey` + `customizerVisitorProfile.key`; soft FK on `receiptKey` (rare in-request co-capture case). Migration `M0003` is additive + idempotent.

4. **Cascade-delete** (US2, P2): `AnalyzerCustomEventCascadeStep` hard-deletes the visitor's `analyzerCustomEvent` rows inside Customizer's outer NPoco scope. Third registered `IAnonymizationCascadeStep` (alongside slice-002 receipt hard-delete + slice-003 session soft-anonymise).

5. **Expose state + pin** (US1 + US3, P3 + cross-cutting): `IAnalyticsEventStateProvider` gains `CurrentRequestCustomEvents` returning `IReadOnlyList<AnalyticsCustomEvent>` (empty when none captured this request; never null). The backing `AnalyticsEventStateStore` gains a parallel list field + `AppendCustomEvent(...)` mutator. New public record `Analyzer.Analytics.AnalyticsCustomEvent`. Pinning baseline regenerated.

The slice also lands the **first management surface** for Analyzer (Principle VII gates apply): authenticated EntraID session enforcement, anti-forgery token validation, payload validation at the boundary, structured audit-log entries on every successful capture.

The slice-003 `IAnalyzerSessionRepository` gains an additive `TouchAsync(sessionKey, lastActivityUtc)` method (per Clarification §1) — separate from `ExtendAsync` which also increments `pageviewCount`. The slice-003 resolver does NOT change shape; the new TouchAsync is invoked directly by the custom-event controller path AFTER the resolver returns.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (server) + TypeScript / Vite (client; slice-001 bundle).

**Primary Dependencies**:

- `Umbraco.Cms.Core` + `Umbraco.Cms.Web.Common` 17.3.5 (unchanged; backoffice-authenticated controller base is in `Web.Common`).
- `Umbraco.Cms.Api.Management` (NEW) — Umbraco's standard backoffice management API conventions (authenticated route prefix, anti-forgery integration, OpenAPI generation). Slice 005's content-app endpoint will reuse the same registration.
- `Customizer` project reference (slice-001 lesson #4: `<ProjectReference>` until NuGet-published).
- `Microsoft.AspNetCore.Mvc` (transitive via Umbraco).
- No new NuGet packages on the test project.
- Client bundle: `@umbraco-cms/backoffice` 17.3.5 (already there from slice 001; not actually used for slice 004's `send()` — the client module is plain TypeScript exposed via `window.analyzer`). Slice 005 will start consuming the backoffice primitives.

**Storage**:

- **Owned by Analyzer (new)**: `analyzerCustomEvent` table — one row per `analyzer.send()` call. Hard FK to `analyzerSession.sessionKey` AND `customizerVisitorProfile.key`; soft FK on `receiptKey` (no DB-level FK constraint; indexed; nullable).
- **Read-only by Analyzer**: `analyzerSession`, `customizerVisitorProfile` (slice-003 / Customizer-owned).
- **Migration history**: `AnalyzerMigrationPlan` chains `M0003` after `M0002`. Idempotent via `TableExists` guards.

**Testing**:

- **Unit**: xUnit v3 + FluentAssertions. Targets: `AnalyzerCustomEventController` (anonymous → 401; validation → 400 with structured error; happy path → 202 with eventKey body), `CustomEventPayloadValidator` (string-length caps, non-finite numerics, whitespace-only fields), `AnalyzerCustomEventCascadeStep` (hard-delete by visitor; zero-row no-op), `AnalyticsEventStateStore` (AppendCustomEvent grows list; reads return same instance within scope).
- **Integration**: SQL Server via slice-002's `AnalyzerIntegrationTestBase`. Covers: end-to-end POST → row + state-provider update + audit-log emit (US1 AS1, AS2, AS4), session lazy-close + new session on stale (US1 AS3), bursts attribute to same session + advance lastActivityUtc (US1 AS5; SC-001 parameterised N=1, 3, 10), cascade hard-delete inside outer scope + rollback on throw (US2 AS1, AS2), 401 on anonymous + 400 on bad payloads + audit-only-on-success (US3 AS1–AS4).
- **Public-surface pinning**: extends slice-003 baseline. Diff is purely additive: `AnalyticsCustomEvent` new type block + `CurrentRequestCustomEvents` member on `IAnalyticsEventStateProvider`. Documented as a Sync Impact-style note in spec.md Assumptions §"Public-surface pinning regeneration" (slice 002 + 003 precedent).
- **Performance**: a single `Analyzer.Tests.Perf.CustomEventThroughputSmokeTests` perf-smoke (same `Category=Perf` opt-in trait as slice 002/003). 1000 POST/s × 60s synthetic load; assert p95 ≤ 5 ms (cache-hit) / 12 ms (cache-miss) per SC-003.
- **Client (TypeScript)**: Vitest unit test for the `send()` wrapper — anti-forgery header threading, Promise resolution on 202, rejection on 4xx/5xx with `{ status, message }` shape. Slice 001's vite.config carries the test runner.

**Target Platform**: same as slice 002 / 003 — Umbraco CMS 17.x on .NET 10. SQL Server is the production store; Aspire SQL container is local-dev; Testcontainers.MsSql is CI.

**Project Type**: same as slice 003 — Razor Class Library + thin client bundle. Slice 004 is the first slice that genuinely uses the client bundle (slice 001 shipped it with an empty entrypoint).

**Performance Goals**:

- Endpoint per-call cost (cache-hit path): ≤ 1 indexed UPDATE (TouchAsync) + ≤ 1 indexed INSERT (custom-event row) + ≤ 1 ILogger emit. p95 ≤ 5 ms (SC-003).
- Cache-miss path: + 1 indexed SELECT (resolver's GetLatestActiveAsync). p95 ≤ 12 ms (SC-003).
- Cascade hard-delete: ≤ 200 ms for ≤ 1 000 rows per visitor (SC-004; single indexed `DELETE … WHERE visitorProfileKey = @key`).
- No regression of slice-002 throughput envelope (1000 pv/s sustained) — custom events are a parallel capture path, not a hot-path modifier for pageviews.

**Constraints**:

- Write path is synchronous to the request thread (NOT queued — spec Assumption + Clarification §2's Promise contract requires HTTP 202 = "row persisted").
- Hard FK on `(sessionKey, visitorProfileKey)`; soft FK on `receiptKey`.
- Hard-delete on cascade (matches slice-002 receipt; not slice-003 session's soft-anonymise — per-table choice per Principle IV v1.1.1).
- Anti-forgery via Umbraco standard backoffice infrastructure; no custom anti-forgery code.
- No throttling layer; rely on Umbraco's pipeline + ASP.NET Core rate-limiter as boundary.
- Audit-log via `ILogger` only (no dedicated `analyzerAuditLog` table this slice).
- No backoffice UI; client API entry point is plain TypeScript exposed via `window.analyzer.send`.

**Scale/Scope**:

- One PR. Target 2 developer-days. ~10–14 new server-side files (controller + validator + repository extension + cascade step + DTO + migration + new public record + state-store extension + state-provider extension + composer wiring), ~5 modified slice-001/003 files (composer; IAnalyzerSessionRepository + AnalyzerSessionRepository; AnalyticsEventStateStore; IAnalyticsEventStateProvider + AnalyticsEventStateProvider; Constants), ~1 TypeScript file (`client/src/send.ts` or equivalent + Vitest spec), ~10–12 test files (unit + integration + perf-smoke + Vitest).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Per `.specify/memory/constitution.md` **v1.1.1**.

| Principle | Gate question | Slice-004 verdict |
|---|---|---|
| I — EntraID-Only Identity (NON-NEGOTIABLE) | Does any new collection path record data without an authenticated EntraID identity? | **PASS** — the management endpoint rejects anonymous requests with HTTP 401 (FR-006); successful captures derive the visitor identity from the authenticated backoffice session via slice-001 `IVisitorIdentifier` (UPN-first, OID canonical key per Principle I). No anonymous code path. |
| II — Spec-Grounded Scope with Declared Drops | Are any out-of-scope FR prefixes cited as parity targets? | **PASS** — spec cites only `FR-EVT-04`, `FR-EVT-05`, `FR-COL-01`, `FR-SRC-01`. None of the dropped prefixes (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection, §6.2 cookie consent) are cited. |
| III — Customizer Substrate, No Retrofit | Does any change modify Customizer's pinned public surface OR introduce a name collision with a Customizer public type? | **PASS** — `Analyzer.Analytics.AnalyticsCustomEvent` is a new Analyzer-owned public record; no Customizer type with that name exists. `AnalyzerCustomEventCascadeStep` implements Customizer's published `IAnonymizationCascadeStep` (extension contract; by design). Zero Customizer file modified by this slice. No new cross-product Customizer prereq (verified — all needed surfaces shipped in slice 002 + slice 003 + customizer's slice 007/011/cross-product UA addition). |
| IV — Additive-Only Storage, Cascade-Step Anonymisation | Does the new Analyzer table lack a cascade-step registration or a FK to Customizer substrate? | **PASS** — `analyzerCustomEvent` ships with `AnalyzerCustomEventCascadeStep` registered at composer time (FR-009). Hard FK to `customizerVisitorProfile(key)` per FR-003. Per-table cascade choice = hard-delete (matches slice-002 receipt cascade pattern; Principle IV v1.1.1 authorises). The session FK is hard but to slice-003's Analyzer-owned table (not Customizer's substrate — the slice-003 contract permits this; the substrate constraint is specifically `customizerVisitorProfile.key` or `customizerPageview.Key`, and slice 004 satisfies the former). |
| V — Slice-Driven Delivery via Speckit | Did the change reach this point via the speckit slice flow? Was Constitution Check applied at plan time? | **PASS** — `/speckit-specify` → `/speckit-clarify` (2 Qs resolved; lastActivityUtc TouchAsync + JS Promise contract) → `/speckit-plan` (this gate). `/speckit-tasks` + `/speckit-implement` follow. |
| VI — Software Engineering Excellence | SOLID + vertical-slice layout? Tests cover all domain rules + extension contracts? | **PASS** — new code lives under `src/Analyzer/Features/CustomEvents/{Application,Infrastructure,Web}/` mirroring slice-002/003 layout. Test discipline (unit + integration + pinning + perf-smoke + Vitest) codified above; every FR has at least one acceptance scenario backing it. |
| VII — Security by Design | New management/reporting surface lacks RBAC/audit/UPN role-gating? | **PASS** — the management endpoint is gated on (a) authenticated Umbraco backoffice session (FR-006; first management surface in Analyzer, sets the precedent for slice 005+ surfaces), (b) anti-forgery token validation (FR-006), (c) payload validation at the boundary (FR-007). Every successful capture emits a structured audit-log entry with actor UPN + OID, target, action, timestamp (FR-008). The endpoint does NOT expose individual-level UPN data in responses — only the actor's own captures are recorded (no cross-visitor reads at this slice). UPN role-gating for "view other visitors' events" lands in slice 005's content app, not here. |
| VIII — Performance & Scalability First | Hot-path code: global locks, sync I/O, N+1, OR bypass of Customizer's outbox? | **PASS** — synchronous request-thread writes are bounded (≤ 3 indexed SQL statements per call per FR-010; cache-hit path is 1 UPDATE + 1 INSERT). No global locks (each call resolves its own per-request scope). No N+1 (one INSERT per event; no joined reads on the hot path). No outbox events emitted (the cross-product webhook dispatcher is for cross-process delivery; custom events stay in-process — future slices may emit `customEvent.captured` webhook through Customizer's outbox per inter-product contract D7, but that's deferred). The resolver's slice-003 LRU cache amortises the SELECT across the warm working set. |
| IX — Umbraco-Native & Operator-First | New backoffice UI uses non-`@umbraco-cms/backoffice` primitives, OR operator workflow requires code? | **PASS (vacuous on UI)** — no backoffice UI in slice 004; the client API entry point is plain TypeScript exposed via `window.analyzer.send` (no `@umbraco-cms/backoffice` element). The slice ships zero operator-facing surface. The management endpoint registers under Umbraco's standard management-API conventions (per FR-002 / Assumption "Anti-forgery integration"); operator-facing configuration ("create custom event types") lands in slice 005. |
| X — Extensibility by Design | New public-extension contract has documented DI lifetime, behaviour-compatible custom-impl story, pinning coverage? | **PASS** — `IAnalyticsEventStateProvider` gains `CurrentRequestCustomEvents` additively (interface still scoped per slice-002 contract; unchanged). `AnalyticsCustomEvent` is pinned in the slice-002 pinning baseline (regenerated with a Sync Impact-style note in spec.md Assumptions). The internal `IAnalyzerCustomEventRepository` and the validator are NOT in the pinned namespace list per slice-002 Clarifications Q3 — internal contracts. |

**Result**: all ten gates PASS. No Complexity Tracking entries required. Proceeding to Phase 0.

### Post-design re-evaluation (after Phase 1)

After producing `research.md`, `data-model.md`, `contracts/AnalyticsCustomEvent.md`, `contracts/AnalyzerCustomEventController.md`, `contracts/AnalyzerCustomEventCascadeStep.md`, `contracts/IAnalyticsEventStateProvider.md` (revised), and `quickstart.md`, Constitution Check is re-applied (2026-05-18; constitution v1.1.1):

- Principle I: still PASS. Phase-1 artifacts confirm the controller authenticates via Umbraco's `[Authorize]`-equivalent backoffice infrastructure; anonymous = 401.
- Principle II: still PASS. Phase-1 artifacts cite the same in-scope `FR-*` set.
- Principle III: still PASS. No Customizer file modified. New types Analyzer-namespaced.
- Principle IV: still PASS. `data-model.md` documents the hard FKs + the cascade-step registration. `contracts/AnalyzerCustomEventCascadeStep.md` documents the hard-delete semantic + atomic-rollback inside outer scope.
- Principle V: still PASS. Plan completes Phase 2 planning; `/speckit-tasks` is next.
- Principle VI: still PASS. `tasks.md` (generated by `/speckit-tasks`) will codify the unit + integration + pinning + perf-smoke + Vitest test layers; the contract docs pin behaviour expectations.
- Principle VII: still PASS. `contracts/AnalyzerCustomEventController.md` documents the auth/anti-forgery/validation/audit four-corner gate. First management surface; sets the precedent for slice 005+.
- Principle VIII: still PASS. `research.md` §1 + §3 confirm the synchronous write path stays within FR-010's 3-statement budget. No outbox or queue.
- Principle IX: still PASS (vacuous). No backoffice UI.
- Principle X: still PASS. `contracts/IAnalyticsEventStateProvider.md` (revised) documents the new `CurrentRequestCustomEvents` member. Pinning baseline regen is a deliberate slice-004 task with the Sync Impact note in spec.md.

No new Complexity Tracking entries. Plan is consistent with the constitution post-design.

## Project Structure

### Documentation (this feature)

```text
specs/004-custom-events/
├── plan.md                                            # this file (Phase 2 output of /speckit-plan)
├── spec.md                                            # committed at c5fbbd6
├── checklists/
│   └── requirements.md                                # committed at c5fbbd6
├── research.md                                        # Phase 0 output (this command)
├── data-model.md                                      # Phase 1 output (this command)
├── quickstart.md                                      # Phase 1 output (this command)
├── contracts/
│   ├── IAnalyticsEventStateProvider.md                # Phase 1 output (REVISED — adds CurrentRequestCustomEvents)
│   ├── AnalyticsCustomEvent.md                        # Phase 1 output (NEW public record)
│   ├── AnalyzerCustomEventController.md               # Phase 1 output (NEW management endpoint contract)
│   └── AnalyzerCustomEventCascadeStep.md              # Phase 1 output (NEW)
└── tasks.md                                           # Phase 2 output (/speckit-tasks — NOT this command)
```

### Source Code (repository root)

Adds a new vertical slice `Features/CustomEvents/` alongside slices 001/002/003 verticals. Mirrors `Features/Events/` + `Features/Sessions/` layout. First slice with a `Web/` sub-folder (controller + payload DTO).

```text
src/Analyzer/
├── Analyzer.csproj                                    # unchanged
├── Constants.cs                                       # +Database.AnalyzerCustomEvent; +AuditLog.CustomEventCapture
├── Composers/
│   ├── AnalyzerComposer.cs                            # +CustomEventCapture handler + repo + cascade step + auditor scoped
│   ├── AnalyzerSchemaComposer.cs                      # unchanged
│   └── AnalyzerCompositionException.cs                # unchanged
├── Analytics/
│   ├── IAnalyticsEventStateProvider.cs                # MODIFIED: +CurrentRequestCustomEvents
│   ├── AnalyticsEventStateProvider.cs                 # MODIFIED: +CurrentRequestCustomEvents projection
│   ├── AnalyticsEventReceipt.cs                       # unchanged
│   ├── AnalyticsSession.cs                            # unchanged
│   └── AnalyticsCustomEvent.cs                        # NEW: public immutable record (pinned)
├── Features/
│   ├── Common/                                        # slice-003 baseline (UniqueConstraintViolationDetector)
│   ├── Visitors/                                      # unchanged
│   ├── Events/
│   │   └── Application/
│   │       └── AnalyticsEventStateStore.cs            # MODIFIED: +_currentCustomEvents list + AppendCustomEvent
│   ├── Sessions/
│   │   └── Infrastructure/
│   │       └── Persistence/
│   │           ├── IAnalyzerSessionRepository.cs      # MODIFIED: +TouchAsync(sessionKey, lastActivityUtc, ct)
│   │           └── AnalyzerSessionRepository.cs       # MODIFIED: +TouchAsync impl (1 UPDATE; no pageviewCount change)
│   └── CustomEvents/                                  # NEW vertical slice
│       ├── Application/
│       │   ├── CustomEventCapture.cs                  # NEW: in-process command DTO (visitorProfileKey, sessionKey, payload, receivedUtc)
│       │   ├── CustomEventCaptureHandler.cs           # NEW: orchestrates resolver TouchAsync + repository.InsertAsync + state-store + audit emit
│       │   ├── ICustomEventAuditor.cs                 # NEW: audit-log emit contract (slice-005 will reuse for content-app actions)
│       │   ├── CustomEventAuditor.cs                  # NEW: ILogger-backed impl per spec Assumption
│       │   └── Anonymization/
│       │       └── AnalyzerCustomEventCascadeStep.cs  # NEW: hard-delete by visitor key
│       ├── Infrastructure/
│       │   └── Persistence/
│       │       ├── AnalyzerCustomEventDto.cs          # NEW: NPoco DTO
│       │       ├── IAnalyzerCustomEventRepository.cs  # NEW: InsertAsync + DeleteByVisitorKeyAsync
│       │       └── AnalyzerCustomEventRepository.cs   # NEW: NPoco impl (nested scope, raw-SQL DELETE)
│       └── Web/
│           ├── CustomEventPayload.cs                  # NEW: request DTO + validation attributes
│           ├── CustomEventResponse.cs                 # NEW: response DTO carrying eventKey
│           └── AnalyzerCustomEventController.cs       # NEW: Umbraco backoffice management-API controller
└── Migrations/
    ├── AnalyzerMigrationPlan.cs                       # MODIFIED: chains M0003 after M0002
    └── M0003_AddAnalyzerCustomEventTable.cs           # NEW: AsyncMigrationBase; TableExists; raw-SQL FK + indexes (SQL Server only; SQLite skip)

src/Analyzer/Client/
├── src/
│   ├── index.ts                                       # MODIFIED: re-export send() on window.analyzer
│   └── analytics/
│       ├── send.ts                                    # NEW: thin fetch wrapper, anti-forgery threading, Promise return
│       └── send.spec.ts                               # NEW: Vitest unit tests
└── vite.config.ts                                     # unchanged (slice-001 carries Vitest)

src/Analyzer.Tests/
├── Analyzer.Tests.csproj                              # unchanged
├── Unit/
│   ├── Features/Events/Application/                   # slice-002 unchanged; one PageviewCapturedHandlerTests assertion already covers receipt SessionKey
│   ├── Features/Sessions/                             # slice-003 unchanged; one repo test gains TouchAsync coverage
│   └── Features/CustomEvents/
│       ├── Application/
│       │   ├── CustomEventCaptureHandlerTests.cs      # NEW: orchestrator unit tests
│       │   ├── CustomEventAuditorTests.cs             # NEW: structured log shape
│       │   └── Anonymization/
│       │       └── AnalyzerCustomEventCascadeStepTests.cs  # NEW
│       └── Web/
│           ├── CustomEventPayloadValidatorTests.cs    # NEW: validation rules
│           └── AnalyzerCustomEventControllerTests.cs  # NEW: anonymous 401, validation 400, happy-path 202
├── Integration/
│   ├── CustomEvents/
│   │   ├── EndToEndCaptureTests.cs                    # US1 AS1, AS2, AS4 (parameterised N=1, 3, 10)
│   │   ├── LazyCloseSessionTests.cs                   # US1 AS3
│   │   ├── BurstAttributionTests.cs                   # US1 AS5 (multiple sends → same session, lastActivityUtc advances)
│   │   ├── ReceiptCorrelationTests.cs                 # US1 AS6 (rare in-request co-capture)
│   │   ├── CascadeHardDeleteTests.cs                  # US2 AS1
│   │   ├── CascadeRollbackTests.cs                    # US2 AS2
│   │   └── ValidationAndAuditTests.cs                 # US3 AS1-AS4
│   └── StateProvider/
│       └── ScopedLifetimeTests.cs                     # MODIFIED: extend slice-003 corpus with CurrentRequestCustomEvents assertions
├── PublicSurface/
│   ├── PublicSurfacePinningTests.cs                   # unchanged (test code)
│   └── Baselines/
│       └── Analyzer-public-surface.txt                # REGENERATED with AnalyticsCustomEvent + CurrentRequestCustomEvents; Sync Impact note in spec.md
└── Perf/
    └── CustomEventThroughputSmokeTests.cs             # NEW: SC-003 (1000 events/s × 60s; cache-hit p95 ≤ 5 ms, cache-miss ≤ 12 ms); [Trait("Category","Perf")]
```

**Structure Decision**: extend the existing feature-folder vertical-slice layout under `src/Analyzer/Features/CustomEvents/` (mirrors `Features/Events/` + `Features/Sessions/`). First slice with a `Web/` sub-folder for the controller + request/response DTOs — sets the precedent for slice 005's content app + slice 010+ reports endpoints. Public consumer-facing record `AnalyticsCustomEvent` joins the existing `Analyzer.Analytics` pinned namespace (slice-002 Clarifications Q3). Client TypeScript work extends slice-001's bundle additively. Tests stay in the existing `Analyzer.Tests` project with new sub-folders mirroring the production tree.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*Constitution Check produced zero violations. This section is intentionally empty.*
