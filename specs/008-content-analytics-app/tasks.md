---
description: "Task list for slice 008 — Per-Content-Node Analytics Content App"
---

# Tasks: Per-Content-Node Analytics Content App

**Input**: Design documents from `/specs/008-content-analytics-app/`

**Prerequisites**: plan.md (✓), spec.md (✓), research.md (✓), data-model.md (✓), contracts/ (✓), quickstart.md (✓)

**Tests**: included — Constitution Principle VI requires unit + integration coverage for every public domain rule, handler, and extension contract. Vitest coverage required for every state in the content-app element per `FR-RPT-013` + Spec Clarifications §5.

**Organization**: Tasks grouped by user story to enable independent implementation. MVP scope is Phase 1 + Phase 2 + Phase 3 (US1). US2 and US3 layer on additively.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1 / US2 / US3); Setup / Foundational / Polish have no story label.

## Path Conventions

Single project (RCL package per Constitution Tech Stack):
- Server: `src/Analyzer/`, tests at `src/Analyzer.Tests/`
- Client: `src/Analyzer/Client/src/`, package manifest at `src/Analyzer/Client/public/umbraco-package.json`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the Reporting feature folder skeleton + Constants entries. No new tables, no new migrations, no new NuGet packages, no new npm packages — this slice is read-only on top of existing infrastructure.

- [X] T001 Add `Constants.ManagementApi.ContentAnalyticsPath = "/umbraco/management/api/v1/analyzer/content-analytics"` to `src/Analyzer/Constants.cs`. Also add `Constants.Configuration.ReportingSection = "Analyzer:Reporting"`. Mirrors slice-007's `Constants.ManagementApi.SearchEventPath` convention.
- [X] T002 [P] Create the Features/Reporting feature folder skeleton at `src/Analyzer/Features/Reporting/{Application,Domain,Infrastructure,Web}/`. Add `Features/Reporting/Application/Authorization/` subfolder for the role-gate check-function. Mirrors slice-007's `Features/Search/` layout.
- [X] T003 [P] Create the public-surface root at `src/Analyzer/Reporting/ContentAnalytics/`. This becomes the namespace for `ContentAnalyticsSnapshot` (T005). Sibling to `src/Analyzer/Analytics/` (slice 001+) and `src/Analyzer/Sessions/` (slice 002).
- [X] T004 [P] Add empty `AnalyzerReportingComposer` at `src/Analyzer/Composers/AnalyzerReportingComposer.cs`. Implements `IComposer`, empty `Compose(IUmbracoBuilder)` body. Tasks T011 + T020 + T034 add DI registrations into it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Public DTO, internal domain types, options POCO, role-gate primitive, and the DI wiring that every user-story phase consumes. Honours Constitution Principle X (extensibility) by pinning the public DTO and Principle VII (security) by shipping the gate primitive even though MVP has nothing for it to filter.

**⚠️ CRITICAL**: No US1 / US2 / US3 work can begin until this phase is complete.

