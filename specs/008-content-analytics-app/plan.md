# Implementation Plan: Per-Content-Node Analytics Content App

**Branch**: `008-content-analytics-app` | **Date**: 2026-05-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-content-analytics-app/spec.md`

## Summary

First **read-side** slice in the Analyzer family. Every Umbraco content node gains an "Analytics" content app tab in the backoffice that surfaces five aggregate metrics keyed by `IPublishedContent.Key`: pageviews in 24h / 7d / 30d windows, unique visitors in 30d, and average time on page in 30d. The data is read live from the capture tables shipped by slices 002 (sessions), 005 (forms), 006 (scroll), and Customizer's `customizerVisitorPageview` — no new persistence, no new capture surface, no new pre-computation.

Three internal pieces ship together: (1) a TypeScript content-app element bundled into the existing Analyzer backoffice bundle, registering against every Umbraco content node via the existing `umbraco-package.json` manifest; (2) a read-only management endpoint at `GET /umbraco/management/api/v1/analyzer/content-analytics/{contentKey}` returning a `ContentAnalyticsSnapshot` DTO; (3) a `IIndividualDataAccessCheck` role-gate check-function that has no observable effect in MVP but is unit-tested for forward compatibility with the future per-visitor drill-down slice (Spec Clarifications §4).

Approach: aggregation runs **on-demand per request** (Spec Assumption + Clarifications-resolved). The query plan joins `customizerVisitorPageview` (by `contentKey` GUID) with `analyzerSession` (by `visitorProfileFk` for the time-on-page delta computation that excludes each session's last pageview per Spec Clarifications §2). A separate `IPublishedContentCache.GetById` lookup populates `isContentCurrentlyTombstoned` from current Umbraco state (Spec Clarifications §3 — present-tense semantic). Anonymised visitors continue to contribute to `uniqueVisitors30d` because Customizer's anonymisation cascade re-keys `identityRef` but preserves the row's `key` — the projection counts distinct `visitorProfileFk` without joining to `identityRef` at all.

Loading state is a skeleton with `aria-busy="true"` (Spec Clarifications §5). Empty-data nodes return 200 with zero metrics (`FR-RPT-010`). Unknown content GUIDs return 404 (`FR-RPT-011`). The tab is visible to any backoffice user with Umbraco's standard content/section permission — no Analyzer-defined gate on aggregate visibility (Spec Clarifications §1).

Two known followup-issues affect the slice's *validation strategy* but not its *implementation contract*: the manual quickstart for this slice is blocked by [#34](https://github.com/d0helmy/analyzer/issues/34) (no EntraID claims shim for local dev) and [#33](https://github.com/d0helmy/analyzer/issues/33) (content-save scope race). Validation falls entirely on automated tests, consistent with slice 007's deferred quickstart posture.

## Technical Context

**Language/Version**: C# / .NET 10 (server, RCL); TypeScript 5.x (backoffice client, Vite bundle). Pinned at slice 001; no new language pins.

**Primary Dependencies**:
- **Server**: Umbraco.Cms 17.3.5 + Umbraco.Cms.Api.Management 17.3.5 (pinned), NPoco (transitive), Customizer (project ref — read-only access to `customizerVisitorPageview`), Microsoft.Data.SqlClient. **No new NuGet packages.** This slice reuses every existing infrastructure piece: NPoco's `IScopeProvider`, the slice-004/005/006/007 management-controller authorization plumbing, and Customizer's project reference.
- **Client**: `@umbraco-cms/backoffice` 17.3.5 (existing pin). Adds `Umbraco.Cms.Web.UI.UI.Element` content-app extension type to the existing bundle's `umbraco-package.json`. No new npm packages.
- **Test**: xUnit v3, FluentAssertions, Testcontainers.MsSql, Microsoft.AspNetCore.Mvc.Testing for the integration path. Vitest 1.x for the content-app element. All existing — no new test dependencies.

**Storage**: Microsoft SQL Server via Umbraco's `IScopeProvider` + NPoco. **No new tables.** No new migrations. The slice issues read-only `SELECT` queries against `customizerVisitorPageview` (by `contentKey` predicate) and `analyzerSession` (by `visitorProfileFk` join). All projection columns are non-PII (counts, GUIDs that don't carry identity, timestamps).

**Testing**: xUnit. Unit tests at `src/Analyzer.Tests/Unit/Features/Reporting/{Application,Infrastructure,Web}/`. Integration tests at `src/Analyzer.Tests/Integration/Reporting/` reuse the slice-002 `AnalyzerIntegrationTestBase` + slice-002/003 `SeedPageviewAsync` / `SeedSessionAsync` / `SeedVisitorProfileAsync` helpers. Vitest tests for the content-app element at `src/Analyzer/Client/src/features/content-analytics/__tests__/`. A new test fixture seeds visitors with varying pageview/session counts to exercise the time-on-page derivation and the anonymisation-preserved cases (`SC-004`, `SC-005`).

**Target Platform**: Umbraco 17.3.5 host on .NET 10, intranet deployment. Identical to slices 002–007.

**Project Type**: Single project (Razor Class Library + backoffice client bundle). No per-slice variation.

**Performance Goals** (from spec Success Criteria):
- SC-001: ≤ 2 s tab-click-to-numbers-visible on a content node with up to 10,000 historical pageviews (median engagement-tier).
- SC-002: ≤ 5 s tab-click-to-numbers-visible on a content node with up to 100,000 historical pageviews in the 30-day window (degraded budget; future pre-computation slice is the escape valve if this becomes the common case).
- The query plan MUST use indexed predicates on `customizerVisitorPageview.contentKey` (index pre-existing per Customizer slice-003) and `customizerVisitorPageview.requestUtc` (composite index already present for retention queries; verified at R2). N+1 patterns are explicitly disallowed (`Principle VIII`).

**Constraints**:
- **Read-only**: no `INSERT`, `UPDATE`, or `DELETE` against any capture table. Confirms `Principle IV` vacuously (no new tables to register an anonymisation cascade for).
- **No PII in projection**: the SELECT clause MUST NOT include `customizerVisitorProfile.identityRef`, `userEmail`, or any other identifying column. Anonymisation-preserved unique-visitor count is computed via `COUNT(DISTINCT visitorProfileFk)` — joins on `customizerVisitorProfile.key` are **not required** for the MVP shape. (`FR-RPT-009` + `SC-005`.)
- **Backoffice authorization**: endpoint gated by `AuthorizationPolicies.BackOfficeAccess` (slice 004-007 precedent).
- **Anti-forgery**: GET endpoint — no anti-forgery requirement (matches Umbraco conventions for read endpoints).
- **No audit-log entries on read**: per `Principle VII`, audit logs are emitted only on state-changing actions. Read endpoints do not emit. (Operators concerned about tracking who accessed what aggregates can rely on Umbraco's section-access audit and standard web server logs.)
- **No client-side time formatting**: numbers come back as integers + seconds; the content-app element formats them in-place with `Intl.NumberFormat` (thousands separator) and a small `formatDurationSeconds()` helper. Locale-specific time zone display is out of scope.
- **`isContentCurrentlyTombstoned`** is computed via `IPublishedContentCache.GetById(contentKey) == null` per `FR-RPT-012` + Spec Clarifications §3. This is an O(1) in-memory cache lookup — no extra DB hit.
- **Loading state**: skeleton placeholders with `aria-busy="true"` per `FR-RPT-013` + Spec Clarifications §5. Spinner / transient-zero / blank-tab patterns are explicitly disallowed.

**Scale/Scope**:
- 0 new tables, 0 cascade-step registrations (vacuous Principle IV), 0 new public extension contracts (the role-gate check-function is internal in MVP — see Constitution Check § Principle X).
- 1 new management endpoint (`GET .../content-analytics/{contentKey}`).
- 1 new content-app extension registered via `umbraco-package.json` (an `Umbraco.Cms.Web.UI.UI.Element` type, applied to every content node).
- 1 new public-surface response DTO (`ContentAnalyticsSnapshot`) under `Analyzer.Reporting.ContentAnalytics.*`.
- 1 new internal check-function (`IIndividualDataAccessCheck` — internal in MVP per Spec Clarifications §4; goes public when the per-visitor drill-down slice arrives).
- ~6 new TypeScript modules under `src/Analyzer/Client/src/features/content-analytics/` (element, repository, formatters, skeleton, types, tests).
- Expected slice-008 task count: **45-55 tasks** across **5 phases** (Foundational, US1, US2, US3, Polish). Larger envelope than slice 007 (54 tasks) because of the new client-side surface area; smaller per-task complexity because there is no new persistence path.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (§ at the foot of `data-model.md`).*

| # | Principle | Status | Evidence |
|---|-----------|--------|----------|
| I | EntraID-Only Identity | ✅ PASS | Read-only slice; no new collection path records data. The endpoint runs server-side and reads visitor aggregates without touching the identity claim chain. The `IIndividualDataAccessCheck` future-gate operates on `ClaimsPrincipal`, but no per-visitor identity data is exposed in MVP. No anonymous / cookie / fingerprint identity path introduced anywhere. |
| II | Spec-Grounded Scope with Declared Drops | ✅ PASS | Spec cites only in-scope IDs (`FR-RPT-*`, `NFR-SEC-*`, `NFR-PER-*`, contract D9 from `docs/INTER-PRODUCT-CONTRACT.md`). Out-of-scope items (click-through attribution `FR-SRC-03`, scroll heatmap rendering, traffic filters `FR-FLT-*`, site-wide Events report, webhook delivery, per-visitor drill-down) are explicitly deferred in the Assumptions section. No `FR-DEP-*` / `FR-DIM-03` / `FR-DIM-04` / §3.3 / §6.2 references. |
| III | Customizer Substrate, No Retrofit | ✅ PASS | Zero Customizer-side change. The slice reads `customizerVisitorPageview` via raw NPoco SELECT — Customizer's pinned public surface (`IAnalyticsStateProvider`, `IPersonalizationProfile`, the slice-002 webhook dispatcher) is untouched. Customizer's `PublicSurfacePinningTests` continue to pass. No name collisions: the new server-side namespace is `Analyzer.Reporting.ContentAnalytics.*`, the new public DTO `Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot` — both deliberately distinct from any Customizer type. The slice does NOT touch Customizer's `PageviewCaptured` notification (the only Customizer-side contract this product depends on) — it reads the persisted side of the same data instead. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ PASS (vacuous) | **No new tables introduced.** The cascade-step gate applies only when new Analyzer event/side tables are added (per constitution v1.1.1 Principle IV wording). The slice reads existing capture tables shipped by slices 002, 005, 006, 007 + Customizer's slice-003 `customizerVisitorPageview` — all of which already have cascade-step registrations from their respective slices. Anonymised visitor rows continue to contribute to unique-visitor aggregate counts (`FR-RPT-009` + `SC-004`) because the projection counts `DISTINCT visitorProfileFk` without joining to the re-keyed `identityRef`. |
| V | Slice-Driven Delivery via Speckit | ✅ PASS | Slice 008 followed `/speckit-specify → /speckit-clarify → /speckit-plan` with `/speckit-tasks → /speckit-implement` to follow. Direct-to-`main` bypass not in scope. Five clarifications resolved upstream (Spec § Clarifications). |
| VI | Software Engineering Excellence | ✅ PASS | Vertical-slice layout under `src/Analyzer/Features/Reporting/{Application,Domain,Infrastructure,Web}/`, mirroring slices 004-007. Every public domain rule + handler + check-function covered by unit + integration tests. Expected envelope: ~50 unit + 10-14 integration + ~25 Vitest tests. **Note**: integration coverage for the management endpoint's HTTP boundary remains gated on issue #23 (mgmt-API 404 in test host) — same gap slices 004-007 left. Listed in Phase 5 polish as `tasks.md` deferred items. |
| VII | Security by Design | ✅ PASS | Defense-in-depth: endpoint validates `contentKey` GUID at the boundary (404 on unknown), authorization gate via `AuthorizationPolicies.BackOfficeAccess` (slice 004-007 precedent), no per-visitor identity data exposed in MVP, role-gate check-function `IIndividualDataAccessCheck` ready for the future per-visitor drill-down slice. Audit logging is **not** required on this read endpoint (read endpoints do not change state per `Principle VII`'s narrowing scope). The slice introduces no new credentials, no plain-text storage, no new external integrations. **Anonymisation-preserved aggregates** (`FR-RPT-009`) deliver the constitutional intent of "right-to-be-forgotten cannot silently corrupt historical aggregates" — anonymised visitors still count, but their identity is never in the projection. |
| VIII | Performance & Scalability First | ✅ PASS (with notes) | Aggregation is on-demand per request (Spec Assumption). The query plan uses indexed predicates on `customizerVisitorPageview.contentKey` (Customizer slice-003 added the index for retention/anonymisation queries — covered by R2 research) and `requestUtc` (composite index). No global locks, no synchronous network I/O, no N+1 patterns: a single SELECT per content node with `GROUP BY` on the appropriate columns produces the four counters in one round-trip. Time-on-page derivation uses `LAG(requestUtc) OVER (PARTITION BY analyzerSession.sessionKey ORDER BY requestUtc)` window function (T-SQL native, single pass — R3 research result). **Notes**: (1) Customizer's slice-002 outbox dispatcher is irrelevant — this is a synchronous read path, not a cross-boundary async write. (2) SC-002's 100k-pageview budget is the soft ceiling. If a deploying intranet has nodes with >100k pageviews in 30d and the budget breaks, a future slice introduces a rollup table. |
| IX | Umbraco-Native & Operator-First | ✅ PASS | Backoffice UI built on `@umbraco-cms/backoffice` 17.3.5 primitives — specifically a custom element registered as an `Umbraco.Cms.Web.UI.UI.Element` content-app extension in `umbraco-package.json`. The content-app pattern is the canonical Umbraco way to add a per-content-node tab; no bespoke UI invention. Editors operate the feature purely from the backoffice — no code changes required after install. Errors are actionable: 404 surfaces "Analytics data not available for this node" rather than a stack trace. **No new operator workflow** other than viewing the tab; no configuration required to enable it (`FR-RPT-001`). |
| X | Extensibility by Design | ✅ PASS | **No new public extension contracts ship in this slice** — a deliberate choice per Spec Clarifications §4. The `IIndividualDataAccessCheck` role-gate check-function is internal in MVP. When the future per-visitor drill-down slice introduces UI that needs custom auth providers (e.g. a host wanting to delegate the gate to a custom user-group source rather than Umbraco's built-in groups), THAT slice will promote the check-function to a public extension surface and add it to `PublicSurfacePinningTests`. The existing Analyzer public extension contracts (`IVisitorIdentifier`, `IAnalyzerSearchQueryNormaliser`, `IEventDimensionExtractor` etc.) are not touched. One additive public DTO (`Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot`) is added to the public surface and registered with `PublicSurfacePinningTests` as a baseline-additive diff. |

**Verdict**: 10 / 10 PASS. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/008-content-analytics-app/
├── plan.md                 # This file
├── research.md             # Phase 0 output (this command)
├── data-model.md           # Phase 1 output (this command)
├── quickstart.md           # Phase 1 output (this command)
├── contracts/              # Phase 1 output (this command)
│   ├── ContentAnalyticsSnapshot.md
│   ├── AnalyzerContentAnalyticsManagementController.md
│   ├── IIndividualDataAccessCheck.md
│   └── ContentAnalyticsContentApp.md
├── checklists/
│   └── requirements.md     # already landed
└── tasks.md                # /speckit-tasks output (next phase)
```

