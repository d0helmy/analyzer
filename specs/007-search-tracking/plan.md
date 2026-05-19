# Implementation Plan: Internal Search-Tracking Capture

**Branch**: `007-search-tracking` | **Date**: 2026-05-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-search-tracking/spec.md`

## Summary

Add per-pageview internal-search-submission capture for every authenticated visitor invoking a new `analyzer.sendSearch(query, resultCount)` helper from an Analyzer-instrumented intranet page. One new persistence table (`analyzerSearchEvent`) carries raw + normalised query + result count + the standard visitor/session/pageview/content correlation. One new public extension point (`IAnalyzerSearchQueryNormaliser`) lets multilingual hosts swap the default normaliser (trim + NFKC + invariant-lower + whitespace-collapse) without forking. One new cascade-step registration hard-deletes a visitor's search rows on right-to-delete (PII posture per `FR-SRC-04`). Read-side reporting (Events report aggregation, click-through attribution per `FR-SRC-03`) is **out of scope** for this slice — this is capture-only.

Approach: extend the slice-004 / slice-005 / slice-006 client-bundle pattern with a `search-tracking` module that exposes `window.analyzer.sendSearch(...)`, consults the shared `analyzer-no-tracking` opt-out predicate (`Client/src/shared/opt-out-attribute.ts` — established in slice 006) at *call time*, and POSTs to a new management endpoint `POST /umbraco/management/api/v1/analyzer/search-event` that mirrors slice-005 / slice-006's Principle-VII four-corner gate (auth + anti-forgery + payload validation + audit). Server-side, the controller resolves the visitor + active session, normalises the query via `IAnalyzerSearchQueryNormaliser`, persists a single `analyzerSearchEvent` row, advances `lastActivityUtc` via slice-004's `TouchAsync`, and returns `{ eventKey }`. The seventh `IAnonymizationCascadeStep` hard-deletes a visitor's rows atomically within the outer NPoco scope. The slice does **not** touch slice-003's session sweeper (search submissions are atomic events, no abandonment materialisation), adds **no** new package dependency, and requires **no** Customizer-side change.

Two reference-doc / contract divergences are resolved inline in the spec (Clarifications §1 dedicated table; §2 hard-delete cascade) and re-validated in the Constitution Check below.

## Technical Context

**Language/Version**: C# / .NET 10 (server, RCL); TypeScript 5.x (client, Vite bundle). Both pinned at the package skeleton (slice 001); no new language pins.

**Primary Dependencies**:
- **Server**: Umbraco.Cms 17.3.5 (pinned), Umbraco.Cms.Api.Management 17.3.5 (existing), NPoco (transitive), Customizer (project ref), Microsoft.Data.SqlClient. **No new NuGet packages** — search capture reuses the slice-004/005/006 management-controller plumbing, slice-006 shared opt-out predicate, slice-003 session resolver + `TouchAsync` path.
- **Client**: existing `@umbraco-cms/backoffice` 17.3.5 (unchanged; not actually consumed by the search module — plain TypeScript exposed via the existing bundle). Search tracking ships as a new module in `src/Analyzer/Client/src/features/search-tracking/`.
- **Test**: xUnit v3 (existing), FluentAssertions (existing), Testcontainers.MsSql (existing), Microsoft.AspNetCore.Mvc.Testing (existing), Vitest 1.x for the TypeScript module (existing). No new test dependencies.

**Storage**: Microsoft SQL Server via Umbraco's `IScopeProvider` + NPoco. One new table (`analyzerSearchEvent`) added by migration `M0007`, idempotent via `TableExists` guards (slice 002/003/004/005/006 pattern). The table hard-FKs to `customizerVisitorProfile(key)`, `analyzerSession(sessionKey)`, and (soft, tombstone-tolerant per slice-006 precedent) `customizerPageview(key)` — all declared via raw SQL per slice-002 / Principle III (no Customizer DTO imports).

**Testing**: xUnit. Unit tests at `src/Analyzer.Tests/Unit/Features/Search/{Application,Infrastructure,Web}/`; integration at `src/Analyzer.Tests/Integration/Search/`. Reuses slice-002 `AnalyzerIntegrationTestBase` with the issue-#20 `SeedVisitorProfileAsync` + slice-003 `SeedPageviewAsync` + slice-003 `SeedSessionAsync` helpers. Vitest unit tests for the TypeScript module live next to the slice-004/005/006 vitest configs. A new 100-pair normaliser fixture lives at `src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json` per SC-002.

**Target Platform**: Umbraco 17.3.5 host on .NET 10, deployed inside the host organisation's intranet. Identical platform pin to slices 002–006.

**Project Type**: Single project (Razor Class Library). Constitution Tech Stack section pins this; no per-slice variation.

**Performance Goals** (from spec Success Criteria):
- SC-001: 99 % search-event rows persisted within 1 s at 200 search-events/min (~3.3 events/s sustained — well below slice-002's 1000 pv/s envelope; capture headroom is ample).
- SC-002: Default `IAnalyzerSearchQueryNormaliser` matches 100 % of a 100-pair input/expected fixture covering trim / case-fold / NFKC / whitespace-collapse.
- SC-004: 200 ms hard-delete budget for 1 000 rows on the indexed `visitorProfileKey` predicate (mirrors slice 004/005/006).
- SC-007: For 3 000 variant submissions (3 case/whitespace/Unicode variants of 1 000 queries), `GROUP BY normalisedQuery` yields exactly 1 000 distinct groups — the user-facing equivalent of SC-002 at the table level.

**Constraints**:
- **PII posture (FR-SRC-04)**: Search queries are potentially personal data. Three mitigations applied:
  1. **Audit log redaction** — neither `rawQuery` nor `normalisedQuery` ever appears in the structured log substrate; the row in the DB is the canonical (role-gated) record.
  2. **Hard-delete cascade** — right-to-delete must remove the literal query text, not re-key the row (Spec Clarifications §2 — diverges from contract D8).
  3. **Visitor-bound `pageviewKey` validation** — the endpoint rejects payloads whose `pageviewKey` does not belong to the resolved visitor, preventing a misbehaving page script from forging arbitrary correlations.
- **Principle VII gate**: POSTs require backoffice auth + anti-forgery + payload validation + per-success audit-log entry (mirrors slice 004/005/006 management endpoint).
- **Cascade-step participation**: hard-delete inside the ambient outer NPoco scope (atomic rollback if a later cascade step throws), matching slice 002 receipt + slice 004 custom-event + slice 006 scroll-sample precedent.
- **Opt-out is client-side first**: `analyzer-no-tracking` (introduced in slice 005, shared in slice 006) MUST short-circuit before any POST is issued; the search module imports the slice-006 shared predicate and evaluates it **per call** (no long-lived listener to mute, unlike scroll's init-only read).
- **Hot-path discipline**: the helper is fire-and-forget (returns a Promise but does not block the caller); the server endpoint's normalisation is a single in-process pass (no I/O); the `TouchAsync` write is the existing slice-004 single indexed `UPDATE`.

**Scale/Scope**:
- 1 new table, 1 cascade-step registration, 1 management endpoint, 1 client-bundle module, 1 additive member on `IAnalyticsEventStateProvider` (`CurrentRequestSearchEvents`), 1 new public extension point (`IAnalyzerSearchQueryNormaliser` + `DefaultAnalyzerSearchQueryNormaliser`), 2 new public records (`AnalyticsSearchEvent`), 1 migration (`M0007`).
- Expected slice-007 task count: **40-55 tasks** across **5 phases** (Foundational, US1, US2, Polish, Lessons). Slightly larger envelope than slice 006 (47 tasks) because of the new public extension point — `IAnalyzerSearchQueryNormaliser` adds the pinning + default-impl + DI registration + 100-pair fixture work that slice 006 did not need. Lesson from slices 004-006: each user story ≈ 16-25 tasks at this domain's complexity; extension-point work adds ~6-10 tasks.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (§ at the foot of `data-model.md`).*

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | EntraID-Only Identity | ✅ PASS | Spec FR-007 + SC-005: identity gate resolves visitor via `IVisitorIdentifier` (`oid`-first, `upn`-fallback). Anonymous / `IsAvailable=false` / `Guid.Empty` rejected 401/403 with zero rows persisted. No cookie / fingerprint path. |
| II | Spec-Grounded Scope with Declared Drops | ✅ PASS | Spec FR-011 cites only in-scope IDs (`FR-SRC-01`, `FR-SRC-02`, `FR-SRC-04`, `FR-COL-*`). `FR-SRC-03` (click-through attribution) is explicitly deferred to a read-side slice — not cited as a parity target satisfied by this slice. No out-of-scope `FR-DEP-*` / `FR-DIM-03` / `FR-DIM-04` / §3.3 / §6.2 references. |
| III | Customizer Substrate, No Retrofit | ✅ PASS | Zero Customizer-side change. `IAnalyticsStateProvider.CurrentRequest.PageviewKey` is the existing read contract (Customizer-pinned, slice-002 consumer). `IAnonymizationCascadeStep` registration plugs into the existing DI-discovered orchestrator. New table FKs to `customizerPageview(key)` + `customizerVisitorProfile(key)` via raw SQL — no Customizer DTO import. Customizer's pinned public surface is untouched. The cascade-disposition divergence from contract D8 (hard-delete vs re-key) is a **register-a-step** decision, not a Customizer-side change — Customizer's orchestrator does not care which participation pattern the step uses, only that the step exists. Documented in Spec Clarifications §2 and flagged for a contract-D8 follow-up amendment in the PR description. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ PASS | New table hard-FKs to `customizerVisitorProfile(key)` AND `analyzerSession(sessionKey)`. Cascade step uses **hard-delete** participation — chosen per slice and pinned here per Principle IV v1.1.1's participation-pattern menu. Justified by FR-SRC-04 (queries are PII; right-to-delete cannot be satisfied by re-keying a row that still contains the literal personal data). Cascade-step registration documented in `contracts/AnalyzerSearchEventCascadeStep.md`. |
| V | Slice-Driven Delivery via Speckit | ✅ PASS | Slice 007 specked + planning. Direct-to-main bypass not in scope. |
| VI | Software Engineering Excellence | ✅ PASS (with note) | Vertical-slice layout under `src/Analyzer/Features/Search/{Application,Domain,Infrastructure,Web}/`, mirroring slice 004's `CustomEvents/`, slice 005's `Forms/`, and slice 006's `Scroll/`. Every public domain rule + handler covered by unit + integration tests (target envelope: ~95 unit + 10-14 integration including the 100-pair normaliser fixture). **Note**: integration coverage for the management endpoint's HTTP boundary remains gated on issue #23 (mgmt-API 404 in test host) — same gap slices 004/005/006 left. Listed in Phase 5 polish as `tasks.md` deferred items. |
| VII | Security by Design | ✅ PASS | Spec FR-003 + FR-007 + FR-008 + FR-009 + SC-005 + SC-006 explicitly require the same four-corner gate as slices 004/005/006: backoffice auth, anti-forgery, payload validation (including the visitor-bound `pageviewKey` check), per-success audit-log entry. Zero rows on 401/403/400. Sensitive opt-out is client-side first (defence in depth). **Audit-log PII redaction**: neither `rawQuery` nor `normalisedQuery` is logged (Spec FR-009 + SC-006) — this is a Principle-VII *strengthening* relative to slice 004's custom-event auditor, since search queries are first-class PII per FR-SRC-04. No new credential storage. UPN role-gating not material in this slice (capture-only, no UI). |
| VIII | Performance & Scalability First | ✅ PASS | Helper is fire-and-forget (`Promise<{ eventKey }>`); server normalisation is in-process (no I/O); cascade hard-delete uses indexed `visitorProfileKey` predicate per SC-004. No global locks, no synchronous network I/O during page resolution. No N+1 query patterns. Throughput envelope (200 events/min vs slice-002's 1000 pv/s) leaves ample headroom. Session-touch reuses the slice-004 single indexed `UPDATE` `TouchAsync` path — no `pageviewCount` increment. |
| IX | Umbraco-Native & Operator-First | ✅ PASS | The helper is exposed on `window.analyzer.sendSearch(...)`, parity with `window.analyzer.send(...)` from slice 004 — operators learn one shape. Capture-only slice means no new operator UI; the existing backoffice continues to work unchanged. Opt-out via the `analyzer-no-tracking` HTML attribute is the same operator-discoverable knob slices 005/006 ship. |
| X | Extensibility by Design | ✅ PASS | **New public extension point**: `IAnalyzerSearchQueryNormaliser` + `DefaultAnalyzerSearchQueryNormaliser` registered as `Scoped` (matches Umbraco's per-request convention and slice-001's `IVisitorIdentifier` lifetime decision; rationale in `research.md` §R5). Custom implementations registered via single composer call replace the default per Umbraco DI's last-registration-wins convention. The contract is added to `PublicSurfacePinningTests` as an additive diff (no removed/renamed members). One additive `IAnalyticsEventStateProvider` member (`CurrentRequestSearchEvents`). One additive public record (`AnalyticsSearchEvent`). No breaking changes to any existing contract. |

**Verdict**: 10 / 10 PASS. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/007-search-tracking/
├── plan.md                 # This file
├── research.md             # Phase 0 output (this command)
├── data-model.md           # Phase 1 output (this command)
├── quickstart.md           # Phase 1 output (this command)
├── contracts/              # Phase 1 output (this command)
│   ├── AnalyticsSearchEvent.md
│   ├── IAnalyzerSearchQueryNormaliser.md
│   ├── AnalyzerSearchEventManagementController.md
│   ├── AnalyzerSearchEventCascadeStep.md
│   └── AnalyzerSendSearchClient.md
├── checklists/
│   └── requirements.md     # already landed
└── tasks.md                # /speckit-tasks output (next phase)
```

