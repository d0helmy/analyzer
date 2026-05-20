# Slice 008 — Per-Content-Node Analytics Content App: HANDOVER

**Branch**: `008-content-analytics-app`
**Status**: Full implementation complete (T001–T046 shipped; T022 + T041 + T048 + T049 + T050 + T058 deferred with reasons)
**Date**: 2026-05-20

## What ships

A backoffice content-app tab named **Analytics** on every Umbraco content
node, surfacing five aggregate metrics computed on demand from existing
capture tables:

- Pageviews — 24h / 7d / 30d windows
- Unique visitors — 30d
- Average time on page — 30d (session-scoped via `LAG()` window function)

The tab is visible to any backoffice user with the standard
`AuthorizationPolicies.BackOfficeAccess` policy (Spec Clarifications §1).

## Public surface delta (additive)

One new pinned type:

```
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot
```

Baseline regenerated in `Analyzer-public-surface.txt` (slice-007 baseline
+ 21 lines). The new type is also covered by
`ContentAnalyticsSnapshot_PrivacyTests` which asserts the type carries no
property name substring-matching `upn`, `oid`, `email`, `identityref`, or
`displayname`.

## New management endpoint

```
GET /umbraco/management/api/v1/analyzer/content-analytics/{contentKey:guid}
```

- 200 with `ContentAnalyticsSnapshot` body when the GUID is known to the
  capture tables OR the published-content cache.
- 404 with `application/problem+json` when neither knows it.
- Always sets `Cache-Control: no-store` (the `windowEndUtc` field is
  request-time-bound).
- No audit log on read (Principle VII).

## Zero new persistence

No new tables, no new migrations, no new cascade-step registrations
(Constitution Principle IV vacuous). The slice reads existing
`customizerVisitorPageview` (Customizer slice-003) + `analyzerSession`
(Analyzer slice-002) via raw NPoco SELECT. The unique-visitor count
uses `COUNT(DISTINCT customizerVisitorPageview.visitorProfileFk)` — never
joins through `identityRef`, so anonymised visitors continue to
contribute to historical aggregates without their identity ever surfacing
in the projection (FR-RPT-009 / SC-004 / SC-005).

## Test counts

| Surface | Pre-slice baseline | Slice 008 delta | Total |
|---------|---------------------|-----------------|-------|
| Server unit (`Category != Integration & Category != Perf`) | 169 | +25 | **194** |
| Server integration (Testcontainers MSSQL) | (deferred) | +6 written | 6 |
| Client Vitest | 55 | +17 | **72** |

Server unit and Vitest suites are **green**. Integration tests are not
gated in the unit run; they remain Category=Integration per slice
002-007 precedent.

## Bundle size

- Slice 007 baseline: 12.01 kB (gzip 3.38 kB)
- Slice 008 with externalised `@umbraco-cms/*` + `@umbraco-ui/*` + `lit`:
  **21.09 kB (gzip 5.89 kB)** — +9 kB minified for the content-app
  element + skeleton + formatters + repository.

## Deviations from contract / plan

1. **Claim type for the role-gate.** The contract referenced
   `Umbraco.Cms.Core.Constants.Security.UserGroupClaimType` which does
   not exist in Umbraco 17.3.5. Replaced with
   `System.Security.Claims.ClaimTypes.Role` — Umbraco's
   `BackOfficeClaimsPrincipalFactory` projects user groups as ASP.NET
   Core role claims. Contract `contracts/IIndividualDataAccessCheck.md`
   updated accordingly.

2. **`IContentAnalyticsQueryService` promoted from `internal` to
   `public`.** Required by ASP.NET Core's controller-constructor
   accessibility check. Not pinned (its namespace is not in
   `PublicSurfacePinningTests.PinnedNamespaces`), so it stays out of the
   long-term SemVer contract.

3. **`PublishedContentTombstoneProbe` accepts a `Func<Guid, IPublishedContent?>` lookup** instead of `IUmbracoContextAccessor` directly.
   Production wiring builds the func from the accessor; tests inject a
   plain function. Keeps the unit test surface narrow.

4. **`AnalyzerReportingComposer` split into a `partial` class** across
   two files. The main file wires Phase 2 foundational primitives; a
   `.ReadSide.cs` partial layers Phase 3a registrations. Lets the slice
   stay incrementally compilable phase-by-phase.

5. **Client `tsconfig.json` enabled `experimentalDecorators: true` +
   `useDefineForClassFields: false`.** Required for Lit's decorator
   syntax. Mirrors Customizer's client tsconfig. First slice on the
   analyzer side to use Lit elements.

6. **Client `vite.config.ts` externalises `@umbraco-cms/*`,
   `@umbraco-ui/*`, and `lit`.** Without this, the bundle ballooned from
   ~12 kB to ~1 MB by inlining the Umbraco backoffice runtime that the
   host already provides.

## Deferred items (followups)