### Source Code (repository root)

```text
src/Analyzer/
├── Constants.cs                                # +ManagementApi.ContentAnalyticsPath, +Authorization keys if any
├── Composers/
│   └── AnalyzerReportingComposer.cs (new)      # DI registrations for the Reporting feature
├── Reporting/                                  # NEW public surface root
│   └── ContentAnalytics/
│       └── ContentAnalyticsSnapshot.cs (new)   # public DTO
├── Features/
│   └── Reporting/                              # NEW feature folder, mirrors Customer pattern
│       ├── Application/
│       │   ├── ContentAnalyticsQuery.cs (new)              # query handler
│       │   ├── ContentAnalyticsQueryService.cs (new)
│       │   └── IIndividualDataAccessCheck.cs (new)         # internal — role-gate check-function
│       │   └── DefaultIndividualDataAccessCheck.cs (new)
│       ├── Domain/
│       │   ├── TimeWindow.cs (new)                         # enum: TwentyFourHours, SevenDays, ThirtyDays
│       │   └── ContentAnalyticsProjection.cs (new)         # internal DTO carrying both server-side computed fields
│       ├── Infrastructure/
│       │   ├── ContentAnalyticsRepository.cs (new)         # NPoco SELECT queries
│       │   └── PublishedContentTombstoneProbe.cs (new)     # wraps IPublishedContentCache lookup
│       └── Web/
│           └── AnalyzerContentAnalyticsManagementController.cs (new)
│
└── Client/src/features/
    └── content-analytics/                                  # NEW client bundle module
        ├── content-app.element.ts (new)                    # Lit element, registered via umbraco-package.json
        ├── content-analytics.repository.ts (new)           # fetches /umbraco/management/api/v1/analyzer/content-analytics/{contentKey}
        ├── types.ts (new)                                  # ContentAnalyticsSnapshot TS shape (mirrors server DTO)
        ├── formatters.ts (new)                             # number + duration formatters
        ├── skeleton.element.ts (new)                       # the loading-state skeleton block
        └── __tests__/
            ├── content-app.element.spec.ts (new)           # renders states (loading/empty/populated/error)
            ├── formatters.spec.ts (new)
            └── repository.spec.ts (new)

src/Analyzer/Client/public/
└── umbraco-package.json                                    # +1 content-app extension entry

src/Analyzer.Tests/
├── Unit/Features/Reporting/
│   ├── Application/
│   │   ├── ContentAnalyticsQueryServiceTests.cs (new)
│   │   ├── DefaultIndividualDataAccessCheckTests.cs (new)
│   │   └── TimeWindowTests.cs (new)
│   ├── Infrastructure/
│   │   ├── ContentAnalyticsRepositoryTests.cs (new)
│   │   └── PublishedContentTombstoneProbeTests.cs (new)
│   └── Web/
│       └── AnalyzerContentAnalyticsManagementControllerTests.cs (new)
├── Integration/Reporting/
│   ├── ContentAnalyticsRepositoryIntegrationTests.cs (new)         # against Testcontainers MSSQL
│   ├── ContentAnalyticsEndToEndTests.cs (new)                      # POST → query, fakes EntraID claims
│   └── AnonymisedVisitorAggregateTests.cs (new)                    # SC-004 anonymisation-preserved counter
└── Contracts/
    └── PublicSurfacePinningTests.cs (existing — additive baseline entry for ContentAnalyticsSnapshot)
```

**Structure Decision**: Single project (Razor Class Library) layout from slices 001-007 is preserved without change. The new vertical slice lives under `src/Analyzer/Features/Reporting/` (mirroring `Features/Search/`, `Features/Scroll/`, etc.), with public surface promoted to `src/Analyzer/Reporting/ContentAnalytics/`. Client-side additions are scoped to `src/Analyzer/Client/src/features/content-analytics/`. Tests follow the slice 002-007 mirror layout. No new top-level project, no new solution entry. Constitution `Principle IX` (Umbraco-native) is honoured by the `umbraco-package.json` content-app extension entry.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No violations. Table intentionally empty.