### Source Code (repository root)

```text
src/Analyzer/
├── Constants.cs                    # +Database.AnalyzerSearchEvent
├── Composers/                      # +composer for Search-feature DI registrations
│   └── AnalyzerSearchComposer.cs (new)
├── Analytics/                      # public surface
│   ├── AnalyticsSearchEvent.cs (new)
│   ├── IAnalyzerSearchQueryNormaliser.cs (new — public extension point)
│   └── IAnalyticsEventStateProvider.cs (additive: +CurrentRequestSearchEvents)
├── Migrations/
│   └── M0007_AddAnalyzerSearchEventTable.cs (new)
├── Features/
│   └── Search/                     # new vertical slice
│       ├── Application/
│       │   ├── AnalyzerSearchEventCaptureHandler.cs
│       │   ├── AnalyzerSearchEventAuditor.cs
│       │   ├── Normalisation/
│       │   │   └── DefaultAnalyzerSearchQueryNormaliser.cs
│       │   └── Anonymization/
│       │       └── AnalyzerSearchEventCascadeStep.cs
│       ├── Domain/
│       │   ├── AnalyzerSearchEventCapture.cs       # command record
│       │   └── AnalyzerSearchPayloadValidationException.cs
│       ├── Infrastructure/
│       │   └── Persistence/
│       │       ├── AnalyzerSearchEventDto.cs
│       │       ├── AnalyzerSearchEventRepository.cs
│       │       └── IAnalyzerSearchEventRepository.cs
│       └── Web/
│           ├── AnalyzerSearchEventManagementController.cs
│           └── AnalyzerSearchEventPayload.cs       # POST DTO
└── Client/                         # TypeScript bundle (existing Vite project)
    ├── src/
    │   ├── analyzer-bundle.ts      # existing entrypoint — wire in search-tracking module
    │   ├── shared/
    │   │   └── opt-out-attribute.ts (existing; slice 006 shared predicate — reused unchanged)
    │   └── features/
    │       └── search-tracking/    # new module
    │           ├── send-search.ts            # public helper attached to window.analyzer
    │           ├── search-event-dispatcher.ts
    │           ├── payload.ts                # shared client-side type for the POST body
    │           └── index.ts
    └── public/
        └── umbraco-package.json    # unchanged (search-tracking is part of analyzer.js bundle)

src/Analyzer.Tests/
├── Unit/
│   └── Features/
│       └── Search/
│           ├── Application/
│           │   ├── AnalyzerSearchEventCaptureHandlerTests.cs
│           │   ├── AnalyzerSearchEventAuditorTests.cs
│           │   ├── DefaultAnalyzerSearchQueryNormaliserTests.cs   # consumes the 100-pair fixture
│           │   ├── normaliser-fixture.json                         # SC-002 fixture
│           │   └── AnalyzerSearchEventCascadeStepTests.cs
│           ├── Infrastructure/
│           │   └── AnalyzerSearchEventRepositoryTests.cs
│           └── Web/
│               └── AnalyzerSearchEventManagementControllerTests.cs
└── Integration/
    └── Search/
        ├── EndToEndCaptureTests.cs
        ├── NormalisationAggregationTests.cs       # SC-007 — group-by stability at table level
        ├── OptOutComplianceTests.cs
        ├── CascadeHardDeleteTests.cs
        ├── CascadeRollbackTests.cs
        └── PageviewVisitorBindingTests.cs         # rejects pageviewKey not belonging to visitor
```

