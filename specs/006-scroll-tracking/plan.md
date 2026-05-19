# Implementation Plan: Scroll Tracking

**Branch**: `006-scroll-tracking` | **Date**: 2026-05-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/006-scroll-tracking/spec.md`

## Summary

Add per-pageview scroll-depth milestone capture (25 / 50 / 75 / 100 %) for every authenticated visitor browsing an Analyzer-instrumented intranet content node, with one new persistence table (`analyzerScrollSample`), one cascade-step registration, and reuse of the slice-005 `analyzer-no-tracking` opt-out attribute respected client-side before any POST is issued. Surfacing (heatmap visualisation, per-content-node Analytics content app) is **out of scope** for this slice and belongs in a future read-side slice — this is capture-only.

Approach: extend the slice-004/005 client-bundle pattern with a `scroll-tracking` module that attaches a passive `scroll` listener at `DOMContentLoaded`, throttles via `requestAnimationFrame`, fires at most once per (pageview, bucket) tuple, and dispatches against a new management endpoint `POST /umbraco/management/api/v1/analyzer/scroll-event/milestone` that mirrors slice 005's Principle-VII four-corner gate (auth + anti-forgery + validation + audit). Server-side, persist one `analyzerScrollSample` row per accepted milestone behind a unique index on `(pageviewKey, bucket)` that enforces FR-003 idempotency in the database (defence in depth against a buggy client emitting twice). One new `IAnonymizationCascadeStep` hard-deletes the visitor's scroll rows during anonymisation, atomic within the outer NPoco scope. The slice does NOT touch slice-003's session sweeper (no abandonment materialisation needed — milestones are atomic) and does NOT add a new Umbraco package (no Forms-like sub-feature).

## Technical Context

**Language/Version**: C# / .NET 10 (server, RCL); TypeScript 5.x (client, Vite bundle). Both pinned at the package skeleton (slice 001); no new language pins.

**Primary Dependencies**:
- **Server**: Umbraco.Cms 17.3.5 (pinned), Umbraco.Cms.Api.Management 17.3.5 (existing), NPoco (transitive), Customizer (project ref), Microsoft.Data.SqlClient. **No new NuGet packages** — scroll capture reuses the slice-004 management-controller plumbing and slice-005 opt-out client primitives.
- **Client**: existing `@umbraco-cms/backoffice` 17.3.5 (unchanged; not actually consumed by the scroll module — plain TypeScript exposed via the existing bundle). Scroll tracking ships as a new module in `src/Analyzer/Client/src/features/scroll-tracking/`.
- **Test**: xUnit v3 (existing), FluentAssertions (existing), Testcontainers.MsSql (existing), Microsoft.AspNetCore.Mvc.Testing (existing), Vitest 1.x for the TypeScript module (existing). No new test dependencies.

**Storage**: Microsoft SQL Server via Umbraco's `IScopeProvider` + NPoco. One new table (`analyzerScrollSample`) added by migration `M0006`, idempotent via `TableExists` guards (slice 002/003/004/005 pattern). The table hard-FKs to `customizerVisitorProfile(key)` (raw-SQL FK per slice-002 precedent — does NOT import Customizer DTOs per Principle III) and soft-FKs to `customizerPageview(key)` and `analyzerSession(sessionKey)`. Unique index on `(pageviewKey, bucket)` enforces per-pageview-per-bucket idempotency.

**Testing**: xUnit. Unit tests at `src/Analyzer.Tests/Unit/Features/Scroll/{Application,Infrastructure,Web}/`; integration at `src/Analyzer.Tests/Integration/Scroll/`. Reuses slice-002 `AnalyzerIntegrationTestBase` with the issue-#20 `SeedVisitorProfileAsync` + slice-003 `SeedPageviewAsync` helpers. Vitest unit tests for the TypeScript module live next to the slice-004/005 vitest configs.

**Target Platform**: Umbraco 17.3.5 host on .NET 10, deployed inside the host organisation's intranet. Identical platform pin to slices 002/003/004/005.

**Project Type**: Single project (Razor Class Library). Constitution Tech Stack section pins this; no per-slice variation.

**Performance Goals** (from spec Success Criteria):
- SC-001: 99 % milestone rows persisted within 1 s at 200 scroll-events/min (~3.3 events/s sustained — well below slice-002's 1000 pv/s envelope; capture headroom is ample).
- SC-002: Zero `(pageviewKey, bucket)` tuples with more than one row across 1 000 simulated pageviews (DB-enforced via unique index).
- SC-004: 200 ms hard-delete budget for 1 000 rows on the indexed `visitorProfileKey` predicate (mirrors slice 004/005).
- SC-006: ≤ 5 ms FCP overhead from client instrumentation on a 5 000 px-tall page vs slice-005 baseline.

**Constraints**:
- **Idempotency at every layer**: client per-bucket flag prevents double-fire; server unique index `UX_analyzerScrollSample_pageviewBucket` rejects DB duplicates with a 409; slice-003's `UniqueConstraintViolationDetector` distinguishes the constraint violation from other failures.
- **Principle VII gate**: POSTs require backoffice auth + anti-forgery + payload validation + per-success audit-log entry (mirrors slice 004/005 management endpoint).
- **Cascade-step participation**: hard-delete inside the ambient outer NPoco scope (atomic rollback if a later cascade step throws), matching slice 002 receipt + slice 004 custom-event + slice 005 form-event precedent.
- **Opt-out is client-side first**: `analyzer-no-tracking` (introduced in slice 005) MUST short-circuit before any POST is issued; the scroll module reuses the slice-005 opt-out detector. Defence in depth — never trust server-side to filter.
- **Hot-path discipline**: passive scroll listener (no `preventDefault`); throttle via `requestAnimationFrame` (single read per frame); per-bucket flag short-circuits already-fired milestones; debounce ≤ 1 POST per 100 ms scroll window (FR-004).

**Scale/Scope**:
- 1 new table, 1 cascade-step registration, 1 management endpoint, 1 client-bundle module, 1 additive member on `IAnalyticsEventStateProvider` (`CurrentRequestScrollEvents`), 2 new public records (`AnalyticsScrollSample` + `AnalyzerScrollBucket` enum), 1 migration (`M0006`).
- Expected slice-006 task count: **35-50 tasks** across **5 phases** (Foundational, US1, US2, Polish, Lessons). Smaller envelope than slice 005 (73 tasks across 7 phases) because: single table vs two, no Forms field-type sub-feature, no abandonment materialisation, two user stories vs four. Lesson from slice 005: each user story ≈ 16-25 tasks at this domain's complexity.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (§ below).*

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | EntraID-Only Identity | ✅ PASS | Spec FR-008 + Edge Case "Visitor identity unavailable at capture time": client silently drops the event if `IVisitorIdentifier` returns `IsAvailable=false`; server endpoint returns 401/403 and persists zero rows (SC-005). No anonymous / cookie / fingerprint path. |
| II | Spec-Grounded Scope with Declared Drops | ✅ PASS | Spec cites only `FR-COL-02` (in-scope §3.1) and references `FR-HMP-01/02/03` (§3.6) + `FR-RPT-03` (§3.10) as read-side deferred. No out-of-scope `FR-DEP-*` / `FR-DIM-03` / `FR-DIM-04` / §3.3 / §6.2 references. Assumptions section explicitly re-states the read-side deferral. |
| III | Customizer Substrate, No Retrofit | ✅ PASS | Zero Customizer-side change. `IAnalyticsStateProvider.CurrentRequest.PageviewKey` is the existing read contract (Customizer-pinned, slice-002 consumer). `IAnonymizationCascadeStep` registration plugs into the existing DI-discovered orchestrator. New table FKs to `customizerPageview(key)` + `customizerVisitorProfile(key)` via raw SQL — no Customizer DTO import. Customizer's pinned public surface is untouched. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ PASS | New table hard-FKs to `customizerVisitorProfile(key)` AND soft-FKs to `customizerPageview(key)`. Cascade step uses **hard-delete** participation (established pattern; matches Customizer's `GoalReachedCascadeStep` and slices 002/004/005). Cascade-step registration documented in `contracts/AnalyzerScrollSampleCascadeStep.md`. |
| V | Slice-Driven Delivery via Speckit | ✅ PASS | Slice 006 specked + planning. Direct-to-main bypass not in scope. |
| VI | Software Engineering Excellence | ✅ PASS (with note) | Vertical-slice layout under `src/Analyzer/Features/Scroll/{Application,Domain,Infrastructure,Web}/`, mirroring slice 004's `CustomEvents/` and slice 005's `Forms/`. Every public domain rule + handler covered by unit + integration tests (target envelope: ~90 unit + 8-12 integration). **Note**: integration coverage for the management endpoint's HTTP boundary remains gated on issue #23 (mgmt-API 404 in test host) — same gap slices 004/005 left. Listed in Phase 5 polish as `tasks.md` deferred items. |
| VII | Security by Design | ✅ PASS | Spec FR-006 + FR-008 + SC-005 + SC-007 explicitly require the same four-corner gate as slices 004/005: backoffice auth, anti-forgery, payload validation, per-success audit-log entry. Zero rows on 401/403/400. Sensitive opt-out is client-side first (defence in depth). No new credential storage. UPN role-gating not material in this slice (capture-only, no UI). |
| VIII | Performance & Scalability First | ✅ PASS | Capture is passive-listener + rAF-throttled + fire-and-forget POST (no hot-path blocking on page render). Hard-delete uses indexed `visitorProfileKey` predicate per SC-004. No global locks, no synchronous network I/O during page resolution. No N+1 query patterns. Throughput envelope (200 events/min vs slice-002's 1000 pv/s) leaves ample headroom. |
| IX | Umbraco-Native & Operator-First | ✅ PASS | Auto-attached: no per-page or per-content-node opt-in configuration required (FR-001). Capture-only slice means no new operator UI; the existing backoffice continues to work unchanged. Opt-out via the `analyzer-no-tracking` HTML attribute is the same operator-discoverable knob slice 005 ships. |
| X | Extensibility by Design | ✅ PASS | One additive member on `IAnalyticsEventStateProvider` (`CurrentRequestScrollEvents`). No breaking changes to any existing extension contract. New public records (`AnalyticsScrollSample`, `AnalyzerScrollBucket` enum) added with `PublicSurfacePinningTests` updates; additive diff. No DI lifetime changes. |

**Verdict**: 10 / 10 PASS. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/006-scroll-tracking/
├── plan.md                 # This file
├── research.md             # Phase 0 output (next)
├── data-model.md           # Phase 1 output
├── quickstart.md           # Phase 1 output
├── contracts/              # Phase 1 output
│   ├── AnalyticsScrollSample.md
│   ├── IAnalyzerScrollEventCaptureHandler.md
│   ├── AnalyzerScrollEventManagementController.md
│   ├── AnalyzerScrollSampleCascadeStep.md
│   └── AnalyzerScrollObserver.md
├── checklists/
│   └── requirements.md     # already landed (12/12 PASS)
└── tasks.md                # /speckit-tasks output (next phase)
```