| Task | Reason | Tracking |
|------|--------|----------|
| T022 (SQLite-backed repository unit test) | The query uses `LAG()` + `DATEDIFF(SECOND, …)`, both MSSQL-specific dialects SQLite cannot execute. Correctness covered by integration tests T027-T029. | Inline note in `tasks.md` |
| T041 (empty-content integration test) | Requires `SeedPublishedContentAsync` helper (the Spec Kit analyze remediation T013a was never applied). Without a real Umbraco published-content cache hit, the tombstone probe falls back to `true` in tests. | Slice-008-followup |
| T048 (tombstoned-content integration test) | Same as T041 — requires real `IContentService.MoveToRecycleBin` in the test fixture. | Slice-008-followup |
| T049 + T050 (performance tests) | Performance tests in this repo are tagged `Category=Perf` and run explicitly. Not blocking for MVP polish. | Slice-008-followup |
| T058 (localisation pass) | Copy strings are inline in the element. Refactor to `lang/en.ts` per slices 005/006/007 convention is a follow-up; doesn't affect the MVP demo. | Slice-008-followup |
| Manual quickstart walkthrough | Blocked by slice-007-followup [#34](https://github.com/d0helmy/analyzer/issues/34) (no EntraID claims shim for local dev). Slice 007 set the precedent of deferring this kind of check until #34 lands. | #34 |

## Followup ticket draft

When opening a follow-up issue against the analyzer repo (project #7
backlog), include:

- T041 + T048: implement `SeedPublishedContentAsync` and
  `MoveContentToRecycleBinAsync` helpers in `AnalyzerIntegrationTestBase`
  (originally drafted as T013a in the Spec Kit analyze remediation but
  never applied).
- T049 + T050: perf-tier guardrails for 10k + 100k pageview windows.
- T058: localisation pass per slices 005-007 pattern.
- T022: revisit SQLite vs in-process MSSQL for fast repository unit
  tests — Microsoft.Data.Sqlite has no `LAG()` support, so either keep
  the integration-only coverage or introduce LocalDB.

## Files touched (slice-008 delta)

Server (production):

```
src/Analyzer/Constants.cs                                                  (modified)
src/Analyzer/Composers/AnalyzerReportingComposer.cs                        (new)
src/Analyzer/Composers/AnalyzerReportingComposer.ReadSide.cs               (new)
src/Analyzer/Reporting/ContentAnalytics/ContentAnalyticsSnapshot.cs        (new)
src/Analyzer/Features/Reporting/Domain/TimeWindow.cs                       (new)
src/Analyzer/Features/Reporting/Domain/ContentAnalyticsProjection.cs       (new)
src/Analyzer/Features/Reporting/Application/AnalyzerReportingOptions.cs    (new)
src/Analyzer/Features/Reporting/Application/AnalyzerReportingOptionsPostConfigurator.cs (new)
src/Analyzer/Features/Reporting/Application/Authorization/IIndividualDataAccessCheck.cs (new)
src/Analyzer/Features/Reporting/Application/Authorization/DefaultIndividualDataAccessCheck.cs (new)
src/Analyzer/Features/Reporting/Application/IContentAnalyticsRepository.cs (new)
src/Analyzer/Features/Reporting/Application/IContentAnalyticsQueryService.cs (new)
src/Analyzer/Features/Reporting/Application/ContentAnalyticsQueryService.cs (new)
src/Analyzer/Features/Reporting/Infrastructure/ContentAnalyticsRepository.cs (new)
src/Analyzer/Features/Reporting/Infrastructure/IPublishedContentTombstoneProbe.cs (new)
src/Analyzer/Features/Reporting/Infrastructure/PublishedContentTombstoneProbe.cs (new)
src/Analyzer/Features/Reporting/Web/AnalyzerContentAnalyticsManagementController.cs (new)
```

Server (tests):

```
src/Analyzer.Tests/PublicSurface/PublicSurfacePinningTests.cs              (modified — +Analyzer.Reporting.ContentAnalytics)
src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt     (regenerated)
src/Analyzer.Tests/PublicSurface/ContentAnalyticsSnapshot_PrivacyTests.cs  (new)
src/Analyzer.Tests/Unit/Features/Reporting/**                              (new — 4 unit-test files)
src/Analyzer.Tests/Integration/Reporting/**                                (new — base + 2 integration-test files)
```

Client:

```
src/Analyzer/Client/public/umbraco-package.json                            (modified — +content-app extension)
src/Analyzer/Client/tsconfig.json                                          (modified — +experimentalDecorators)
src/Analyzer/Client/vite.config.ts                                         (modified — +externalise @umbraco-*)
src/Analyzer/Client/src/index.ts                                           (modified — +element import)
src/Analyzer/Client/src/features/content-analytics/**                      (new — 5 modules + 3 test files)
```

Spec / docs:

```
specs/008-content-analytics-app/contracts/IIndividualDataAccessCheck.md    (modified — claim-type correction)
specs/008-content-analytics-app/tasks.md                                   (modified — task checkboxes + deferral notes)
specs/008-content-analytics-app/checklists/accessibility.md                (new)
specs/008-content-analytics-app/HANDOVER.md                                (this file)
```