**Structure Decision**: Slice 007 follows the slice-004 / 005 / 006 vertical-slice layout exactly. `Features/Search/` is a new top-level domain folder under `src/Analyzer/Features/`, mirroring `CustomEvents/`, `Forms/`, and `Scroll/`. The TypeScript bundle gains a `features/search-tracking/` submodule wired in from the existing `analyzer-bundle.ts` entrypoint; the package manifest is unchanged. No host-project (`samples/Analyzer.Host`) edits are required — no new csproj package references (in contrast with slice 005 which added Umbraco.Forms). The only structural novelty vs slice 006 is the addition of `Features/Search/Application/Normalisation/DefaultAnalyzerSearchQueryNormaliser.cs` paired with the public extension-point interface in `Analytics/IAnalyzerSearchQueryNormaliser.cs` — the first slice to ship a brand-new Analyzer-defined extension surface since slice 001's `IVisitorIdentifier`.

## Complexity Tracking

None. Constitution Check passes 10/10 without justifications. The two reference-doc / contract divergences (Spec Clarifications §1 dedicated table + §2 hard-delete cascade) are resolved at the spec layer with documented rationale and re-validated under Principles III and IV above — neither is a constitution violation, both are scope-significant decisions the spec author resolved inline per the spec's own clarification protocol.