### Source Code (repository root)

```text
src/Analyzer/
├── Constants.cs                    # +Database.AnalyzerScrollSample
├── Composers/                      # +composer for Scroll-feature DI registrations
│   └── AnalyzerScrollComposer.cs (new)
├── Analytics/                      # public surface
│   ├── AnalyticsScrollSample.cs (new)
│   ├── AnalyzerScrollBucket.cs (new — enum: Quarter=25, Half=50, ThreeQuarters=75, Full=100)
│   └── IAnalyticsEventStateProvider.cs (additive: +CurrentRequestScrollEvents)
├── Migrations/
│   └── M0006_AddAnalyzerScrollSampleTable.cs (new)
├── Features/
│   └── Scroll/                     # new vertical slice
│       ├── Application/
│       │   ├── AnalyzerScrollEventCaptureHandler.cs
│       │   ├── AnalyzerScrollEventAuditor.cs
│       │   └── Anonymization/
│       │       └── AnalyzerScrollSampleCascadeStep.cs
│       ├── Domain/
│       │   ├── AnalyzerScrollEventCapture.cs       # command record
│       │   └── AnalyzerScrollPayloadValidationException.cs
│       ├── Infrastructure/
│       │   └── Persistence/
│       │       ├── AnalyzerScrollSampleDto.cs
│       │       ├── AnalyzerScrollSampleRepository.cs
│       │       └── IAnalyzerScrollSampleRepository.cs
│       └── Web/
│           ├── AnalyzerScrollEventManagementController.cs
│           └── AnalyzerScrollEventPayload.cs       # POST DTO
└── Client/                         # TypeScript bundle (existing Vite project)
    ├── src/
    │   ├── analyzer-bundle.ts      # existing entrypoint — wire in scroll-tracking module
    │   └── features/
    │       └── scroll-tracking/    # new module
    │           ├── scroll-observer.ts        # passive scroll listener + rAF throttle
    │           ├── milestone-tracker.ts      # per-bucket fire-once state
    │           ├── short-page-detector.ts    # detect zero-scroll case + emit 100% on ready
    │           ├── scroll-event-dispatcher.ts
    │           └── index.ts
    └── public/
        └── umbraco-package.json    # unchanged (scroll-tracking is part of analyzer.js bundle)

src/Analyzer.Tests/
├── Unit/
│   └── Features/
│       └── Scroll/
│           ├── Application/
│           │   ├── AnalyzerScrollEventCaptureHandlerTests.cs
│           │   ├── AnalyzerScrollEventAuditorTests.cs
│           │   └── AnalyzerScrollSampleCascadeStepTests.cs
│           ├── Infrastructure/
│           │   └── AnalyzerScrollSampleRepositoryTests.cs
│           └── Web/
│               └── AnalyzerScrollEventManagementControllerTests.cs
└── Integration/
    └── Scroll/
        ├── EndToEndCaptureTests.cs
        ├── IdempotencyTests.cs                 # unique-index enforcement + 409 mapping
        ├── OptOutComplianceTests.cs
        ├── CascadeHardDeleteTests.cs
        └── CascadeRollbackTests.cs
```