- [X] T005 [P] Public DTO: `src/Analyzer/Reporting/ContentAnalytics/ContentAnalyticsSnapshot.cs` per `contracts/ContentAnalyticsSnapshot.md` + data-model §ContentAnalyticsSnapshot. C# record with all 9 init-only required members. Include the XML doc block establishing the privacy invariant ("contains no field carrying personally-identifying information").
- [X] T006 [P] Internal domain enum: `src/Analyzer/Features/Reporting/Domain/TimeWindow.cs` per data-model §TimeWindow. Three members: `TwentyFourHours`, `SevenDays`, `ThirtyDays`. Used as labels only — the SQL layer takes `DateTimeOffset` start/end directly.
- [X] T007 [P] Internal projection DTO: `src/Analyzer/Features/Reporting/Domain/ContentAnalyticsProjection.cs` per data-model §ContentAnalyticsProjection. Includes `hasAnyCaptureRow` boolean used by the controller to choose 200-with-zeros vs 404 (`FR-RPT-010` / `FR-RPT-011`).
- [X] T008 [P] Options POCO: `src/Analyzer/Features/Reporting/Application/AnalyzerReportingOptions.cs` per data-model §AnalyzerReportingOptions. Single property `IndividualDataUserGroupAlias : string?`. Add `[ConfigurationKeyName]` if Umbraco's binding convention requires.
- [X] T009 [P] Options post-configurator: `src/Analyzer/Features/Reporting/Application/AnalyzerReportingOptionsPostConfigurator.cs` implementing `IPostConfigureOptions<AnalyzerReportingOptions>` to default `IndividualDataUserGroupAlias` to `"Analytics.IndividualData"` when null/empty/whitespace. Ensures the fallback covered by test #6-8 of T012.
- [X] T010 [P] Role-gate interface + default impl: `src/Analyzer/Features/Reporting/Application/Authorization/IIndividualDataAccessCheck.cs` + `DefaultIndividualDataAccessCheck.cs` per `contracts/IIndividualDataAccessCheck.md`. Both `internal`. Default impl reads `IOptions<AnalyzerReportingOptions>` and uses `StringComparison.Ordinal` for the user-group alias compare (test #9 ensures case-sensitivity).
- [X] T011 Wire DI in `AnalyzerReportingComposer` (T004): `AddSingleton<IIndividualDataAccessCheck, DefaultIndividualDataAccessCheck>()`, `Configure<AnalyzerReportingOptions>(Constants.Configuration.ReportingSection)`, `AddSingleton<IPostConfigureOptions<AnalyzerReportingOptions>, AnalyzerReportingOptionsPostConfigurator>()`. T020 and T034 layer additional registrations on top.
- [X] T012 [P] Unit tests for `DefaultIndividualDataAccessCheck`: `src/Analyzer.Tests/Unit/Features/Reporting/Application/Authorization/DefaultIndividualDataAccessCheckTests.cs` covering all 10 scenarios in `contracts/IIndividualDataAccessCheck.md` § Test plan. Use `ClaimsPrincipal` constructed with `ClaimsIdentity` + `Claim(Umbraco.Cms.Core.Constants.Security.UserGroupClaimType, …)` per Umbraco's claim wiring.
- [X] T013 [P] Add `ContentAnalyticsSnapshot` to `src/Analyzer.Tests/Contracts/PublicSurfacePinningTests.cs` baseline. Confirm the diff is purely additive (no removed members from prior slices). The pinning manifest entries are listed in `contracts/ContentAnalyticsSnapshot.md` § Pinning entry.
- [X] T014 [P] Privacy invariant test: `src/Analyzer.Tests/Contracts/ContentAnalyticsSnapshot_PrivacyTests.cs` — reflection over `typeof(ContentAnalyticsSnapshot).GetProperties()`, assert no property name contains (case-insensitive) `upn`, `oid`, `email`, `identityref`, or `displayname`. Future slices that need to add a personally-identifying field MUST update this test and the SC-005 acceptance criteria.
- [X] T015 Regression gate: run `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --filter "Category!=Integration&Category!=Perf"`. Slice-007 baseline is 169/169 green; slice 008's foundational work adds ~12 unit tests (T012 + T014). Expect ~181 unit suite green. Failures here halt the slice.

**Checkpoint**: Foundation ready — US1 work can now begin.

---

## Phase 3: User Story 1 — Editor reviews a content node's usage at a glance (P1)

**Goal**: Editor opens any published content node, clicks the Analytics tab, sees five aggregate metrics (pageviews 24h/7d/30d, unique visitors 30d, avg time on page 30d) within the SC-001 budget.

**Independent Test**: With seeded pageviews + sessions for one content node, calling the management endpoint as a backoffice user returns a `ContentAnalyticsSnapshot` with the correct counts; window monotonicity holds (24h ≤ 7d ≤ 30d); navigating between three different nodes returns three distinct payloads. End-to-end: Vitest verifies the content-app element renders the populated state with the expected metric values when the repository's `fetch()` resolves.

### Server-side (Phase 3a)

- [X] T016 [US1] Repository interface: `src/Analyzer/Features/Reporting/Application/IContentAnalyticsRepository.cs`. Single method: `Task<ContentAnalyticsProjection?> GetAsync(Guid contentKey, DateTimeOffset windowEndUtc, CancellationToken ct)`. Null return reserved for the "GUID known to neither cache nor capture" 404 case (T020 enforces).
- [X] T017 [US1] Repository impl: `src/Analyzer/Features/Reporting/Infrastructure/ContentAnalyticsRepository.cs`. Uses `IScopeProvider` + NPoco. Single SQL query per research §R3 using `LAG(requestUtc) OVER (PARTITION BY analyzerSession.sessionKey ORDER BY requestUtc)`. Compute `pageviews24h` / `pageviews7d` / `pageviews30d` as `COUNT(CASE WHEN ...)` in one pass. `uniqueVisitors30d` = `COUNT(DISTINCT visitorProfileFk)`. `avgTimeOnPageSeconds30d` = `AVG(DATEDIFF(SECOND, prevRequestUtc, requestUtc))` — null when no qualifying rows. `hasAnyCaptureRow` = `COUNT(*) > 0` for the 30d window. **Critical**: NEVER reference `customizerVisitorProfile.identityRef` in the projection; SELECT only `visitorProfileFk` for the distinct count.
- [X] T018 [US1] Tombstone probe: `src/Analyzer/Features/Reporting/Infrastructure/PublishedContentTombstoneProbe.cs` per research §R4 + `contracts/AnalyzerContentAnalyticsManagementController.md`. Wraps `IPublishedContentCache.GetById(Guid)`; returns `bool isCurrentlyTombstoned = (cache.GetById(contentKey) == null)`. Internal interface `IPublishedContentTombstoneProbe` so the controller can be unit-tested with a mock.
- [X] T019 [US1] Query service: `src/Analyzer/Features/Reporting/Application/ContentAnalyticsQueryService.cs` implementing `IContentAnalyticsQueryService`. Orchestrates `IContentAnalyticsRepository.GetAsync` + `IPublishedContentTombstoneProbe.IsTombstoned`, composes a `ContentAnalyticsSnapshot`. Maps the projection's `hasAnyCaptureRow=false && isCurrentlyTombstoned=true (cache miss)` to "404 candidate" (returns null). All other combinations build the snapshot.
- [X] T020 [US1] Management controller: `src/Analyzer/Features/Reporting/Web/AnalyzerContentAnalyticsManagementController.cs` per `contracts/AnalyzerContentAnalyticsManagementController.md`. `[ApiController]`, `[ApiVersion("1.0")]`, `[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]`, route `umbraco/management/api/v1/analyzer/content-analytics`. Single `[HttpGet("{contentKey:guid}")]` action. Returns `Ok(snapshot)` or `NotFound(problemDetails)` with `Type` URL matching the contract. Adds `Cache-Control: no-store` response header.
- [X] T021 [US1] Wire DI: extend `AnalyzerReportingComposer` (T011) to register `IContentAnalyticsRepository → ContentAnalyticsRepository` (scoped), `IPublishedContentTombstoneProbe → PublishedContentTombstoneProbe` (scoped), `IContentAnalyticsQueryService → ContentAnalyticsQueryService` (scoped). Controllers auto-discover via Umbraco's API conventions.
- [ ] T022 [P] [US1] Repository unit tests: `src/Analyzer.Tests/Unit/Features/Reporting/Infrastructure/ContentAnalyticsRepositoryTests.cs`. **Deferred** — the repository's SQL uses `LAG()` + `DATEDIFF(SECOND, ...)`, both MSSQL-specific dialects SQLite cannot execute. Correctness is covered by T027/T028/T029 against Testcontainers MSSQL. The originally-planned SQLite-backed unit pattern doesn't fit this slice.
- [X] T023 [P] [US1] Tombstone probe unit tests: `src/Analyzer.Tests/Unit/Features/Reporting/Infrastructure/PublishedContentTombstoneProbeTests.cs`. Mock `IPublishedContentCache.GetById` returning null → probe returns true; returning a populated `IPublishedContent` → probe returns false.
- [X] T024 [P] [US1] Query service unit tests: `src/Analyzer.Tests/Unit/Features/Reporting/Application/ContentAnalyticsQueryServiceTests.cs`. Mocked `IContentAnalyticsRepository` + `IPublishedContentTombstoneProbe`. Assert: (a) both return null/false → query service returns null (404 candidate); (b) repo returns a projection with `hasAnyCaptureRow=false` AND tombstone=true → null (404); (c) repo returns projection with `hasAnyCaptureRow=true` AND tombstone=true → snapshot with `isContentCurrentlyTombstoned=true`; (d) windowEndUtc threaded from `TimeProvider` into the snapshot unchanged.
- [X] T025 [US1] Controller unit tests: `src/Analyzer.Tests/Unit/Features/Reporting/Web/AnalyzerContentAnalyticsManagementControllerTests.cs`. Mock `IContentAnalyticsQueryService`. Assert: (a) service returns snapshot → controller returns 200 with snapshot body + `Cache-Control: no-store`; (b) service returns null → controller returns 404 with `application/problem+json` body matching `contracts/AnalyzerContentAnalyticsManagementController.md` § Response (404 Not Found); (c) route GUID echoed into problem-details `contentKey` field.
- [X] T026 [US1] Integration test base: extend `src/Analyzer.Tests/Integration/AnalyzerIntegrationTestBase.cs` (slice-002+) with a `SeedAnonymisedVisitorProfileAsync` helper (visitor profile with `identityRef = "anonymized:" + Guid.NewGuid()`) if not already present from a prior slice. Confirm the existing `SeedVisitorProfileAsync` / `SeedPageviewAsync` / `SeedSessionAsync` helpers are usable as-is for slices 002-007.
- [X] T027 [US1] Integration test US1 — happy path: `src/Analyzer.Tests/Integration/Reporting/ContentAnalyticsEndToEndTests.cs::Returns200WithMonotonicCounts`. Testcontainers MSSQL. Seed 1 content node, 3 visitors, 1 session per visitor, 5 pageviews on the target node across the 30d window (some within 24h, some between 24h-7d, some between 7d-30d). Call the endpoint with `WebApplicationFactory<Program>` + faked EntraID claims (slice-007 pattern). Assert: 200, snapshot.pageviews24h ≤ pageviews7d ≤ pageviews30d, uniqueVisitors30d == 3, avgTimeOnPageSeconds30d > 0, JSON contains no identity field names.
- [X] T028 [P] [US1] Integration test US1 — cross-node isolation: `ContentAnalyticsEndToEndTests::CrossNodeBleedIsPrevented`. Seed pageviews on 3 different content nodes (A, B, C) with different visitor counts. Query each; assert each response carries node-specific counts (no aggregate bleed).
- [X] T029 [P] [US1] Integration test US1 — windowEndUtc consistency: `ContentAnalyticsEndToEndTests::WindowEndUtcMatchesFakedTime`. Inject `FakeTimeProvider`; advance to a known instant; assert the response's `windowEndUtc` equals the faked instant. Confirms `FR-RPT-008` (UTC-source) at the wire boundary.

### Client-side (Phase 3b — runs in parallel with Phase 3a tasks once T005/T007 are done)

- [X] T030 [P] [US1] Bundle manifest entry: extend `src/Analyzer/Client/public/umbraco-package.json` with the `Analyzer.ContentApp.ContentAnalytics` extension entry per `contracts/ContentAnalyticsContentApp.md` § Manifest registration. `kind: "contentApp"`, `conditions: [{ alias: "Umb.Condition.WorkspaceAlias", match: "Umb.Workspace.Document" }]`, `weight: 200`, `elementName: "analyzer-content-analytics-app"`, label "Analytics", icon "icon-chart-line".
- [X] T031 [P] [US1] TypeScript types: `src/Analyzer/Client/src/features/content-analytics/types.ts`. `interface ContentAnalyticsSnapshot { contentKey, windowEndUtc, pageviews24h, pageviews7d, pageviews30d, uniqueVisitors30d, avgTimeOnPageSeconds30d, isContentCurrentlyTombstoned, topReferrers30d }` mirroring the server DTO. Plus an `interface ContentAnalyticsError { status: number; title?: string }` for the error state.
- [X] T032 [P] [US1] Repository / fetcher: `src/Analyzer/Client/src/features/content-analytics/content-analytics.repository.ts`. Exports `fetchContentAnalytics(contentKey: string): Promise<ContentAnalyticsSnapshot>`. Calls `GET /umbraco/management/api/v1/analyzer/content-analytics/{contentKey}`. Throws `ContentAnalyticsError` on non-2xx with the parsed problem-details `status` + `title`. Reuses anti-forgery cookie convention (XSRF cookie/header pair) per slice 004-007 client modules.
- [X] T033 [P] [US1] Formatters: `src/Analyzer/Client/src/features/content-analytics/formatters.ts`. `formatNumber(n: number): string` using `Intl.NumberFormat()` (thousands separator). `formatDurationSeconds(s: number | null): string` → `null` returns `"—"`; `< 60` returns `"Xs"`; otherwise `"Xm Ys"`.
- [X] T034 [P] [US1] Vitest tests for formatters: `src/Analyzer/Client/src/features/content-analytics/__tests__/formatters.spec.ts`. Cases: 0 → "0", 12345 → "12,345", 92 (seconds) → "1m 32s", 59 → "59s", null → "—", 3600 → "60m 0s".
- [X] T035 [P] [US1] Skeleton element: `src/Analyzer/Client/src/features/content-analytics/skeleton.element.ts`. Lit element `analyzer-content-analytics-skeleton` rendering a CSS grid of 5 placeholder rectangles with a shimmer animation. CSS `@keyframes shimmer-pulse` + `@media (prefers-reduced-motion: reduce)` static fallback.
- [X] T036 [US1] Content-app element: `src/Analyzer/Client/src/features/content-analytics/content-app.element.ts`. Lit element registered as `analyzer-content-analytics-app`. State machine: `loading` → (`populated` | `empty` | `error`). Lifecycle: `connectedCallback` reads the active content node's GUID via `UmbDocumentWorkspaceContext` (`@umbraco-cms/backoffice/document`), calls `fetchContentAnalytics`, sets state on resolve/reject. Renders five metric blocks per `contracts/ContentAnalyticsContentApp.md` § State 2 / 3 / 4. Sets `aria-busy="true"` while loading. Reduced-motion supported.
- [X] T037 [US1] Vitest tests for content-app element — populated state: `src/Analyzer/Client/src/features/content-analytics/__tests__/content-app.element.spec.ts::renders_populated_state`. Mock the repository to resolve with a known snapshot; render the element; assert all 5 metric blocks present with formatted numbers, `aria-busy="false"`, no skeleton, no error state.
- [X] T038 [US1] Vitest test — loading state: `content-app.element.spec.ts::renders_loading_state`. Repository promise pending; assert `aria-busy="true"`, 5 skeleton elements present, no metric numbers.
- [X] T039 [US1] Register content-app element in the bundle entrypoint: `src/Analyzer/Client/src/index.ts`. Import `./features/content-analytics/content-app.element` so the custom-element registration runs when the bundle loads. Mirrors slices 005/006/007 module-registration pattern.

**Checkpoint**: At this point, US1 is a complete vertical slice — editors can view aggregate analytics on any content node with historical data. MVP shippable here.

---

## Phase 4: User Story 2 — Freshly published or never-viewed content shows zero gracefully (P2)

**Goal**: Brand-new content node opens its Analytics tab and shows a clean zeros-with-headline state, not an error.

**Independent Test**: Querying the endpoint for a content node that has zero pageviews but does exist in the published content cache returns HTTP 200 with all metric fields at 0 / null; the content-app element renders the empty-state copy without an error banner.

- [X] T040 [P] [US2] Server-side: confirm `ContentAnalyticsQueryService` (T019) returns a snapshot (not null) when the projection has `hasAnyCaptureRow=false` AND the tombstone probe returns false (cache hit). Add explicit unit test `ContentAnalyticsQueryServiceTests::PublishedContentWithNoCapturesReturnsSnapshotWithZeros`.
- [ ] T041 [P] [US2] Integration test US2: `ContentAnalyticsEndToEndTests::EmptyContentReturns200WithZeros`. Seed a content node in the Umbraco cache (via test fixture content-type + node creation) but no pageviews. Call the endpoint; assert: 200, pageviews24h/7d/30d all = 0, uniqueVisitors30d = 0, avgTimeOnPageSeconds30d = null, isContentCurrentlyTombstoned = false, topReferrers30d = [].
- [X] T042 [US2] Client-side: extend `content-app.element.ts` (T036) with empty-state rendering — when all four metric counts are 0 AND tombstone is false, render the "No activity in the last 30 days" headline + sub-copy from `contracts/ContentAnalyticsContentApp.md` § State 3, but still render the metric blocks with `0` / `—` values so layout doesn't shift on next-load.
- [X] T043 [P] [US2] Vitest test: `content-app.element.spec.ts::renders_empty_state`. Repository resolves with all-zero snapshot; assert empty-state headline present, 5 metric blocks render with `0` / `—`, no error banner, `aria-busy="false"`.

**Checkpoint**: US2 layered on top of US1. Together they cover the "node has data" and "node has no data" cases without an error path.

---

## Phase 5: User Story 3 — Anonymisation-preserved unique visitor count (P3)

**Goal**: Anonymising a subset of visitors via Customizer's cascade does NOT reduce the historical `uniqueVisitors30d` count, and the response payload never contains the anonymised identifier.

**Independent Test**: With 10 visitors on a content node, anonymise 3, query the endpoint, assert `uniqueVisitors30d` is still 10 and the JSON contains no `identityRef`-style field.

- [X] T044 [US3] Integration test US3 — anonymisation preserves count: `src/Analyzer.Tests/Integration/Reporting/AnonymisedVisitorAggregateTests.cs::AnonymisationDoesNotReduceUniqueVisitors`. Seed 10 visitors, 1 pageview each on the target node. Run Customizer's anonymisation cascade for 3 of the 10 (via the visitor-profile store, which re-keys `identityRef` to `anonymized:<guid>` without touching `customizerVisitorProfile.key` or `customizerVisitorPageview.visitorProfileFk`). Call the endpoint. Assert `uniqueVisitors30d == 10`.
- [X] T045 [P] [US3] Integration test US3 — privacy assertion: `AnonymisedVisitorAggregateTests::ResponseContainsNoIdentityFields`. Run the same seed + anonymise flow; parse the response JSON; assert the body does not contain (case-insensitive substring) `upn`, `oid`, `userEmail`, or `identityRef`. Reuses the privacy invariant T014 but at the wire boundary not the type boundary.
- [X] T046 [P] [US3] SQL-projection audit test: `src/Analyzer.Tests/Unit/Features/Reporting/Infrastructure/ContentAnalyticsRepositorySqlAuditTests.cs`. Reflectively inspect the SQL string the repository builds; assert it does NOT reference `identityRef` (case-insensitive). Defends against a future refactor accidentally JOINing through `customizerVisitorProfile.identityRef`.

**Checkpoint**: US3 layered on top of US1 + US2. The slice now meets all three user stories and SC-004 + SC-005.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, performance budgets, accessibility, content-app remaining states (tombstone banner + error), housekeeping. Each task here is independent of the others — phase can run in parallel.

- [X] T047 [P] Integration test — unknown content GUID: `ContentAnalyticsEndToEndTests::UnknownContentKeyReturns404`. Query an unseeded GUID; assert HTTP 404 with `application/problem+json` body matching the contract.
- [ ] T048 [P] Integration test — tombstoned content: `ContentAnalyticsEndToEndTests::TombstonedContentReturns200WithFlag`. Seed pageviews, then "delete" the content (move to recycle bin via `IContentService.MoveToRecycleBin` in the test fixture). Query; assert HTTP 200, `isContentCurrentlyTombstoned: true`, historical aggregates unchanged.
- [ ] T049 [P] Performance test — SC-001 budget: `src/Analyzer.Tests/Perf/ContentAnalyticsPerfTests.cs::Within2SecondsForTenThousandPageviews`. Seed 10,000 pageviews across 100 visitors / 1000 sessions on one content node within the 30d window. Time the full endpoint round-trip; assert `Stopwatch.ElapsedMilliseconds < 2000`. `[Trait("Category", "Perf")]`.
- [ ] T050 [P] Performance test — SC-002 budget: `ContentAnalyticsPerfTests::Within5SecondsForHundredThousandPageviews`. 100k pageviews; assert < 5000ms. May be skipped in default CI runs (uses `Skip = "explicit-only"` attribute or `Category=Perf` filter).
- [X] T051 [P] Vitest test — tombstone banner: `content-app.element.spec.ts::renders_tombstone_banner_when_currently_tombstoned`. Repository resolves with `isContentCurrentlyTombstoned: true`; assert `<uui-tag color="warning">` present with banner copy.
- [X] T052 [P] Vitest test — error state with 404: `content-app.element.spec.ts::renders_error_state_on_404`. Repository rejects with `ContentAnalyticsError({ status: 404, title: "Content node not found" })`; assert error headline + status code present, retry button present.
- [X] T053 [P] Vitest test — error state with 500: `content-app.element.spec.ts::renders_error_state_on_500`. Repository rejects with `status: 500`; assert generic error copy + retry button, no stack trace in DOM.
- [X] T054 [P] Vitest test — retry button triggers refetch: `content-app.element.spec.ts::retry_button_refetches`. Repository mock that rejects first call, resolves second. Click retry; assert mock called twice; element transitions to populated.
- [X] T055 [P] Vitest test — unmount during in-flight: `content-app.element.spec.ts::unmount_does_not_leak_promise_rejection`. Mount the element, leave fetch pending, unmount via `disconnectedCallback`. Use `vi.spyOn(console, 'error')` to assert no unhandled rejection logged.
- [X] T056 [P] Vitest test — reduced motion: `skeleton.element.spec.ts::reduced_motion_disables_animation`. Stub `window.matchMedia('(prefers-reduced-motion: reduce)')` returning `{ matches: true }`; render skeleton; assert the shimmer animation rule is overridden (CSS-only assertion).
- [X] T057 Accessibility audit: manual checklist applied to `content-app.element.ts` source — `<dl>/<dt>/<dd>` structure for metrics, `aria-busy` flips with state, focus order: tombstone-banner → metric blocks → retry-button (when in error state). Document the checklist results in `specs/008-content-analytics-app/checklists/accessibility.md`.
- [ ] T058 Localise copy strings: import every user-facing string in `content-app.element.ts` (and `skeleton.element.ts` if any) via `@umbraco-cms/backoffice/localization`'s `localize.term('analyzer_contentAnalytics_*')` pattern. Add the English strings to `src/Analyzer/Client/src/lang/en.ts` (create if missing per slice-005/006 precedent). Future locales can override without touching the element source.
- [X] T059 Update `feature-summary.md` for slice 008 at `docs/feature-summary.md` (or wherever the per-slice summary lives in this repo — check slice 007's PR for precedent). Brief description of the new content-app surface, the new public DTO, the new internal role-gate primitive, and the explicit `slice-007-followups #34` deferral for manual quickstart validation.
- [X] T060 Update HANDOVER (or equivalent) for slice 008 closeout per slice-007 convention. Note the bundle-size impact, the new public DTO entry in `PublicSurfacePinningTests`, and the integration-test count delta from the slice-007 baseline.
- [X] T061 Run full test suite: `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "Category!=Perf"` PLUS `cd src/Analyzer/Client && npm run test`. Confirm zero failures. Targets: ~210 unit + integration green, ~80 Vitest green. Acceptable variance ±5 depending on test fixture detail.
- [X] T062 Final pre-PR check: `dotnet build Analyzer.slnx` clean; `cd src/Analyzer/Client && npm run build` produces a bundle under the slice-007 baseline + reasonable delta (the content-app module adds ~3-4 kB minified, ~1 kB gzipped). Update slice-007 bundle-baseline assertion (if pinned in any test) to slice-008's new floor.

---

## Dependencies

```
Phase 1 (T001-T004) ──┐
                      ↓
Phase 2 (T005-T015) ──┬──→ Phase 3 (T016-T039) [US1 — MVP shippable here]
                      │       ├── 3a server (T016-T029) ──┐
                      │       └── 3b client (T030-T039) ──┴──→ Phase 4 (T040-T043) [US2]
                      │                                         │
                      │                                         ↓
                      └─────────────────────────────────────→ Phase 5 (T044-T046) [US3]
                                                                ↓
                                                            Phase 6 (T047-T062) [Polish]
```

- US2 depends on US1 (re-uses the controller + query service + content-app element infrastructure).
- US3 depends on US1 (re-uses the same endpoint; only adds new assertions against an existing flow).
- Polish phase can begin once US1 ships; doesn't block US2 / US3.

## Parallel Opportunities

- **Phase 2** is heavily parallelisable: T005, T006, T007, T008, T009, T010, T012, T013, T014 all touch different files and can run concurrently.
- **Phase 3a server** is parallelisable across the unit-test tasks T022 / T023 / T024 once T016-T020 are done.
- **Phase 3b client** parallelisable across T030 / T031 / T032 / T033 / T034 / T035; T036-T038 sequentialise around the element file.
- **Phase 6** has 11 [P] tasks (T047-T056) that can run concurrently. T057-T062 sequentialise around housekeeping artifacts (one feature-summary file, one HANDOVER, one suite run).
- **Server + Client** (Phase 3a vs 3b) run in parallel after T005/T007 land, since they share only the DTO contract.

## Independent Test Criteria

- **US1**: `dotnet test --filter "FullyQualifiedName~ContentAnalyticsEndToEndTests"` + `npm run test -- content-analytics` ⇒ green, including window-monotonicity + cross-node + populated-state Vitest tests.
- **US2**: `dotnet test --filter "FullyQualifiedName~EmptyContentReturns200WithZeros"` + Vitest empty-state test ⇒ green.
- **US3**: `dotnet test --filter "FullyQualifiedName~AnonymisedVisitorAggregateTests"` + SQL audit unit test ⇒ green.
- Full slice: T061 + T062 plus the PublicSurfacePinningTests additive diff approved.

## Implementation Strategy

1. **MVP shippable**: Phase 1 + Phase 2 + Phase 3 (T001-T039). All three user stories build on this. US1 alone is a viable demo.
2. **Incremental delivery**: ship US1 as the first PR; US2 and US3 layer on as follow-up PRs against the same slice branch if scope splitting helps review (or ship them together if the slice is small enough).
3. **Defer until #34 lands**: do NOT block the slice on manual quickstart validation. Polish phase T061 + T062 are the canonical green-light gate.
4. **Parallel paths**: Phase 3a (server) and Phase 3b (client) can be worked simultaneously by two contributors after Phase 2 completes.

## MVP Suggested Scope

For a first shippable cut: complete tasks T001-T039 (Setup + Foundational + US1). This produces a working Analytics tab on every content node showing the five aggregate metrics for nodes with historical data. US2 (empty-state graceful) and US3 (anonymisation preserved) are layered on subsequent commits or PRs but are not blocking for a P1 demo.
