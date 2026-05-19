# Implementation Plan: Forms Tracking

**Branch**: `005-forms-tracking` | **Date**: 2026-05-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-forms-tracking/spec.md`

## Summary

Add per-form lifecycle capture (Impression / Start / Success / Abandon) plus field-level focus/unfocus capture for every Umbraco Form rendered on an Analyzer-instrumented intranet, with two new persistence tables (`analyzerFormEvent` + `analyzerFormFieldEvent`, per spec Q2 resolution), two cascade-step registrations, one Analyzer-owned Umbraco Forms field type that writes the visitor key into Forms entries at submit time (spec Q1 resolution), and an `analyzer-no-tracking` opt-out attribute respected client-side before any POST is issued. Surfacing belongs in a later slice — this is capture-only.

Approach: extend the slice-004 client-bundle pattern (`window.analyzer.send`) with a `forms-tracking` module that attaches `focus` / `blur` / `submit` observers at `DOMContentLoaded`, dispatches against a new management endpoint `POST /umbraco/management/api/v1/analyzer/form-event` that mirrors slice 004's Principle-VII four-corner gate (auth + anti-forgery + validation + audit). Server-side, register Analyzer's `VisitorIdField` Umbraco Forms field type via composer; on submit, resolve `IVisitorIdentifier` and write the Guid into the Forms entry. Abandonment materialisation plugs into slice-003's `AnalyzerSessionSweeperService`: when a session is logically closed, emit one `Abandon` row per `(visitorKey, formKey, sessionKey)` tuple with a `Start` but no `Success`.

## Technical Context

**Language/Version**: C# / .NET 10 (server, RCL); TypeScript 5.x (client, Vite bundle). Both already pinned at the package skeleton (slice 001); no new language pins.

**Primary Dependencies**:
- **Server**: Umbraco.Cms 17.3.5 (pinned), NPoco (transitive), Customizer (project ref), Microsoft.Data.SqlClient. **NEW**: Umbraco.Forms 17.x — Forms package matching the host CMS version. Central-package-management entry to be added in `src/Analyzer/Directory.Packages.props`. Project reference added to `Analyzer.csproj` AND `samples/Analyzer.Host/Analyzer.Host.csproj` (so integration tests can render real forms).
- **Client**: @umbraco-cms/backoffice 17.3.5 (existing), no new client dependencies. Forms tracking ships as a new module in the existing `src/Analyzer/Client/` bundle.
- **Test**: xUnit (existing), FluentAssertions (existing), Testcontainers.MsSql (existing), Microsoft.AspNetCore.Mvc.Testing (existing). No new test dependencies.

**Storage**: Microsoft SQL Server via Umbraco's `IScopeProvider` + NPoco. Two new tables (`analyzerFormEvent`, `analyzerFormFieldEvent`) added by migrations `M0004`, `M0005`, idempotent via `TableExists` guards (slice 002/003/004 pattern). Both tables hard-FK to `customizerVisitorProfile(key)`; soft-FK to `analyzerSession(sessionKey)`.

**Testing**: xUnit. Unit tests at `src/Analyzer.Tests/Unit/Features/Forms/{Application,Infrastructure,Web}/`; integration at `src/Analyzer.Tests/Integration/Forms/`. Reuses slice-002 `AnalyzerIntegrationTestBase` with the issue-#20 `SeedVisitorProfileAsync` helper.

**Target Platform**: Umbraco 17.3.5 host on .NET 10, deployed inside the host organisation's intranet. Identical platform pin to slices 002/003/004.

**Project Type**: Single project (Razor Class Library). Constitution Tech Stack section pins this; no per-slice variation.

**Performance Goals** (from spec Success Criteria):
- 99% lifecycle event rows persisted within 1 s of client interaction at 100 form-interactions/min (SC-001).
- 100% abandonment materialisation rate within one sweeper pass (SC-002).
- 200 ms hard-delete budget for 1 000 rows on the indexed `visitorProfileKey` predicate (SC-004, mirrors slice 004).
- ≤ 10 ms first-contentful-paint overhead from client instrumentation on a 5-form page (SC-008).

**Constraints**:
- **Privacy**: zero field values stored anywhere (SC-003). Schema MUST have no column intended to hold field content; `hadValue` boolean is the only payload property derived from field content.
- **Principle VII gate**: POSTs require backoffice auth + anti-forgery + payload validation + per-success audit-log entry (mirrors slice 004 management endpoint).
- **Cascade-step participation**: Both DELETEs participate in the ambient outer NPoco scope (atomic rollback if a later cascade step throws), matching slice 002 receipt + slice 004 custom-event precedent.
- **Opt-out is client-side**: `analyzer-no-tracking` attribute MUST short-circuit before any POST is issued (defence in depth — never trust server-side to filter).

**Scale/Scope**:
- 2 new tables, 2 cascade-step registrations, 1 management endpoint, 1 Umbraco Forms field type, 1 client-bundle module, 2 additive members on `IAnalyticsEventStateProvider` (`CurrentRequestFormEvents`, `CurrentRequestFormFieldEvents`), 4 new public records (`AnalyticsFormEvent`, `AnalyticsFormFieldEvent` + their event-type enums), 2 migrations (`M0004`, `M0005`).
- Expected slice-005 task count: 50-65 tasks across 6 phases (slice-004 envelope).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (§ below).*

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | EntraID-Only Identity | ✅ PASS | Spec FR-014 + Edge Case "Visitor identity unavailable at capture time": client drops the event if `IVisitorIdentifier` returns `IsAvailable=false`; server endpoint returns 401/403 and persists zero rows (SC-007). No anonymous / cookie / fingerprint path. |
| II | Spec-Grounded Scope with Declared Drops | ✅ PASS | Spec cites only `FR-FRM-01..05` and `NFR-SEC-*` (via Principle VII gate). No out-of-scope `FR-DEP-*` / `FR-DIM-03` / `FR-DIM-04` / §3.3 / §6.2 references. Assumptions section explicitly re-states the drop list. |
| III | Customizer Substrate, No Retrofit | ✅ PASS | Spec Q1 resolved to Analyzer-owned Visitor ID field type. No new Customizer surface. `IVisitorIdentifier` is the existing read contract (slice 002 onward). Customizer's pinned public surface is untouched. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ PASS | Both new tables hard-FK to `customizerVisitorProfile(key)`. Both register `IAnonymizationCascadeStep` (hard-delete participation pattern, matching `AnalyzerEventReceiptCascadeStep` + `AnalyzerCustomEventCascadeStep` precedent). `Abandon` materialisation runs INSIDE the sweeper's outer scope, so anonymisation-during-open-session is safe. |
| V | Slice-Driven Delivery via Speckit | ✅ PASS | Slice 005 specked + planned + tasks-pending. Direct-to-main bypass not in scope. |
| VI | Software Engineering Excellence | ✅ PASS (with note) | Vertical-slice layout under `src/Analyzer/Features/Forms/{Application,Domain,Infrastructure,Web}/`, mirroring slice 004's `Features/CustomEvents/`. Every public domain rule + handler covered by unit + integration tests (slice-004 envelope: 82 unit + 12-15 integration). **Note**: integration coverage for the management endpoint's HTTP boundary remains gated on issue #23 (mgmt-API 404 in test host) — same gap slice 004 left. |
| VII | Security by Design | ✅ PASS | Spec FR-009 + SC-006 + SC-007 explicitly require the same four-corner gate as slice 004: backoffice auth, anti-forgery, payload validation, per-success audit-log entry. Zero rows on 401/403/400. Sensitive opt-out is client-side first (defence in depth). No new credential storage. UPN role-gating not material in this slice (capture-only, no UI). |
| VIII | Performance & Scalability First | ✅ PASS | Capture is fire-and-forget POST (no hot-path blocking on the page render). Hard-delete uses indexed `visitorProfileKey` predicate per SC-004. No global locks, no synchronous network I/O during page resolution. Abandonment materialisation runs on slice-003's background sweeper (already a bounded BackgroundService). |
| IX | Umbraco-Native & Operator-First | ✅ PASS | Auto-attached: no per-page or per-form opt-in configuration required (FR-001). Capture-only slice means no new operator UI; the existing backoffice continues to work unchanged. The Analyzer Visitor ID field type IS configurable via Umbraco Forms' standard field-type designer. |
| X | Extensibility by Design | ✅ PASS | Two additive members on `IAnalyticsEventStateProvider`. No breaking changes to any existing extension contract. New public records (`AnalyticsFormEvent`, `AnalyticsFormFieldEvent`) added with `PublicSurfacePinningTests` updates; additive diff. No DI lifetime changes. |

**Verdict**: 10 / 10 PASS. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/005-forms-tracking/
├── plan.md                 # This file
├── research.md             # Phase 0 output (next)
├── data-model.md           # Phase 1 output
├── quickstart.md           # Phase 1 output
├── contracts/              # Phase 1 output
│   ├── IAnalyzerFormEventCaptureHandler.md
│   ├── IAnalyzerFormFieldEventCaptureHandler.md
│   ├── AnalyzerFormEventCascadeStep.md
│   ├── AnalyzerFormFieldEventCascadeStep.md
│   ├── AnalyzerFormAbandonmentMaterialiser.md
│   └── AnalyzerVisitorIdField.md
├── checklists/
│   └── requirements.md     # already landed (16/16 PASS)
└── tasks.md                # /speckit-tasks output
```