**Structure Decision**: Slice 006 follows the slice-004/005 vertical-slice layout exactly. `Features/Scroll/` is a new top-level domain folder under `src/Analyzer/Features/`, mirroring `CustomEvents/` and `Forms/`. The TypeScript bundle gains a `features/scroll-tracking/` submodule wired in from the existing `analyzer-bundle.ts` entrypoint; the package manifest is unchanged. No host-project (`samples/Analyzer.Host`) edits are required — no new csproj package references (in contrast with slice 005 which added Umbraco.Forms).

## Complexity Tracking

None. Constitution Check passes 10/10 without justifications.

## Phase 0 — Research

See [`research.md`](./research.md) for the consolidated findings on:

- R1: client-side scroll-position observation strategy (passive scroll listener + `requestAnimationFrame` throttle).
- R2: bucket-crossing detection algorithm (per-bucket fire-once flag; high-watermark check).
- R3: short-page handling — detection + 100 % emission on page-ready.
- R4: opt-out attribute reuse from slice 005 (`isOptedOut()` predicate shared between modules).
- R5: single-table persistence shape + unique-index idempotency strategy.
- R6: pageview-key resolution flow (Customizer's `IAnalyticsStateProvider.CurrentRequest.PageviewKey`).
- R7: management endpoint route + Principle-VII gate (mirrors slice 005's `/form-event/lifecycle`).
- R8: audit-log payload shape (mirrors slice 005 `CustomEventAuditor`).
- R9: public-surface pinning diff (additive — mirrors slice 005 envelope).
- R10: no-Customizer-side-change verification (cascade-step DI discovery, pageview FK semantics).

## Phase 1 — Design & Contracts

See [`data-model.md`](./data-model.md), [`contracts/`](./contracts/), and [`quickstart.md`](./quickstart.md).

Constitution Check re-evaluation post-design: **PASS** (no design choice introduced a new violation; all 10 gates still satisfied — see footer of `data-model.md` for the re-check audit).
