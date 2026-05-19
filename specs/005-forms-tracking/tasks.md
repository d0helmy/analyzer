---

description: "Task list for slice 005 — Forms Tracking"
---

# Tasks: Forms Tracking

**Input**: Design documents from `/specs/005-forms-tracking/`

**Prerequisites**: plan.md (✓), spec.md (✓), research.md (✓), data-model.md (✓), contracts/ (✓), quickstart.md (✓)

**Tests**: included — slice-004 precedent (unit + integration coverage on every public domain rule, handler, cascade step, repository, materialiser, and management endpoint).

**Organization**: Tasks grouped by user story to enable independent implementation. MVP scope is Phase 1 + Phase 2 + Phase 3 (US1). US2 / US3 / US4 layer on additively.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1 / US2 / US3 / US4); Setup / Foundational / Polish have no story label.

## Path Conventions

Single project (RCL package per Constitution Tech Stack):
- Server: `src/Analyzer/`, tests at `src/Analyzer.Tests/`
- Client: `src/Analyzer/Client/src/`
- Host sample: `samples/Analyzer.Host/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pull in the new Umbraco.Forms dependency, declare new table constants, and create the slice-005 feature folder skeleton.

- [X] T001 Add Umbraco.Forms 17.x central pin to `src/Analyzer/Directory.Packages.props` (`[17.0.0,18.0.0)`), and `<PackageReference Include="Umbraco.Forms" />` to `src/Analyzer/Analyzer.csproj` AND `samples/Analyzer.Host/Analyzer.Host.csproj`. Verify `dotnet restore` succeeds.
- [X] T002 Add `Constants.Database.AnalyzerFormEvent = "analyzerFormEvent"` and `Constants.Database.AnalyzerFormFieldEvent = "analyzerFormFieldEvent"` to `src/Analyzer/Constants.cs`. Update the XML docs to mirror slice-004's `AnalyzerCustomEvent` precedent.
- [X] T003 [P] Create the Features/Forms feature folder skeleton at `src/Analyzer/Features/Forms/{Application,Domain,Infrastructure,Web}/`. Add `Features/Forms/Application/Anonymization/` and `Features/Forms/Application/Abandonment/` subfolders. Add `Features/Forms/Infrastructure/Persistence/` and `Features/Forms/Infrastructure/UmbracoForms/`. Mirrors slice-004's `Features/CustomEvents/` layout.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Tables, public records, state-store wiring, and shared abstractions that EVERY user-story phase consumes.

**⚠️ CRITICAL**: No US1 / US2 / US3 / US4 work can begin until this phase is complete.

- [X] T004 [P] DTO: `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormEventDto.cs` — NPoco DTO per data-model §2.1. `[TableName(Constants.Database.AnalyzerFormEvent)]`, `[PrimaryKey(nameof(Id), AutoIncrement = false)]`, all columns from data-model §1.1 with attributes (`[Column]`, `[Index]`, `[NullSetting]`).
- [X] T005 [P] DTO: `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormFieldEventDto.cs` — NPoco DTO per data-model §2.2.
- [X] T006 [P] Migration: `src/Analyzer/Migrations/M0004_AddAnalyzerFormEventTable.cs` per data-model §3.1. Idempotent via `TableExists` guard. Raw-SQL FK to `customizerVisitorProfile(key)` (SQL Server only; SQLite skip). Composite lifecycle index `IDX_analyzerFormEvent_lifecycle` declared in body.
- [X] T007 [P] Migration: `src/Analyzer/Migrations/M0005_AddAnalyzerFormFieldEventTable.cs` per data-model §3.2. Raw-SQL FK + two composite indexes (`IDX_analyzerFormFieldEvent_perField`, `IDX_analyzerFormFieldEvent_cascadeProbe`).
- [X] T008 Chain `M0004` and `M0005` into `AnalyzerMigrationPlan` after `M0003` (file: `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs`). Confirms slice-005 migrations run in order on host boot.
- [X] T009 [P] Public records: `src/Analyzer/Analytics/AnalyticsFormEvent.cs`, `src/Analyzer/Analytics/AnalyticsFormFieldEvent.cs`, `src/Analyzer/Analytics/AnalyzerFormEventType.cs`, `src/Analyzer/Analytics/AnalyzerFormFieldEventType.cs` per data-model §4. Init-only props, byte-backed enums.
- [X] T010 [P] Domain commands: `src/Analyzer/Features/Forms/Domain/AnalyzerFormEventCapture.cs` + `AnalyzerFormFieldEventCapture.cs` per data-model §5.
- [X] T011 [P] Validation exception: `src/Analyzer/Features/Forms/Domain/AnalyzerFormPayloadValidationException.cs` — derived from slice-004's exception pattern; carries property-name + validator-message slot.
- [X] T012 Extend `Analyzer.Features.Sessions.Application.SessionActivityKind` enum (slice 003) with `FormImpression = 2` value (additive). The resolver's `ResolveAsync` MUST dispatch `FormImpression` → passive read (no touch); `CustomEvent` value continues to drive `TouchAsync` for Start / Success / FieldFocus / FieldUnfocus events. Confirm slice-003 + slice-004 callers unchanged.
- [X] T013 Extend `AnalyzerSessionResolver` (slice 003) at `src/Analyzer/Features/Sessions/Application/AnalyzerSessionResolver.cs` to handle `SessionActivityKind.FormImpression`: resolve current session via repo's GetCurrentAsync without TouchAsync. Add unit tests at `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.cs` (additive cases).
- [X] T014 [P] Extend `IAnalyticsEventStateProvider` interface at `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` with `CurrentRequestFormEvents` + `CurrentRequestFormFieldEvents` members (data-model §4.3). Documented as additive.
- [X] T015 Extend `AnalyticsEventStateStore` (slice 002 onward) at `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` with `AppendFormEvent(...)`, `AppendFormFieldEvent(...)`, and read-only accumulators backing the new interface members. Update slice-002/004 unit tests to assert the new accumulators are empty on store creation.
- [X] T016 Regression gate: run `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --filter "Category!=Integration&Category!=Perf"`. Confirm 82/82 unit suite still green after slice-003 resolver signature change. Failures here halt the slice.

**Checkpoint**: Foundation ready — US1 work can now begin in parallel.

---

## Phase 3: User Story 1 — Per-form lifecycle events (Priority: P1) 🎯 MVP

**Goal**: Capture Impression / Start / Success / Abandon rows per `(visitorKey, formKey, sessionKey)` and expose them via `IAnalyticsEventStateProvider.CurrentRequestFormEvents`. Surface the management endpoint at `/umbraco/management/api/v1/analyzer/form-event/lifecycle` with the Principle-VII four-corner gate. Plug abandonment into slice-003's sweeper.

**Independent Test**: Per quickstart §1 — load a form-bearing page as an authenticated employee, focus a field, submit; query DB for the 3 lifecycle rows. Then trigger sweeper after inactivity timeout; verify `Abandon` row materialised.

- [X] T017 [P] [US1] Repo interface: `src/Analyzer/Features/Forms/Infrastructure/Persistence/IAnalyzerFormEventRepository.cs` per data-model §6.
- [X] T018 [US1] Repo impl: `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormEventRepository.cs` — `InsertAsync`, `DeleteByVisitorKeyAsync`, `ListUnclosedStartsForSessionsAsync`, `InsertAbandonsBulkAsync`. Uses `IScopeProvider` + NPoco. Reuses slice-003's `UniqueConstraintViolationDetector` for the UX(`eventKey`) idempotency check.
- [ ] T019 [P] [US1] Repo unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Infrastructure/AnalyzerFormEventRepositoryTests.cs` — exercise UX-violation handling + cascade-DELETE path. Mocks `IScopeProvider`.
- [X] T020 [P] [US1] Handler interface + capture command wiring: `src/Analyzer/Features/Forms/Application/IAnalyzerFormEventCaptureHandler.cs`.
- [X] T021 [US1] Handler impl: `src/Analyzer/Features/Forms/Application/AnalyzerFormEventCaptureHandler.cs` per contracts/IAnalyzerFormEventCaptureHandler.md. Identity gate → payload validation → session resolution → repo insert → state-store append → audit emit → return `EventKey`. Wire the `SessionActivityKind` dispatch (Impression → FormImpression, Start/Success → CustomEvent).
- [X] T022 [P] [US1] Handler unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormEventCaptureHandlerTests.cs` covering the 5 conformance items in the contract doc (RejectsEmptyVisitor, RejectsMismatchedTimingSlots, HappyPathInsertsAndAppendsState, AuditEmittedOnceOnSuccess, SessionActivityDispatch). Slice-004's `CustomEventCaptureHandlerTests` is the template.
- [X] T023 [P] [US1] Auditor: `src/Analyzer/Features/Forms/Application/IAnalyzerFormEventAuditor.cs` + `AnalyzerFormEventAuditor.cs` impl (ILogger-backed; structured log scope per research §R6).
- [X] T024 [P] [US1] Auditor unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormEventAuditorTests.cs` — assert log scope shape (EventKey, FormKey, EventType, ActorUpn, ReceivedUtc).
- [X] T025 [P] [US1] Management controller: `src/Analyzer/Features/Forms/Web/AnalyzerFormEventManagementController.cs` + `AnalyzerFormEventPayload.cs` per contracts/AnalyzerFormEventManagementController.md. Route `/umbraco/management/api/v1/analyzer/form-event/lifecycle`. Principle-VII four-corner gate via `[Authorize(Policy = "BackOffice")]` + anti-forgery convention.
- [X] T026 [P] [US1] Controller unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Web/AnalyzerFormEventManagementControllerTests.cs` (LifecycleHappyPathReturns202, LifecycleRejectsEmptyFormKey, LifecycleRejectsMismatchedTimingSlots).
- [X] T027 [US1] Composer: `src/Analyzer/Composers/AnalyzerFormsComposer.cs` registers `IAnalyzerFormEventRepository` (Scoped), `IAnalyzerFormEventCaptureHandler` (Scoped), `IAnalyzerFormEventAuditor` (Singleton), management controller (auto-registered by Umbraco), cascade step (Singleton — added in T028 below), state-store extensions (Scoped — already registered for slice 002 + 004; ensure additive members are exposed).
- [X] T028 [P] [US1] Cascade step (lifecycle table): `src/Analyzer/Features/Forms/Application/Anonymization/AnalyzerFormEventCascadeStep.cs` implements `IAnonymizationCascadeStep`. Single-statement DELETE via repo's `DeleteByVisitorKeyAsync`. Hard-delete participation pattern per Principle IV + research §R8.
- [X] T029 [P] [US1] Cascade step unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormEventCascadeStepTests.cs`.
- [X] T030 [P] [US1] Abandonment materialiser interface: `src/Analyzer/Features/Forms/Application/Abandonment/IAnalyzerFormAbandonmentMaterialiser.cs` per contracts/AnalyzerFormAbandonmentMaterialiser.md.
- [X] T031 [US1] Materialiser impl: `src/Analyzer/Features/Forms/Application/Abandonment/AnalyzerFormAbandonmentMaterialiser.cs`. Batch SELECT with the two `NOT EXISTS` predicates (Success-exclusion + Abandon-exclusion for idempotency) + bulk INSERT. Filters out anonymised visitors per spec Edge Case.
- [X] T032 [P] [US1] Materialiser unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormAbandonmentMaterialiserTests.cs` (6 conformance items per contract).
- [X] T033 [US1] Sweeper integration: extend `src/Analyzer/Features/Sessions/Application/AnalyzerSessionSweeperService.cs` to invoke `IAnalyzerFormAbandonmentMaterialiser.MaterialiseAsync(closedSessionKeys, logicalCloseUtc, ct)` after the close-UPDATE inside the same scope. Update slice-003's sweeper tests to assert materialiser is invoked exactly once per pass.
- [X] T034 [P] [US1] Client module: `src/Analyzer/Client/src/features/forms-tracking/form-event-dispatcher.ts` — POST helper for `/lifecycle` route. Handles 202 success, surfaces non-2xx errors via `console.warn` (best-effort).
- [X] T035 [P] [US1] Client module: `src/Analyzer/Client/src/features/forms-tracking/form-observer.ts` — `DOMContentLoaded` attach, `IntersectionObserver` for impressions, `focus` (Start), `submit` (Success) listeners. Reads `data-umbraco-form` Guid from the form element. **v1 scope: forms present at `DOMContentLoaded` only. SPA / Ajax-inserted forms are explicitly out of scope (deferred to v1.1 — would require `MutationObserver` wiring).** Documented in spec Assumptions + Edge Cases.
- [X] T036 [US1] Wire forms-tracking into the bundle: extend `src/Analyzer/Client/src/analyzer-bundle.ts` to import and initialise the forms-tracking module after `DOMContentLoaded`. Add `npm run build` verification step.
- [X] T037 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Forms/EndToEndCaptureTests.cs` — 3-row lifecycle persistence per visitor; multi-visitor disjoint rows. **Additional scenario: simulate an Umbraco Forms server-side validation rejection (4xx) after `Start` is recorded — assert NO `Success` row is written and the lifecycle remains in `Start` state until the sweeper materialises `Abandon`.** Uses `SeedVisitorProfileAsync` helper (issue #20 precedent).
- [X] T038 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Forms/CascadeHardDeleteTests.cs` — DeletesTargetVisitorOnly, CompletesUnderTwoHundredMsForOneThousandRows (SC-004), ZeroRowNoOp.
- [X] T039 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Forms/CascadeRollbackTests.cs` — `Throw_after_lifecycle_step_rolls_back_the_delete` (slice-002/004 precedent).
- [X] T040 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Forms/AbandonmentMaterialisationTests.cs` — 6 conformance items per contract (OneAbandonPerOpenLifecycle, NoAbandonWhenSuccessRecorded, IdempotentAcrossSweeps, SkipsAnonymisedVisitors, ElapsedMsFromStartPopulated, SharedScopeWithSessionClose).

**Checkpoint**: US1 MVP complete — Impression / Start / Success captured via the management endpoint; Abandon materialised by the sweeper. Cascade hard-delete works. Slice is independently shippable here.

---

## Phase 4: User Story 2 — Field-level focus / unfocus (Priority: P2)

**Goal**: Capture `FieldFocus` and `FieldUnfocus` rows per field interaction, with the `hadValue` boolean on Unfocus. Expose via `CurrentRequestFormFieldEvents`. Endpoint at `/umbraco/management/api/v1/analyzer/form-event/field`. Field-table cascade step.

**Independent Test**: Per quickstart §2 — focus / blur fields with and without input; verify two field-event rows per cycle and `hadValue` accuracy. Confirm no field values appear in DB.

- [ ] T041 [P] [US2] Repo interface: `src/Analyzer/Features/Forms/Infrastructure/Persistence/IAnalyzerFormFieldEventRepository.cs`.
- [ ] T042 [US2] Repo impl: `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormFieldEventRepository.cs` — `InsertAsync`, `DeleteByVisitorKeyAsync`.
- [ ] T043 [P] [US2] Repo unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Infrastructure/AnalyzerFormFieldEventRepositoryTests.cs`.
- [ ] T044 [P] [US2] Handler interface: `src/Analyzer/Features/Forms/Application/IAnalyzerFormFieldEventCaptureHandler.cs`.
- [ ] T045 [US2] Handler impl: `src/Analyzer/Features/Forms/Application/AnalyzerFormFieldEventCaptureHandler.cs`. Validates `HadValue` consistency with `EventType` (set only on FieldUnfocus); identity gate; session resolution via `SessionActivityKind.CustomEvent` (touches lastActivity).
- [ ] T046 [P] [US2] Handler unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormFieldEventCaptureHandlerTests.cs` (5 conformance items + HadValue-on-Focus rejection).
- [ ] T047 [P] [US2] Auditor: `src/Analyzer/Features/Forms/Application/IAnalyzerFormFieldEventAuditor.cs` + impl + tests. Log scope carries FieldKey + HadValue.
- [ ] T048 [P] [US2] Controller route: extend `AnalyzerFormEventManagementController.cs` with the `/field` action accepting `AnalyzerFormFieldEventPayload`. Same Principle-VII gate.
- [ ] T049 [P] [US2] Controller unit tests: extend `AnalyzerFormEventManagementControllerTests.cs` with FieldHappyPathReturns202, FieldRejectsHadValueOnFocus.
- [ ] T050 [P] [US2] Cascade step (field table): `src/Analyzer/Features/Forms/Application/Anonymization/AnalyzerFormFieldEventCascadeStep.cs`.
- [ ] T051 [P] [US2] Cascade step unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Application/AnalyzerFormFieldEventCascadeStepTests.cs`.
- [ ] T052 [US2] Composer extension: register field-event repo / handler / auditor / cascade step in `AnalyzerFormsComposer.cs`.
- [ ] T053 [P] [US2] Client module: `src/Analyzer/Client/src/features/forms-tracking/field-observer.ts` — focus / blur listeners attached per field via event capture phase. `hadValue = element.value.length > 0`.
- [ ] T054 [US2] Wire field-observer into the forms-tracking module's `index.ts`.
- [ ] T055 [P] [US2] Integration test: `src/Analyzer.Tests/Integration/Forms/FieldEventCaptureTests.cs` — focus/blur cycles + value-presence verification + zero-field-value-in-DB invariant (SC-003 column-shape audit).
- [ ] T056 [P] [US2] Integration test: field-table cascade hard-delete + rollback — `src/Analyzer.Tests/Integration/Forms/FieldCascadeHardDeleteTests.cs` + `FieldCascadeRollbackTests.cs`.

**Checkpoint**: US2 layered on top of US1 MVP. Slice still independently shippable through US2.

---

## Phase 5: User Story 3 — `analyzer-no-tracking` opt-out (Priority: P3)

**Goal**: Form-level and field-level opt-out attribute respected client-side before any POST is issued. Zero rows, zero POSTs.

**Independent Test**: Per quickstart §3 — render a form with `analyzer-no-tracking` on the `<form>` element; verify zero events. Render a form with the attribute on a single field; verify form-level events present but no field events for that field.

- [ ] T057 [P] [US3] Client module: `src/Analyzer/Client/src/features/forms-tracking/opt-out-attribute.ts` — `isFormOptedOut(form: HTMLFormElement): boolean` and `isFieldOptedOut(field: HTMLElement): boolean`. Treats attribute presence as truthy regardless of value (per FR-007/008).
- [ ] T058 [US3] Wire opt-out into the form-observer: short-circuit attach if `isFormOptedOut(form)` returns true. Wire into the field-observer: skip listener attachment if `isFieldOptedOut(field)`.
- [ ] T059 [P] [US3] Client-side unit test: `src/Analyzer/Client/src/features/forms-tracking/__tests__/opt-out-attribute.test.ts` — true on `analyzer-no-tracking=""`, `analyzer-no-tracking="anything"`, presence-only `<form analyzer-no-tracking>`; false otherwise.
- [ ] T060 [P] [US3] Integration test: `src/Analyzer.Tests/Integration/Forms/OptOutComplianceTests.cs` — render a form with the form-level opt-out attribute (via the Analyzer.Host sample); load it via WebApplicationFactory; assert zero rows in both tables + zero POSTs in a recorded network trace fixture. **HTTP-boundary visibility gated on issue #23** (slice-004 unresolved mgmt-API 404 in test host). **Handler-level + DB assertions are independent of the route gap and provide the core SC-005 evidence at slice-005 ship time. PR description MUST flag this dependency under "Known limitations" — same posture as slice 004.**

**Checkpoint**: Opt-out compliance verified end-to-end.

---

## Phase 6: User Story 4 — Visitor ID field type (Priority: P3)

**Goal**: Submitted Umbraco Forms entries carry the visitor's `customizerVisitorProfile.key` via Analyzer's `VisitorIdField` field type.

**Independent Test**: Per quickstart §4 — add the "Analyzer Visitor ID" field to a form; submit as a known employee; verify the entry's value equals the visitor's key.

- [ ] T061 [P] [US4] Field type: `src/Analyzer/Features/Forms/Infrastructure/UmbracoForms/AnalyzerVisitorIdField.cs` derived from `Umbraco.Forms.Core.Providers.FieldTypes.FieldType` per contracts/AnalyzerVisitorIdField.md. Stable Guid `00000005-0000-0000-0000-000000000001`. `RenderInputType = "hidden"`.
- [ ] T062 [P] [US4] Submission handler: `src/Analyzer/Features/Forms/Infrastructure/UmbracoForms/AnalyzerVisitorIdFieldSubmissionHandler.cs` implements `INotificationHandler<FormSubmittingNotification>`. Resolves `IVisitorIdentifier` from `IHttpContextAccessor`; writes Guid into `RecordField.Values[0]`; logs warning + writes `Guid.Empty` on misconfig.
- [ ] T063 [P] [US4] Field type unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Infrastructure/AnalyzerVisitorIdFieldTests.cs` — field-type metadata (Id, Name, Icon, RenderInputType).
- [ ] T064 [P] [US4] Handler unit tests: `src/Analyzer.Tests/Unit/Features/Forms/Infrastructure/AnalyzerVisitorIdFieldSubmissionHandlerTests.cs` — 5 conformance items per contract (auto-discovery confirmation, value population, client-value overwrite, empty-on-misconfig, misconfig-warning emitted).
- [ ] T065 [US4] Composer: register `INotificationHandler<FormSubmittingNotification>` for `AnalyzerVisitorIdFieldSubmissionHandler` (Scoped). Field type itself auto-discovered by Umbraco Forms — no DI registration needed.
- [ ] T066 [P] [US4] Integration test: `src/Analyzer.Tests/Integration/Forms/VisitorIdFieldSubmitTests.cs` — render a form with the Visitor ID field via the Host sample; submit as the unattended-install user; assert the Umbraco Forms entry's record-field value is the visitor's Guid.

**Checkpoint**: US4 lands. Parity FR-FRM-05 satisfied without Customizer-side change.

---

## Phase 7: Polish & Cross-cutting

**Purpose**: Public-surface pinning regeneration, regression sweep, perf-smoke, doc updates.

- [ ] T067 [P] Regenerate `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` — slice-005 additive diff: 4 records (`AnalyticsFormEvent`, `AnalyticsFormFieldEvent`) + 2 enums (`AnalyzerFormEventType`, `AnalyzerFormFieldEventType`) + 2 `IAnalyticsEventStateProvider` members + 1 enum value (`SessionActivityKind.FormImpression`). Confirm no breaking diff.
- [ ] T068 [P] `PublicSurfacePinningTests` updates: confirm the baseline diff is strictly additive (no removed members). Update test docstrings to reference slice 005.
- [ ] T069 [P] Full unit-test suite green: `dotnet test src/Analyzer.Tests --filter "Category!=Integration&Category!=Perf"` — expect 110+ tests after slice 005's ~30 unit additions. Zero failures.
- [ ] T070 [P] Quickstart manual verification: walk all 7 sections of `specs/005-forms-tracking/quickstart.md` against a running Analyzer.Host + Aspire AppHost. **Includes SC-008 manual FCP-delta spot-check (≤10ms overhead on a 5-form page vs slice-004 baseline) — accepted as manual-only for v1; revisit automation in a follow-up if regressions appear.** Confirm each numbered step's expected outcome holds. Note any deviations for the slice retrospective.
- [ ] T071 [P] Perf-smoke: `src/Analyzer.Tests/Perf/FormsThroughputSmokeTests.cs` — sustained-rate test mirroring slice-004's `CustomEventThroughputSmokeTests`. **Two assertions: (a) SC-001 — at sustained 100 form-events/min for 60s, ≥99% rows persisted within 1s of dispatch (matches slice-002's SC-002 envelope adapted to form-event rate); (b) p95 cache-hit budget per slice-004 precedent.** `[Trait("Category", "Perf")]` so CI skips.
- [ ] T072 [P] Update CLAUDE.md SPECKIT block: replace the slice-005-in-flight paragraph with "Last shipped: slice 005 (forms tracking — `analyzerFormEvent` + `analyzerFormFieldEvent` tables, `/umbraco/management/api/v1/analyzer/form-event/{lifecycle,field}` management endpoints, `AnalyzerVisitorIdField` Umbraco Forms field type, `analyzer-no-tracking` opt-out attribute, abandonment materialisation hooked into slice-003's sweeper, two new cascade-step registrations) — see [`specs/005-forms-tracking/`](specs/005-forms-tracking/) for the artifacts. No cross-product Customizer prereq required."
- [ ] T073 [P] Build-clean validation: `dotnet build Analyzer.slnx -c Release` — zero warnings beyond pre-existing NU1902/NU1903 (issue #10). Slice-005-introduced warnings = failure.

---

## Dependencies

| User Story | Depends on | Parallel with |
|---|---|---|
| US1 (P1, MVP) | Phase 1 + Phase 2 | — |
| US2 (P2) | Phase 1 + Phase 2 (US1 can be in flight) | US1 endpoint route extension serialises T048 |
| US3 (P3) | US1 client bundle (form-observer + field-observer wired) | independent client work; integration test depends on US1+US2 capture paths existing |
| US4 (P3) | Phase 2 (independent of US1/US2/US3 — only needs `IVisitorIdentifier` from slice 002) | fully parallel to US1/US2/US3 |

**Story-level parallelisation**: US1, US2, US3, US4 are largely independent within their internal task graphs. Within Phase 3 (US1) the dependency chain is:
- T017 → T018 → T019 (interface → impl → tests)
- T020 → T021 → T022 (handler interface → impl → tests)
- T023 → T024 (auditor)
- T025 → T026 (controller)
- T027 wires composer once T017/T020/T023/T025 land
- T028 → T029 (cascade)
- T030 → T031 → T032 → T033 (materialiser → sweeper hook)
- T034, T035, T036 (client) parallel to server work
- T037–T040 (integration tests) depend on the full feature being wired (T027 composer + T036 bundle)

## Parallel execution examples

**Within Phase 2 foundational**: T004, T005, T006, T007, T009, T010, T011, T014 are all parallel (different files, no shared state).

**Within Phase 3 (US1)**: T017, T020, T023, T025, T028, T030, T034, T035 are parallel (different files). Their tests (T019, T022, T024, T026, T029, T032) are also parallel.

**Across user stories**: US1, US2, US3, US4 are largely independent; their tasks can run in parallel once Phase 2 closes.

## Implementation strategy

**MVP scope** (suggested ship point if time-boxed):
- Phase 1 + Phase 2 + Phase 3 (US1) = T001–T040
- ~40 tasks, ~3-commit shipping cadence matching slices 003 + 004:
  - **Commit 1** (Phase 1 + Phase 2): T001–T016 (setup + foundational; 16 tasks)
  - **Commit 2** (US1 server + abandonment): T017–T033 (17 tasks)
  - **Commit 3** (US1 client + integration tests): T034–T040 (7 tasks)

Followup commits per user story:
- **Commit 4** (US2 — field-level): T041–T056
- **Commit 5** (US3 — opt-out): T057–T060
- **Commit 6** (US4 — Visitor ID field): T061–T066
- **Commit 7** (Polish): T067–T073

Total slice envelope: **73 tasks across 7 phases**. Per slice-004 precedent (50 tasks), slice 005 is ~46% larger; the extra envelope is driven by 4 user stories (vs. slice 004's 3), the second persistence table, and the Umbraco Forms field-type sub-feature.

**Independent test criteria per story**:
- US1: 3 lifecycle rows + 1 Abandon row materialised; verified per quickstart §1.
- US2: field events captured with correct `hadValue`; field values never persist; verified per quickstart §2.
- US3: zero rows + zero POSTs when opt-out attribute present; verified per quickstart §3.
- US4: Forms entry contains visitor key matching `analyzerFormEvent` row; verified per quickstart §4.