## Phase 0 — Research

See [`research.md`](./research.md) for the consolidated findings on:

- R1: client-side helper API shape (`Promise<{ eventKey } | { skipped: true }>` + per-call opt-out evaluation).
- R2: server-side normalisation algorithm (trim + NFKC + invariant-culture lower + whitespace-run collapse) and its 100-pair fixture.
- R3: visitor-bound `pageviewKey` validation strategy (defends against forged correlations).
- R4: opt-out attribute reuse from slice 006 (`shared/opt-out-attribute.ts` — unchanged).
- R5: extension-point lifetime + registration convention (`IAnalyzerSearchQueryNormaliser` as `Scoped`, last-registration-wins).
- R6: audit-log PII-redaction payload shape (no query in logs).
- R7: management endpoint route + Principle-VII gate (mirrors slice 005 / 006).
- R8: cascade-step disposition choice (hard-delete) and the contract-D8 follow-up amendment.
- R9: public-surface pinning diff (additive — adds `IAnalyzerSearchQueryNormaliser`, `AnalyticsSearchEvent`, `CurrentRequestSearchEvents`).
- R10: no-Customizer-side-change verification.

## Phase 1 — Design & Contracts

See [`data-model.md`](./data-model.md), [`contracts/`](./contracts/), and [`quickstart.md`](./quickstart.md).

Constitution Check re-evaluation post-design: **PASS** (no design choice introduced a new violation; all 10 gates still satisfied — see footer of `data-model.md` for the re-check audit).