### Source Code (repository root)

```text
src/Analyzer/
├── Analyzer.csproj                 # +Umbraco.Forms package reference
├── Directory.Packages.props        # +Umbraco.Forms central pin
├── Constants.cs                    # +Database.AnalyzerFormEvent / AnalyzerFormFieldEvent
├── Composers/                      # +composer for Forms-feature DI registrations
│   └── AnalyzerFormsComposer.cs (new)
├── Analytics/                      # public surface
│   ├── AnalyticsFormEvent.cs (new)
│   ├── AnalyticsFormFieldEvent.cs (new)
│   ├── AnalyzerFormEventType.cs (new — enum: Impression, Start, Success, Abandon)
│   ├── AnalyzerFormFieldEventType.cs (new — enum: FieldFocus, FieldUnfocus)
│   └── IAnalyticsEventStateProvider.cs (additive: +CurrentRequestFormEvents, +CurrentRequestFormFieldEvents)
├── Migrations/
│   ├── M0004_AddAnalyzerFormEventTable.cs (new)
│   └── M0005_AddAnalyzerFormFieldEventTable.cs (new)
├── Features/
│   └── Forms/                      # new vertical slice
│       ├── Application/
│       │   ├── AnalyzerFormEventCaptureHandler.cs
│       │   ├── AnalyzerFormFieldEventCaptureHandler.cs
│       │   ├── AnalyzerFormEventAuditor.cs
│       │   ├── AnalyzerFormFieldEventAuditor.cs
│       │   ├── Anonymization/
│       │   │   ├── AnalyzerFormEventCascadeStep.cs
│       │   │   └── AnalyzerFormFieldEventCascadeStep.cs
│       │   └── Abandonment/
│       │       └── AnalyzerFormAbandonmentMaterialiser.cs
│       ├── Domain/
│       │   ├── AnalyzerFormEventCapture.cs       # command record
│       │   ├── AnalyzerFormFieldEventCapture.cs  # command record
│       │   └── AnalyzerFormPayloadValidationException.cs
│       ├── Infrastructure/
│       │   ├── Persistence/
│       │   │   ├── AnalyzerFormEventDto.cs
│       │   │   ├── AnalyzerFormFieldEventDto.cs
│       │   │   ├── AnalyzerFormEventRepository.cs
│       │   │   ├── AnalyzerFormFieldEventRepository.cs
│       │   │   ├── IAnalyzerFormEventRepository.cs
│       │   │   └── IAnalyzerFormFieldEventRepository.cs
│       │   └── UmbracoForms/
│       │       └── AnalyzerVisitorIdField.cs     # Umbraco.Forms.Core.Providers.FieldTypes.FieldType
│       └── Web/
│           ├── AnalyzerFormEventManagementController.cs
│           ├── AnalyzerFormEventPayload.cs       # POST DTO
│           ├── AnalyzerFormFieldEventPayload.cs  # POST DTO
│           └── (route prefix already set by slice 004 — extend)
└── Client/                         # TypeScript bundle (existing Vite project)
    ├── src/
    │   ├── analyzer-bundle.ts      # existing entrypoint — wire in forms-tracking module
    │   └── features/
    │       └── forms-tracking/     # new module
    │           ├── form-observer.ts
    │           ├── field-observer.ts
    │           ├── opt-out-attribute.ts
    │           ├── form-event-dispatcher.ts
    │           └── index.ts
    └── public/
        └── umbraco-package.json    # unchanged (forms-tracking is part of analyzer.js bundle)

src/Analyzer.Tests/
├── Unit/
│   └── Features/
│       └── Forms/
│           ├── Application/
│           │   ├── AnalyzerFormEventCaptureHandlerTests.cs
│           │   ├── AnalyzerFormFieldEventCaptureHandlerTests.cs
│           │   ├── AnalyzerFormEventAuditorTests.cs
│           │   └── AnalyzerFormAbandonmentMaterialiserTests.cs
│           ├── Infrastructure/
│           │   ├── AnalyzerFormEventRepositoryTests.cs
│           │   ├── AnalyzerFormFieldEventRepositoryTests.cs
│           │   └── AnalyzerVisitorIdFieldTests.cs
│           └── Web/
│               └── AnalyzerFormEventManagementControllerTests.cs
└── Integration/
    └── Forms/
        ├── EndToEndCaptureTests.cs
        ├── FieldEventCaptureTests.cs
        ├── OptOutComplianceTests.cs
        ├── CascadeHardDeleteTests.cs (×2 — one per table)
        ├── CascadeRollbackTests.cs (×2 — one per table)
        ├── AbandonmentMaterialisationTests.cs
        └── VisitorIdFieldSubmitTests.cs

samples/Analyzer.Host/
└── Analyzer.Host.csproj            # +Umbraco.Forms package reference (so integration tests can render real forms)
```

