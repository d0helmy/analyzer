---

description: "Task list for slice 006 — Scroll Tracking"
---

# Tasks: Scroll Tracking

**Input**: Design documents from `/specs/006-scroll-tracking/`

**Prerequisites**: plan.md (✓), spec.md (✓), research.md (✓), data-model.md (✓), contracts/ (✓), quickstart.md (✓)

**Tests**: included — slice-004/005 precedent (unit + integration coverage on every public domain rule, handler, cascade step, repository, and management endpoint).

**Organization**: Tasks grouped by user story to enable independent implementation. MVP scope is Phase 1 + Phase 2 + Phase 3 (US1). US2 layers on additively.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1 / US2); Setup / Foundational / Polish have no story label.

## Path Conventions

Single project (RCL package per Constitution Tech Stack):
- Server: `src/Analyzer/`, tests at `src/Analyzer.Tests/`
- Client: `src/Analyzer/Client/src/`
- Host sample: `samples/Analyzer.Host/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Declare the new table constant and create the slice-006 feature folder skeleton. No new package dependency (in contrast to slice 005's Umbraco.Forms add).

- [X] T001 Add `Constants.Database.AnalyzerScrollSample = "analyzerScrollSample"` to `src/Analyzer/Constants.cs`. Update the XML docs to mirror slice-004's `AnalyzerCustomEvent` precedent.
- [X] T002 [P] Create the Features/Scroll feature folder skeleton at `src/Analyzer/Features/Scroll/{Application,Domain,Infrastructure,Web}/`. Add `Features/Scroll/Application/Anonymization/` and `Features/Scroll/Infrastructure/Persistence/` subfolders. Mirrors slice-004's `Features/CustomEvents/` layout.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Table, public records, state-store wiring, and shared abstractions that EVERY user-story phase consumes.

**⚠️ CRITICAL**: No US1 / US2 work can begin until this phase is complete.

- [X] T003 [P] DTO: `src/Analyzer/Features/Scroll/Infrastructure/Persistence/AnalyzerScrollSampleDto.cs` — NPoco DTO per data-model §2.1. `[TableName(Constants.Database.AnalyzerScrollSample)]`, `[PrimaryKey(nameof(Id), AutoIncrement = false)]`, all columns from data-model §1.1 with attributes (`[Column]`, `[Index]`, `[NullSetting]`).
- [X] T004 [P] Migration: `src/Analyzer/Migrations/M0006_AddAnalyzerScrollSampleTable.cs` per data-model §3.1. Idempotent via `TableExists` guard. SQL Server branch: raw-SQL FK to `customizerVisitorProfile(key)`, CHECK constraint `CK_analyzerScrollSample_bucket IN (25, 50, 75, 100)`, and the composite unique index `UX_analyzerScrollSample_pageviewBucket` on `(pageviewKey, bucket)`. SQLite branch: table only (no FK / no extra indexes), matching slices 002/004/005.
- [X] T005 Chain `M0006` into `AnalyzerMigrationPlan` after `M0005` (file: `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs`). Confirms slice-006 migration runs on host boot.
- [X] T006 [P] Public records: `src/Analyzer/Analytics/AnalyticsScrollSample.cs` + `src/Analyzer/Analytics/AnalyzerScrollBucket.cs` per data-model §4. Init-only required props on the record; byte-backed enum with `Quarter=25, Half=50, ThreeQuarters=75, Full=100`.
- [X] T007 [P] Domain command: `src/Analyzer/Features/Scroll/Domain/AnalyzerScrollEventCapture.cs` per contracts/IAnalyzerScrollEventCaptureHandler.md (Actor, PageviewKey, ContentKey, Bucket, ReceivedUtc, optional UserAgent).
- [X] T008 [P] Validation exception: `src/Analyzer/Features/Scroll/Domain/AnalyzerScrollPayloadValidationException.cs` — derived from slice-004/005 exception pattern; carries property-name + validator-message slot.
- [X] T009 [P] Duplicate exception: `src/Analyzer/Features/Scroll/Domain/ScrollSampleDuplicateException.cs` — thrown by the repository when the `UX_analyzerScrollSample_pageviewBucket` unique index rejects an insert. Carries `PageviewKey` + `Bucket`. Maps to HTTP 409 at the controller (per contracts/AnalyzerScrollEventManagementController.md status-code matrix).
- [X] T010 Extend `Analyzer.Features.Sessions.Application.SessionActivityKind` enum (slice 003 / 005) with `ScrollEvent = 3` value (additive — next-available after slice-005's `FormImpression`). The resolver's `ResolveAsync` MUST dispatch `ScrollEvent` → `TouchAsync` (intentional engagement, parity with `CustomEvent`). Confirm slice-003/004/005 callers unchanged.
- [X] T011 Extend `AnalyzerSessionResolver` (slice 003) at `src/Analyzer/Features/Sessions/Application/AnalyzerSessionResolver.cs` to handle `SessionActivityKind.ScrollEvent` via the existing touch branch. Add unit cases at `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.cs` (additive: `ScrollEvent_TouchesLastActivity`).
- [X] T012 [P] Extend `IAnalyticsEventStateProvider` interface at `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` with `CurrentRequestScrollEvents : IReadOnlyList<AnalyticsScrollSample>` (data-model §4.3). Documented as additive (slice-006 lineage).
- [X] T013 Extend `AnalyticsEventStateStore` at `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` with `AppendScrollEvent(...)` and a read-only accumulator backing the new interface member. Update slice-002/004/005 unit tests to assert the new accumulator is empty on store creation.
- [X] T014 Regression gate: run `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --filter "Category!=Integration&Category!=Perf"`. Confirm 117/117 unit suite (slice-005 baseline) still green after the additive resolver / state-store changes. Failures here halt the slice.

**Checkpoint**: Foundation ready — US1 work can now begin.

---

## Phase 3: User Story 1 — Scroll-depth milestones (Priority: P1) 🎯 MVP

**Goal**: Capture one row per milestone crossing (25 / 50 / 75 / 100 %) per `(visitorKey, pageviewKey, contentKey)` and expose them via `IAnalyticsEventStateProvider.CurrentRequestScrollEvents`. Surface the management endpoint at `/umbraco/management/api/v1/analyzer/scroll-event/milestone` with the Principle-VII four-corner gate. Enforce idempotency via the DB unique index. Register the hard-delete cascade step.

**Independent Test**: Per quickstart §1 — load a tall page as an authenticated employee, scroll top-to-bottom, query DB for the 4 milestone rows. Replay a milestone POST → 409, row count unchanged.

- [X] T015 [P] [US1] Repo interface: `src/Analyzer/Features/Scroll/Infrastructure/Persistence/IAnalyzerScrollSampleRepository.cs` — `InsertAsync(AnalyzerScrollSampleDto, CancellationToken)`, `DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken)`, `CountByVisitorAsync(Guid visitorProfileKey, CancellationToken)` (for perf-smoke verification).
- [X] T016 [US1] Repo impl: `src/Analyzer/Features/Scroll/Infrastructure/Persistence/AnalyzerScrollSampleRepository.cs` — uses `IScopeProvider` + NPoco. `InsertAsync` catches `SqlException.Number IN (2601, 2627)` for the `UX_analyzerScrollSample_pageviewBucket` constraint (slice-003's `UniqueConstraintViolationDetector` discriminates) and re-throws `ScrollSampleDuplicateException`. Other UX violations (eventKey collision) re-throw the original.
- [ ] T017 [P] [US1] **DEFERRED** — Repo unit tests at `src/Analyzer.Tests/Unit/Features/Scroll/Infrastructure/AnalyzerScrollSampleRepositoryTests.cs`. Mocking `IUmbracoDatabase` adds no incremental value over the integration tests `IdempotencyTests` + `CascadeHardDeleteTests`, which exercise the real repo against Testcontainers MS SQL (matches slice-005 precedent — `AnalyzerFormEventRepositoryTests` was also left deferred). Re-evaluate only if an internal-method-only bug demands unit-level shape.
- [X] T018 [P] [US1] Handler interface: `src/Analyzer/Features/Scroll/Application/IAnalyzerScrollEventCaptureHandler.cs` per contracts/IAnalyzerScrollEventCaptureHandler.md.
- [X] T019 [US1] Handler impl: `src/Analyzer/Features/Scroll/Application/AnalyzerScrollEventCaptureHandler.cs` — identity gate → payload validation → session resolution (`SessionActivityKind.ScrollEvent`) → repo insert → state-store append → audit emit → return `EventKey`. On `ScrollSampleDuplicateException`: do NOT append to state-store; invoke auditor with `Duplicate` tag; re-throw for controller to map to 409.
- [X] T020 [P] [US1] Handler unit tests: `src/Analyzer.Tests/Unit/Features/Scroll/Application/AnalyzerScrollEventCaptureHandlerTests.cs` covering the 6 conformance items in the contract doc (RejectsUnavailableActor, RejectsEmptyVisitorKey, RejectsInvalidBucket, RejectsEmptyPageviewKey, HappyPathInsertsAndAppendsState, DuplicateRowAuditedAsDuplicateAndStateNotAppended).
- [X] T021 [P] [US1] Auditor: `src/Analyzer/Features/Scroll/Application/IAnalyzerScrollEventAuditor.cs` + `AnalyzerScrollEventAuditor.cs` impl (ILogger-backed; structured log scope per research §R8 carrying `EventKey, PageviewKey, Bucket, ActorUpn, ReceivedUtc`; second `Duplicate`-tagged overload for the 409 path).
- [X] T022 [P] [US1] Auditor unit tests: `src/Analyzer.Tests/Unit/Features/Scroll/Application/AnalyzerScrollEventAuditorTests.cs` — assert log scope shape on the success path and on the `Duplicate` path; assert nothing is logged on validation/identity failure.
- [X] T023 [P] [US1] Management controller: `src/Analyzer/Features/Scroll/Web/AnalyzerScrollEventManagementController.cs` + `AnalyzerScrollEventPayload.cs` per contracts/AnalyzerScrollEventManagementController.md. Route `POST /umbraco/management/api/v1/analyzer/scroll-event/milestone`. Principle-VII four-corner gate via `[Authorize(Policy = "BackOffice")]` + anti-forgery convention. Map `ScrollSampleDuplicateException` to 409 `{ "code": "duplicate" }`; map `AnalyzerScrollPayloadValidationException` to 400 problem-details; map `UnauthorizedAccessException` to 401/403.
- [X] T024 [P] [US1] Controller unit tests: `src/Analyzer.Tests/Unit/Features/Scroll/Web/AnalyzerScrollEventManagementControllerTests.cs` (HappyPathReturns202WithEventKey, RejectsEmptyPageviewKeyWith400, RejectsInvalidBucketWith400, DuplicateReturns409WithCode, AnonymousReturns401).
- [X] T025 [US1] Composer: `src/Analyzer/Composers/AnalyzerScrollComposer.cs` registers `IAnalyzerScrollSampleRepository` (Scoped), `IAnalyzerScrollEventCaptureHandler` (Scoped), `IAnalyzerScrollEventAuditor` (Singleton), management controller (auto-registered by Umbraco), cascade step via `AnonymizationCascadeStepCollectionBuilder.Append<AnalyzerScrollSampleCascadeStep>()` (Transient — matches slice 005 cascade-step lifetime).
- [X] T026 [P] [US1] Cascade step: `src/Analyzer/Features/Scroll/Application/Anonymization/AnalyzerScrollSampleCascadeStep.cs` implements `IAnonymizationCascadeStep` per contracts/AnalyzerScrollSampleCascadeStep.md. Single-statement DELETE via repo's `DeleteByVisitorAsync`. Hard-delete participation pattern per Principle IV + research §R10. `Order` = next-available after slice-005's two cascade steps.
- [X] T027 [P] [US1] Cascade step unit tests: `src/Analyzer.Tests/Unit/Features/Scroll/Application/AnalyzerScrollSampleCascadeStepTests.cs` (ZeroRowsNoOp, HundredRowsDeletedOnce, RepoThrowsBubbles).
- [X] T028 [P] [US1] Client module: `src/Analyzer/Client/src/features/scroll-tracking/scroll-event-dispatcher.ts` — POST helper for the `/scroll-event/milestone` route. `fetch` with `credentials: 'same-origin'`, `keepalive: true`, anti-forgery header. Handles 202 success silently; surfaces non-2xx via `console.warn` (best-effort capture, never block the page).
- [X] T029 [P] [US1] Client module: `src/Analyzer/Client/src/features/scroll-tracking/milestone-tracker.ts` — pure-function `crossingDetector` per research §R2: takes `(percent: number, crossed: Set<ScrollBucket>)`, returns `ScrollBucket[]` of newly-crossed buckets. Adds them to the set as a side-effect.
- [X] T030 [P] [US1] Client module: `src/Analyzer/Client/src/features/scroll-tracking/short-page-detector.ts` — per research §R3: detects `scrollableHeight <= 0`; when true, returns the full bucket set `{25, 50, 75, 100}` to pre-mark as crossed (caller emits ONLY the 100 bucket immediately).
- [X] T031 [P] [US1] Client module: `src/Analyzer/Client/src/features/scroll-tracking/scroll-observer.ts` — passive `window.addEventListener('scroll', _, { passive: true })` + `requestAnimationFrame` coalescer per research §R1. On each rAF, reads scroll position, derives `percent`, invokes `crossingDetector`, dispatches POST for each newly-crossed bucket. Per-pageview closure state holds `crossed: Set<ScrollBucket>` and `rafQueued: boolean`.
- [X] T032 [P] [US1] Client module: `src/Analyzer/Client/src/features/scroll-tracking/index.ts` — exports `initScrollTracking(opts)` entrypoint per contracts/AnalyzerScrollObserver.md. Reads `window.analyzer.pageviewKey` + `window.analyzer.contentKey`; warns + returns if `pageviewKey` is missing/empty (Edge Case); invokes short-page-detector and dispatches 100 % immediately when applicable; otherwise installs the scroll listener.
- [X] T033 [US1] Wire scroll-tracking into the bundle: extend `src/Analyzer/Client/src/analyzer-bundle.ts` to import and initialise the scroll-tracking module after `DOMContentLoaded`, AFTER the slice-005 forms-tracking init (sequential init keeps the per-module opt-out check ordering deterministic). Add `npm run build` verification step.
- [X] T034 [P] [US1] Vitest unit tests: `src/Analyzer/Client/src/features/scroll-tracking/*.test.ts` covering the 4 conformance items from contracts/AnalyzerScrollObserver.md (milestone-crossing in order; back-scroll produces no fetch; rAF coalescing; anti-forgery header on every dispatch). Use the slice-004 Vitest harness (existing `vitest.config.ts`).
- [X] T035 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Scroll/EndToEndCaptureTests.cs` — POST 4 milestones in order → 4 rows in `analyzerScrollSample`; multi-visitor disjoint rows; cross-visitor rows untouched. Uses `SeedVisitorProfileAsync` (issue-#20 helper) + slice-003 `SeedPageviewAsync` helper.
- [X] T036 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Scroll/IdempotencyTests.cs` — replay a `(pageviewKey, bucket)` POST → second call returns 409; row count remains 1 per tuple; structured log shows the `Duplicate` tag. Stress run: 100 simultaneous POSTs for the same `(pageviewKey, bucket=25)` → exactly one row (DB-enforced).
- [X] T037 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Scroll/CascadeHardDeleteTests.cs` — `DeletesTargetVisitorOnly`, `CompletesUnderTwoHundredMsForOneThousandRows` (SC-004), `ZeroRowNoOp`.
- [X] T038 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Scroll/CascadeRollbackTests.cs` — `Throw_after_scroll_step_rolls_back_the_delete` (slice-002/004/005 precedent — register a sentinel `IAnonymizationCascadeStep` that throws after the scroll step; assert rows remain).

**Checkpoint**: US1 MVP complete — milestones captured via the management endpoint; DB-enforced idempotency holds; cascade hard-delete works. Slice is independently shippable here.

---

## Phase 4: User Story 2 — Opt-out (Priority: P2)

**Goal**: Wire the slice-005 `analyzer-no-tracking` attribute into the scroll module so it short-circuits at handler-init time. Extract the opt-out predicate into a shared module so both forms-tracking (slice 005) and scroll-tracking (slice 006) consume the same code (research §R4).

**Independent Test**: Per quickstart §2 — render a page with `<body analyzer-no-tracking>`, scroll top-to-bottom, assert zero POSTs in DevTools + zero rows in DB.

- [ ] T039 [P] [US2] Extract shared opt-out predicate: create `src/Analyzer/Client/src/shared/is-opted-out.ts` exporting `isOptedOut(): boolean` that returns `true` when `analyzer-no-tracking` is present on `<html>`, `<body>`, or `document.documentElement`. Logic moved verbatim from slice-005's `forms-tracking/opt-out-attribute.ts`.
- [ ] T040 [P] [US2] Update slice-005 forms-tracking imports: change `forms-tracking/index.ts` (and any internal callers) to import `isOptedOut` from `shared/is-opted-out.ts`. Delete the old `forms-tracking/opt-out-attribute.ts`. Verify slice-005 Vitest suite still passes (zero behaviour change).
- [ ] T041 [US2] Wire `isOptedOut()` into `scroll-tracking/index.ts` at init-time (first check before pageviewKey resolution): if `true`, log a debug message and `return` — no listener installed, no fetch fired, no state allocated.
- [ ] T042 [P] [US2] Vitest test: `scroll-tracking/opt-out.test.ts` — stub `isOptedOut()` to return `true`, init the module, simulate scroll events, assert `fetch` was never called and no scroll listener was attached.
- [ ] T043 [P] [US2] Integration test: `src/Analyzer.Tests/Integration/Scroll/OptOutComplianceTests.cs` — render a synthetic page via the test host with `<body analyzer-no-tracking>`, drive the scroll observer through 100 milestone-crossings, assert zero `analyzerScrollSample` rows + zero audit-log entries.

**Checkpoint**: US2 complete — opt-out attribute respected at the client boundary; defence-in-depth confirmed (no server-side opt-out logic needed because the client never POSTs).

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Public-surface pinning, perf-smoke baselines, additional success-criteria verification, and post-merge housekeeping.

- [ ] T044 [P] Public-surface pinning baseline regeneration: re-run the pinning-baseline regenerator helper to include `Analyzer.Analytics.AnalyticsScrollSample`, `Analyzer.Analytics.AnalyzerScrollBucket`, and the new `IAnalyticsEventStateProvider.CurrentRequestScrollEvents` member. Diff MUST be additive only (no existing member removed/renamed). Update `src/Analyzer.Tests/PublicSurfacePinning/baseline.txt` (or equivalent).
- [ ] T045 [P] Perf-smoke test: `src/Analyzer.Tests/Perf/ScrollEventCaptureLatencySmoke.cs` — 200 events/min sustained for 60 s synthetic load; assert P99 server-side persistence < 1 s (SC-001). `[Trait("Category", "Perf")]` opt-in trait (matches slice 002/003/004/005).
- [ ] T046 [P] Perf-smoke test: `src/Analyzer.Tests/Perf/ScrollCascadeThroughputSmoke.cs` — insert 1 000 rows for one visitor; assert cascade-step DELETE completes in ≤ 200 ms (SC-004). `[Trait("Category", "Perf")]`.
- [ ] T047 [P] Perf-smoke test (client overhead): `src/Analyzer/Client/src/features/scroll-tracking/scroll-observer.fcp.test.ts` — Vitest with Playwright trace driving a synthetic 5 000 px-tall page; assert FCP delta with the scroll module loaded is ≤ 5 ms vs the slice-005 baseline (SC-006). If the Playwright trace harness is not yet wired into the existing Vitest config, prefer to gate this task on a one-task harness extension (T047a) and keep the assertion target intact. Trait/category opt-in matches the server-side perf-smoke pattern.
- [ ] T048 [P] Audit-log fidelity test: `src/Analyzer.Tests/Integration/Scroll/AuditLogFidelityTests.cs` — verify one structured log entry per accepted row (SC-007). Captures the log sink, drives N captures, asserts the count matches the row count and each entry carries `EventKey, PageviewKey, Bucket, ActorUpn, ReceivedUtc`.
- [ ] T049 [P] Identity-gate test: `src/Analyzer.Tests/Integration/Scroll/IdentityGateTests.cs` — anonymous POST → 401; backoffice-auth POST with `IVisitorIdentifier.IsAvailable=false` → 403; both cases assert zero rows persisted + zero audit entries (SC-005).
- [ ] T050 Run quickstart.md walkthrough end-to-end on a freshly-built Aspire AppHost. Documents the manual-verification pass (used by reviewers + future agents). Capture results in the PR description; if any step fails, file a follow-up issue and tag it `slice-006-followup`.
- [ ] T051 Post-merge housekeeping (after PR merges to `main`): reset the CLAUDE.md SPECKIT block back to its "Last shipped" form pointing at slice 006 (`5e868ef` → new slice-006 head commit), matching the slice 002/003/004/005 cadence. Move project board #7 item for issue #25 (or its slice-006 equivalent) to Status=Done.

---

## Deferred items (slice-scoped, ship with this PR's "Known limitations")

The following deferred items mirror slice 004 + 005 precedent and are NOT blockers for slice 006 ship:

- **HTTP-boundary integration test for the management endpoint** (would belong in `EndToEndCaptureTests.cs` as a `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`-driven case): gated on issue #23 (mgmt-API 404 in the WAF test host). Slices 004 + 005 left the same gap. When #23 lands, add a single test that POSTs via the WAF client and asserts the round-trip.
- **SPA / dynamic-route scroll handling**: out of scope for v1 per spec Assumptions. If/when the platform adopts an SPA shell, this slice's `scroll-observer` will need a SPA route-change listener to reset `crossed` state. Track as `slice-006-followup` after ship if any host needs it.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **US1 (Phase 3)**: Depends on Foundational; everything inside US1 is independently shippable as the slice MVP.
- **US2 (Phase 4)**: Depends on Foundational + on slice 005's `analyzer-no-tracking` opt-out predicate existing in `forms-tracking/opt-out-attribute.ts` (it does — shipped in slice 005). Touches that file (T040) so MUST NOT run in parallel with any slice-005-edit task on the same path.
- **Polish (Phase 5)**: Depends on US1 + US2 being complete (perf-smoke needs a working capture path; pinning needs the public surface stable).

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational (Phase 2). Self-contained.
- **US2 (P2)**: Can start after Foundational. The opt-out predicate extraction (T039 + T040) is mechanical and could in principle run BEFORE US1; sequencing it after US1 keeps the slice MVP path crisp.

### Within Each User Story

- Models / DTOs / domain commands before services.
- Handlers + repositories before controllers.
- Server before client (controller exists before the client module dispatches against it — or develop in parallel using the contracted endpoint shape).
- Tests can be written alongside their target file (slice-004/005 precedent — no strict TDD; xUnit + Vitest harnesses already wired).

### Parallel Opportunities

- All `[P]` tasks in Phase 2 can run in parallel (different files, no dependencies on incomplete tasks).
- Within US1: client modules (T028-T032), unit tests (T017, T020, T022, T024, T027), and integration tests (T035-T038) all parallelisable.
- Polish tasks T044-T048 all parallelisable.

---

## Parallel Example: Phase 2 Foundational

```bash
# Launch all parallelisable Foundational tasks together:
Task: "DTO AnalyzerScrollSampleDto in src/Analyzer/Features/Scroll/Infrastructure/Persistence/AnalyzerScrollSampleDto.cs"
Task: "Migration M0006_AddAnalyzerScrollSampleTable in src/Analyzer/Migrations/M0006_AddAnalyzerScrollSampleTable.cs"
Task: "Public records AnalyticsScrollSample + AnalyzerScrollBucket in src/Analyzer/Analytics/"
Task: "Domain command AnalyzerScrollEventCapture in src/Analyzer/Features/Scroll/Domain/"
Task: "Validation exception AnalyzerScrollPayloadValidationException in src/Analyzer/Features/Scroll/Domain/"
Task: "Duplicate exception ScrollSampleDuplicateException in src/Analyzer/Features/Scroll/Domain/"
Task: "Extend IAnalyticsEventStateProvider with CurrentRequestScrollEvents"
```

T005 (migration plan chaining), T010-T011 (resolver enum + dispatch), T013 (state-store extension), and T014 (regression gate) are sequential after the `[P]` set lands.

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks US1).
3. Complete Phase 3: US1.
4. **STOP and VALIDATE**: walk quickstart §1 manually; the slice is shippable here even without US2.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → milestones captured + cascade + idempotency working → ship-candidate-1.
3. US2 → opt-out layered on → ship-candidate-2.
4. Polish → pinning + perf-smoke + audit/identity tests → ship.

### Slice-ship cadence (slice 003 / 004 / 005 precedent)

3-commit push-through onto `006-scroll-tracking`:
- **Commit A**: Foundational (T001-T014). One CI-green commit.
- **Commit B**: US1 (T015-T038). One CI-green commit.
- **Commit C**: US2 + Polish (T039-T051). One CI-green commit.

Then `git push -u origin 006-scroll-tracking`, open PR, rebase-merge to `main` (squashing if requested by the user). Post-merge: T051 housekeeping.

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete tasks.
- `[Story]` label maps each task to its user story for traceability.
- Tests are written alongside their target file (slice-004/005 precedent — no strict TDD; harnesses already wired from slice 001).
- Avoid same-file conflicts: T040 (slice-005 import update) MUST NOT race with any slice-005-edit task on `forms-tracking/index.ts`.
- After every Phase, run the foundational regression gate (T014's pattern): `dotnet test --filter "Category!=Integration&Category!=Perf"` to ensure no slice-002/003/004/005 unit suite has regressed.
- Slice envelope projection: 51 tasks across 5 phases — just above the plan's 35-50 estimate. Drivers: defence-in-depth idempotency adds 1 extra exception type + repo unit case + integration test; the shared opt-out extraction adds 2 tasks not present in slices 002-005; SC-006 FCP perf-smoke (T047, added post-`/speckit-analyze` remediation) closes the last coverage gap.