**Structure Decision**: Slice 005 follows the slice-004 vertical-slice layout exactly. `Features/Forms/` is a new top-level domain folder under `src/Analyzer/Features/`, mirroring `CustomEvents/` and `Events/`. The TypeScript bundle gains a `features/forms-tracking/` submodule wired in from the existing `analyzer-bundle.ts` entrypoint; the package manifest is unchanged. The Umbraco Forms package reference appears in three csproj files (Analyzer, Analyzer.Host, transitively in Analyzer.Tests).

## Complexity Tracking

None. Constitution Check passes 10/10 without justifications.

## Phase 0 — Research

See [`research.md`](./research.md) for the consolidated findings on:

- Umbraco.Forms 17.x package version pin + integration patterns (field-type registration, server-side submit hook, DOM-rendered form identifiers + field identifiers).
- Client-side form observation strategy (`focus` / `blur` / `submit` event capture + `IntersectionObserver` for impressions).
- Two-table data model index strategy + query patterns.
- Abandonment materialisation hook into slice-003 `AnalyzerSessionSweeperService`.
- Audit-log payload shape (mirrors slice 004 `CustomEventAuditor`).
- Public-surface pinning diff (additive — same envelope as slice 004 + slice 003).

## Phase 1 — Design & Contracts

See [`data-model.md`](./data-model.md), [`contracts/`](./contracts/), and [`quickstart.md`](./quickstart.md).

Constitution Check re-evaluation post-design: **PASS** (no design choice introduced a new violation; all 10 gates still satisfied — see footer of `data-model.md` for the re-check audit).
