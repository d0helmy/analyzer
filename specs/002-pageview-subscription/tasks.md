---

description: "Slice 002 — pageview subscription + analytics-event state provider"
---

# Tasks: Pageview Subscription + Analytics-Event State Provider

**Input**: Design documents from `/specs/002-pageview-subscription/`

**Prerequisites**: [`plan.md`](plan.md), [`spec.md`](spec.md), [`research.md`](research.md), [`data-model.md`](data-model.md), [`contracts/`](contracts/), [`quickstart.md`](quickstart.md)

**Tests**: INCLUDED. Slice 002 mandates test coverage by spec (SC-006 — passing unit + integration tests; FR-009 — public-surface pinning test; US1/US2/US3 each define acceptance scenarios that map to specific test files). Tests-first discipline applies per Constitution Principle VI.

**Organization**: Tasks are grouped by user story (US1/US2/US3) per spec priorities P1/P2/P3. Each story is independently testable per the spec's Independent Test clause.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Each task includes the exact file path to create or modify

## Path Conventions

Slice 002 uses slice 001's existing layout:

- `src/Analyzer/` — RCL source
- `src/Analyzer.Tests/` — test project
- `specs/002-pageview-subscription/` — this slice's docs

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Pre-coding scaffold — directories, NuGet refs, internals visibility.

- [X] T001 Create new directories under `src/Analyzer/`: `Analytics/`, `Features/Events/Application/`, `Features/Events/Application/Anonymization/`, `Features/Events/Domain/`, `Features/Events/Infrastructure/Persistence/`, `Features/Events/Infrastructure/Dispatcher/`, `Migrations/`
- [X] T002 Create new test directories under `src/Analyzer.Tests/`: `Unit/Features/Events/Application/`, `Unit/Features/Events/Application/Anonymization/`, `Integration/PageviewSubscription/`, `Integration/Anonymization/`, `Integration/StateProvider/`, `PublicSurface/Baselines/`, `Perf/`
- [X] T003 [P] Add `Testcontainers.MsSql` PackageReference to `src/Analyzer.Tests/Analyzer.Tests.csproj` (used only as CI fallback when Aspire AppHost isn't running — per [`research.md`](research.md) §7)
- [X] T004 [P] Verify `[assembly: InternalsVisibleTo("Analyzer.Tests")]` on `src/Analyzer/Analyzer.csproj` covers slice 002's new internal types (`AnalyticsEventStateStore`, `AnalyzerEventReceiptCascadeStep`, dispatcher internals, etc.); slice 001 already declared this — confirm only, no change required if already present

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Receipt schema, repository, migration, queue + dispatcher, integration test base — all blocking for any of US1/US2/US3.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Constants + Options

- [X] T005 Add `Constants.Database.AnalyzerEventReceipt = "analyzerEventReceipt"` to `src/Analyzer/Constants.cs` (extends the existing `Constants` class — single canonical name per [`data-model.md`](data-model.md) §6)
- [X] T006 [P] Create `AnalyzerWriteQueueOptions` (internal sealed; `WriteQueueCapacity=10000`, `FlushBatchSize=100`, `FlushIntervalMs=250`) in `src/Analyzer/Features/Events/Infrastructure/Dispatcher/AnalyzerWriteQueueOptions.cs` — per [`data-model.md`](data-model.md) §4

### Domain + Persistence

- [X] T007 [P] Create immutable record `AnalyticsEventReceipt(Guid Id, Guid PageviewKey, Guid VisitorProfileKey, DateTimeOffset ReceivedUtc)` in `src/Analyzer/Analytics/AnalyticsEventReceipt.cs` — `public sealed record`, namespace `Analyzer.Analytics` (alongside `IAnalyticsEventStateProvider`; placement ensures the record sits **inside** the pinned namespace so the pinning baseline captures it directly, not transitively — per slice-002 `/speckit-analyze` finding U2), full XML docs per [`data-model.md`](data-model.md) §2
- [X] T008 [P] Create NPoco DTO `AnalyzerEventReceiptDto` in `src/Analyzer/Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptDto.cs` with attributes `[TableName(Constants.Database.AnalyzerEventReceipt)]`, `[PrimaryKey("id", AutoIncrement = false)]`, `[ExplicitColumns]`, `[Column("id")]` PK, `[Column("pageviewKey")]` + `[Index(UniqueNonClustered, Name="UX_analyzerEventReceipt_pageviewKey")]`, `[Column("visitorProfileKey")]` + `[Index(NonClustered, Name="IDX_analyzerEventReceipt_visitorProfileKey")]`, `[Column("receivedUtc")]` + `[Index(NonClustered, Name="IDX_analyzerEventReceipt_receivedUtc")]` — per [`data-model.md`](data-model.md) §1 (note: FK declared in migration body, NOT via `[ForeignKey]` attribute)
- [X] T009 Create `IAnalyzerEventReceiptRepository` interface in `src/Analyzer/Features/Events/Infrastructure/Persistence/IAnalyzerEventReceiptRepository.cs` with members `Task InsertAsync(AnalyticsEventReceipt receipt, CancellationToken ct)` and `Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)`; internal visibility
- [X] T010 Create `AnalyzerEventReceiptRepository` impl in `src/Analyzer/Features/Events/Infrastructure/Persistence/AnalyzerEventReceiptRepository.cs` (depends on T008, T009): ctor takes `IScopeProvider`; `InsertAsync` opens nested scope, calls `database.InsertAsync(dto)`, catches `SqlException` with `Number == 2627 || Number == 2601` AND `SqliteException` with `SqliteErrorCode == 19` (SQLITE_CONSTRAINT) treats as no-op + `LogDebug` ("Duplicate dispatch tolerated for PageviewKey={PageviewKey}"); `DeleteByVisitorKeyAsync` runs `DELETE FROM analyzerEventReceipt WHERE visitorProfileKey = @visitorProfileKey` via `database.ExecuteAsync` — per [`research.md`](research.md) §8

### Migration + plan + schema composer

- [X] T011 Create `M0001_AddAnalyzerEventReceiptTable : AsyncMigrationBase` in `src/Analyzer/Migrations/M0001_AddAnalyzerEventReceiptTable.cs` (depends on T005, T008); `MigrateAsync` body: `if (!TableExists(Constants.Database.AnalyzerEventReceipt)) { Create.Table<AnalyzerEventReceiptDto>().Do(); Database.Execute("ALTER TABLE [analyzerEventReceipt] ADD CONSTRAINT [FK_analyzerEventReceipt_VisitorProfile] FOREIGN KEY ([visitorProfileKey]) REFERENCES [customizerVisitorProfile]([key])"); }` — raw-SQL FK avoids importing Customizer internals per [`data-model.md`](data-model.md) §1 pinned decision
- [X] T012 Create `AnalyzerMigrationPlan : Umbraco.Cms.Infrastructure.Migrations.MigrationPlan` in `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs` (depends on T011); registers `M0001` as the from-initial migration, plan name `"Analyzer"`
- [X] T013 Create `AnalyzerSchemaComposer : IComposer` in `src/Analyzer/Composers/AnalyzerSchemaComposer.cs` (depends on T012) attribute `[ComposeAfter(typeof(AnalyzerComposer))]`; uses Umbraco's `RuntimeUnattendedInstallNotificationHandler`-equivalent pattern (Customizer precedent: `Customizer/Composers/MigrationsComposer.cs`) to run `MigrationPlanExecutor.ExecutePlanAsync(...)` on Umbraco startup against `AnalyzerMigrationPlan` and the migration-history table identifier `"Analyzer"`

### Integration test base

- [X] T014 Create `AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/TestHelpers/AnalyzerIntegrationTestBase.cs` (depends on T013): `IClassFixture` shape; reads `ConnectionStrings__umbracoDbDSN` from environment, falls back to `Testcontainers.MsSql` if env var absent; per-class schema scope (`Analyzer_Test_<ClassName>`); runs Umbraco install + `AnalyzerMigrationPlan` once per class; exposes `IUmbracoContextFactory`, `IServiceProvider`, `IScopeProvider` to test methods; tear-down drops the per-class schema
- [X] T015 Extend existing `src/Analyzer.Tests/TestHelpers/UmbracoTestHost.cs` to also support the SQL-Server-backed flow (current SQLite seam stays for slice 001 unit tests; new constructor parameter selects SQL Server path) — depends on T014

**Checkpoint**: Foundation ready — receipt schema + repository + migration applied on a real SQL DB; user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 — Analyzer records an event receipt for every captured pageview (Priority: P1) 🎯 MVP

**Goal**: Subscribe to Customizer's `PageviewCaptured`, persist one receipt row per notification through a bounded queue + dispatcher, with at-most-once delivery semantics (Clarifications Q2).

**Independent Test**: an authenticated EntraID employee navigates to a page on the host; `SELECT COUNT(*) FROM analyzerEventReceipt WHERE pageviewKey = <known>` returns exactly 1 within 1 s; duplicate dispatch produces no extra rows; back-pressure drop is observable in the warning log.

### Tests for User Story 1 (FAIL-FIRST)

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation.**

- [X] T016 [P] [US1] Unit `AnalyzerEventReceiptWriteQueueTests` in `src/Analyzer.Tests/Unit/Features/Events/Application/AnalyzerEventReceiptWriteQueueTests.cs`: `TryEnqueue_ReturnsTrueWhenRoomAvailable`, `TryEnqueue_ReturnsFalseWhenAtCapacity` (load with `WriteQueueCapacity` ops, assert next op returns false), `Reader_ExposesEnqueuedItemsInOrder`
- [X] T017 [P] [US1] Unit `PageviewCapturedHandlerTests` in `src/Analyzer.Tests/Unit/Features/Events/Application/PageviewCapturedHandlerTests.cs` (covers US1 AS1 unit + AS4 + edge cases; uses `FakeTimeProvider` + mock `AnalyzerEventReceiptWriteQueue` + mock `IHttpContextAccessor`): `EnqueuesReceiptForValidNotification`, `SkipsEmptyPageviewKey`, `SkipsEmptyVisitorProfileKey_LogsWarning`, `LogsWarningWhenQueueFull`, `SwallowsHandlerExceptionAndLogsWarning`
- [X] T018 [P] [US1] Integration `EndToEndCaptureTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/PageviewSubscription/EndToEndCaptureTests.cs` (covers US1 AS1 + AS3 + SC-001 + SC-004): `PublishCausesReceiptRowInDb_WithinOneSecond`, `DuplicatePublishesProduceSingleRow`, `MultipleVisitorsProduceCorrectReceiptCounts`
- [X] T019 [P] [US1] Integration `BackPressureDropTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/PageviewSubscription/BackPressureDropTests.cs` (covers US1 AS2): `NotificationWithAbsentParentPageviewWritesReceipt` (publish a `PageviewCaptured` without going through `PageviewCaptureMiddleware` — simulates the back-pressure-drop case where the customizerPageview row never lands)

### Implementation for User Story 1

- [X] T020 [US1] Create record `AnalyzerEventReceiptWriteOp(AnalyticsEventReceipt Receipt)` (internal sealed) in `src/Analyzer/Features/Events/Infrastructure/Dispatcher/AnalyzerEventReceiptWriteOp.cs`
- [X] T021 [US1] Create `AnalyzerEventReceiptWriteQueue` in `src/Analyzer/Features/Events/Infrastructure/Dispatcher/AnalyzerEventReceiptWriteQueue.cs` (depends on T006, T020): wraps `Channel<AnalyzerEventReceiptWriteOp>` constructed with `BoundedChannelOptions { Capacity = options.WriteQueueCapacity, FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false }`; exposes `bool TryEnqueue(AnalyzerEventReceiptWriteOp op)` returning `Writer.TryWrite(op)`, `ChannelReader<...> Reader`, `int Capacity`; mirror Customizer's `VisitorWriteQueue` shape per [`research.md`](research.md) §2
- [X] T022 [US1] Create `AnalyzerEventReceiptWriteDispatcher : BackgroundService` in `src/Analyzer/Features/Events/Infrastructure/Dispatcher/AnalyzerEventReceiptWriteDispatcher.cs` (depends on T021, T010): ctor takes `AnalyzerEventReceiptWriteQueue`, `IServiceScopeFactory`, `IOptionsMonitor<AnalyzerWriteQueueOptions>`, `TimeProvider`, `ILogger<AnalyzerEventReceiptWriteDispatcher>`; `ExecuteAsync` drain loop batches up to `FlushBatchSize` per `FlushIntervalMs` and bulk-inserts via repository (resolved through a fresh scope per batch); graceful 5-second drain on shutdown; mirrors Customizer's `VisitorWriteDispatcher` shape per [`research.md`](research.md) §2
- [X] T023 [US1] Create `PageviewCapturedHandler : INotificationAsyncHandler<PageviewCaptured>` in `src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs` (depends on T007, T020, T021); ctor takes `AnalyzerEventReceiptWriteQueue`, `IServiceScopeFactory`, `IHttpContextAccessor`, `TimeProvider`, `ILogger<...>`; `HandleAsync` body matches [`contracts/PageviewCapturedHandler.md`](contracts/PageviewCapturedHandler.md) — **omit `TryUpdateInFlightStateStore` for now** (added in US3-T031, since `AnalyticsEventStateStore` doesn't exist yet); structured logging per [`research.md`](research.md) §9
- [X] T024 [US1] Extend `src/Analyzer/Composers/AnalyzerComposer.cs` (depends on T021, T022, T023; modifies existing file): bind `AnalyzerWriteQueueOptions` from `"Analyzer:WriteQueue"` config section; `services.AddSingleton<AnalyzerEventReceiptWriteQueue>()`; `services.AddScoped<IAnalyzerEventReceiptRepository, AnalyzerEventReceiptRepository>()`; `services.AddHostedService<AnalyzerEventReceiptWriteDispatcher>()`; `services.AddTransient<INotificationAsyncHandler<PageviewCaptured>, PageviewCapturedHandler>()`; `services.AddHttpContextAccessor()` (idempotent); `services.TryAddSingleton(TimeProvider.System)`

**Checkpoint**: At this point, User Story 1 is fully functional. T016/T017 (unit) + T018/T019 (integration) should all pass green. Receipts persist for every authenticated pageview; duplicates are deduplicated; queue drops are logged.

---

## Phase 4: User Story 2 — Operator-triggered visitor anonymisation cascades into receipt rows (Priority: P2)

**Goal**: Register an `IAnonymizationCascadeStep` that hard-deletes the visitor's receipt rows inside Customizer's outer scope. Throwing rolls back the entire anonymisation atomically.

**Independent Test**: seed two visitors A and B with N receipts each; invoke Customizer's `AnonymizeVisitorProfileCommand` for A; verify `COUNT WHERE visitorProfileKey = A.Key` is 0; verify B's rows untouched; verify a fault inside the cascade-step rolls back Customizer's visitor-row overwrite.

### Tests for User Story 2

- [X] T025 [P] [US2] Unit `AnalyzerEventReceiptCascadeStepTests` in `src/Analyzer.Tests/Unit/Features/Events/Application/Anonymization/AnalyzerEventReceiptCascadeStepTests.cs`: `ZeroRowsIsNoOp` (visitor with no receipts; step succeeds, repo called with right key), `DelegatesToRepositoryWithSuppliedKey`, `PropagatesCancellation` (cancellation token passes through)
- [X] T026 [P] [US2] Integration `CascadeDeleteTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/Anonymization/CascadeDeleteTests.cs` (covers US2 AS1 + SC-003): `AnonymisationDeletesReceiptsForOneVisitorOnly`, `PostAnonymisationCountIsZero`, `CompletesUnderTwoHundredMsForTenThousandRows` (seed 10 k receipts on one visitor, run command, assert wall-clock ≤ 200 ms)
- [X] T027 [P] [US2] Integration `CascadeRollbackTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/Anonymization/CascadeRollbackTests.cs` (covers US2 AS2): `ThrowFromAnalyzerStepRollsBackEverything` (register a deliberately-throwing companion `IAnonymizationCascadeStep` after Analyzer's step; assert Analyzer's deletes are undone AND Customizer's visitor row is NOT marked anonymised)

### Implementation for User Story 2

- [X] T028 [US2] Create `AnalyzerEventReceiptCascadeStep : IAnonymizationCascadeStep` in `src/Analyzer/Features/Events/Application/Anonymization/AnalyzerEventReceiptCascadeStep.cs` (depends on T009): `internal sealed`; ctor takes `IAnalyzerEventReceiptRepository`; single-line `ExecuteAsync(visitorProfileKey, ct) => _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct);` — matches `GoalReachedCascadeStep` precedent per [`contracts/AnalyzerEventReceiptCascadeStep.md`](contracts/AnalyzerEventReceiptCascadeStep.md)
- [X] T029 [US2] Extend `src/Analyzer/Composers/AnalyzerComposer.cs` (depends on T028; modifies existing file): `services.AddScoped<IAnonymizationCascadeStep, AnalyzerEventReceiptCascadeStep>()`; place registration after the repository registration from T024

**Checkpoint**: At this point, User Stories 1 AND 2 both work independently. Receipts persist on capture; anonymisation deletes them atomically.

---

## Phase 5: User Story 3 — Stable read contract via `IAnalyticsEventStateProvider` + public-surface pinning (Priority: P3)

**Goal**: Publish the `Analyzer.Analytics.IAnalyticsEventStateProvider` request-scoped contract; pin it (plus slice-001's `IVisitorIdentifier` + `BaseVisitorIdentifier`) via `PublicSurfacePinningTests`.

**Independent Test**: resolve `IAnalyticsEventStateProvider` from DI inside a request scope; assert scoped lifetime (same instance within scope, different across scopes); pinning test passes against the checked-in baseline; tampering with any pinned member's signature fails the pinning test.

### Tests for User Story 3

- [X] T030 [P] [US3] Integration `ScopedLifetimeTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/StateProvider/ScopedLifetimeTests.cs` (covers US3 AS1): `ResolutionReturnsSameInstanceWithinScope`, `ResolutionReturnsDifferentInstanceAcrossScopes`, `CurrentReceiptIsNullBeforeHandlerWrites`
- [X] T031 [P] [US3] Integration `CrossRequestIsolationTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Integration/StateProvider/CrossRequestIsolationTests.cs` (covers US3 AS2): `ConcurrentRequestsDoNotShareState` — spin up two concurrent scopes, mutate one's `AnalyticsEventStateStore`, assert the other's `CurrentRequestReceipt` remains null
- [X] T032 [P] [US3] `PublicSurfacePinningTests` in `src/Analyzer.Tests/PublicSurface/PublicSurfacePinningTests.cs` (covers US3 AS3 + SC-005): mirrors Customizer's shape (`src/Customizer.Tests/Unit/SegmentRules/PublicSurfacePinningTests.cs`); pinned namespaces = `{ "Analyzer.Analytics", "Analyzer.Features.Visitors.Application.Contracts" }`; reflection over `Analyzer.dll`, canonical-form serialise, byte-compare to `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt`
- [X] T033 [US3] Generate the pinning baseline `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` (depends on T032 + all impl tasks below); first run of T032 with no baseline writes the file, subsequent runs assert equality; commit baseline with the slice

### Implementation for User Story 3

- [X] T034 [US3] Create `AnalyticsEventStateStore` in `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` (depends on T007): `internal sealed` class with `AnalyticsEventReceipt? CurrentRequestReceipt { get; }` (returns private `_current` field) and `void SetCurrentReceipt(AnalyticsEventReceipt receipt)` (null-guards then assigns); per [`data-model.md`](data-model.md) §3
- [X] T035 [P] [US3] Create `IAnalyticsEventStateProvider` interface in `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` (depends on T007): `public interface` in namespace `Analyzer.Analytics`; one member `AnalyticsEventReceipt? CurrentRequestReceipt { get; }`; full XML docs per [`contracts/IAnalyticsEventStateProvider.md`](contracts/IAnalyticsEventStateProvider.md)
- [X] T036 [US3] Create `AnalyticsEventStateProvider : IAnalyticsEventStateProvider` impl in `src/Analyzer/Analytics/AnalyticsEventStateProvider.cs` (depends on T034, T035): `internal sealed`; ctor takes `AnalyticsEventStateStore`; `CurrentRequestReceipt => _store.CurrentRequestReceipt;`
- [X] T037 [US3] Extend `src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs` (depends on T023, T034; modifies T023's file): add private method `TryUpdateInFlightStateStore(AnalyticsEventReceipt receipt)` that resolves `IHttpContextAccessor.HttpContext?.RequestServices.GetService<AnalyticsEventStateStore>()` and calls `SetCurrentReceipt` opportunistically; swallow `ObjectDisposedException` + `InvalidOperationException`; call this method from `HandleAsync` after the successful `TryEnqueue` — per [`contracts/PageviewCapturedHandler.md`](contracts/PageviewCapturedHandler.md)
- [X] T038 [US3] Extend `src/Analyzer/Composers/AnalyzerComposer.cs` (depends on T034, T035, T036; modifies existing file): `services.AddScoped<AnalyticsEventStateStore>()`; `services.AddScoped<IAnalyticsEventStateProvider, AnalyticsEventStateProvider>()`; place registrations alongside the slice 001 scoped contracts
- [X] T039 [US3] Verify the pinning baseline (T033) captures `AnalyticsEventReceipt`'s shape directly — the record lives in `Analyzer.Analytics` per T007 (pre-decided by slice-002 `/speckit-analyze` finding U2), which is in the pinned namespace list. Inspect the canonical-form output to confirm; no conditional move required.

**Checkpoint**: All three user stories independently functional. `IAnalyticsEventStateProvider` resolves; pinning baseline locks in the slice-001 + slice-002 extension surfaces.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: SC-002 perf gate, quickstart validation, lessons-learned capture.

- [X] T040 [P] `ThroughputSmokeTests : AnalyzerIntegrationTestBase` in `src/Analyzer.Tests/Perf/ThroughputSmokeTests.cs` (covers SC-002): `[Trait("Category","Perf")]`; runs 1000 pubs/s for 60 s; asserts ≥ 99% receipts persisted, p95 publisher-thread latency delta ≤ 2 ms vs baseline (subscriber-unregistered run on same machine in same test session)
- [X] T041 Run quickstart manual verification (the "Verifying you're done" section of [`quickstart.md`](quickstart.md)): `dotnet build`, full test suite green excluding Perf trait, AppHost boot, browse `https://localhost:44364/umbraco`, render a page, query `analyzerEventReceipt` to confirm a fresh row appears within 1 s
- [X] T042 [P] Re-verify the post-design Constitution Check section in [`plan.md`](plan.md) is still accurate after implementation lands; amend if any gate verdict needs revision (e.g., if T039 moved the receipt record into `Analyzer.Analytics`)
- [X] T043 Final lessons-learned: append slice-002-specific entries to `.remember/now.md` (slice-002 entries: any unique-violation error-number gotchas, queue capacity tuning insights, pinning tool transitive-reference behaviour) — per the user's `/remember` workflow

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies — start immediately.
- **Phase 2 (Foundational)**: depends on Phase 1 — BLOCKS all user stories.
- **Phase 3 (US1)**: depends on Phase 2 complete.
- **Phase 4 (US2)**: depends on Phase 2 complete; some cross-story integration with US1 in tests (anonymisation seeds receipts via the US1 path) but US2 implementation tasks (T028, T029) do NOT depend on US1 tasks.
- **Phase 5 (US3)**: depends on Phase 2 complete + US1 T023 in place (T037 modifies T023's file). US3 implementation can start in parallel with US2 if staffed; T037 is sequenced after T023 explicitly.
- **Phase 6 (Polish)**: depends on Phases 3 + 4 + 5 complete (T040 perf-tests US1's path; T041 verifies all three stories; T042 reviews everything; T043 captures cross-cutting lessons).

### Within Each User Story

- **Tests first**: every story's tests (T016/T017/T018/T019 for US1; T025/T026/T027 for US2; T030/T031/T032 for US3) are written before the matching implementation tasks. They fail on first run; pass after implementation lands.
- **Models → infrastructure → application → composer wiring**: each story follows this internal ordering (e.g., US1: queue/op → dispatcher → handler → composer).
- **Pinning baseline last (T033, T039)**: the baseline file can only be generated when all pinned types exist and have their final signatures.

### Cross-Story Conflicts (Single-File Edits)

- **`src/Analyzer/Composers/AnalyzerComposer.cs`**: extended by T024 (US1), T029 (US2), and T038 (US3). These tasks MUST be sequenced (not [P] across stories) but the file-edits are simple additions; merge-conflict risk is low.
- **`src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs`**: created by T023 (US1) and modified by T037 (US3). T037 explicitly depends on T023.

### Parallel Opportunities

- All Phase 1 `[P]` setup tasks: T003 + T004.
- All Phase 2 foundational `[P]` tasks within their layer: T006 + T007 + T008 are independent (different files); T011 + T012 are sequential.
- Within US1 tests: T016 + T017 + T018 + T019 all `[P]`.
- Within US2 tests: T025 + T026 + T027 all `[P]`.
- Within US3 tests + impl: T030 + T031 + T032 + T035 all `[P]`.
- T040 + T042 in Phase 6 can run in parallel.

---

## Parallel Example: User Story 1

```bash
# Launch all US1 tests in parallel (FAIL-FIRST):
Task: "Write AnalyzerEventReceiptWriteQueueTests in src/Analyzer.Tests/Unit/Features/Events/Application/AnalyzerEventReceiptWriteQueueTests.cs"
Task: "Write PageviewCapturedHandlerTests in src/Analyzer.Tests/Unit/Features/Events/Application/PageviewCapturedHandlerTests.cs"
Task: "Write EndToEndCaptureTests in src/Analyzer.Tests/Integration/PageviewSubscription/EndToEndCaptureTests.cs"
Task: "Write BackPressureDropTests in src/Analyzer.Tests/Integration/PageviewSubscription/BackPressureDropTests.cs"

# Sequentially implement US1:
# T020 -> T021 -> T022 -> T023 -> T024
# Then verify all US1 tests pass green.
```

---

## Implementation Strategy

### MVP scope (User Story 1 only)

1. Phase 1 Setup (T001–T004)
2. Phase 2 Foundational (T005–T015)
3. Phase 3 US1 — write receipts on pageview capture (T016–T024)
4. **STOP and validate**: run `dotnet test` (excluding Perf trait); run quickstart's manual verification; browse intranet → row appears in `analyzerEventReceipt`.
5. Deploy / demo if ready. **MVP achieved.**

The MVP delivers the first slice where Analyzer records data (Constitution Principle IV non-vacuous starting here). The remaining stories are additive on top of this baseline.

### Incremental delivery

1. Setup + Foundational → foundation ready.
2. + US1 → MVP (receipts persist).
3. + US2 → anonymisation cascade works (Principle IV's "MUST register cascade step" satisfied).
4. + US3 → public-surface pinning landed; future slices have a stable read contract to extend.
5. + Polish → perf gate green; lessons captured.

Each step adds value without breaking prior steps.

### Parallel team strategy

With two developers:

1. Both complete Phase 1 + Phase 2 together (shared infra).
2. Dev A: US1 (T016–T024).
3. Dev B: starts US3 infra (T034–T036, T038) in parallel — does NOT block on US1 because the state-store/provider/composer wiring stand alone.
4. After Dev A finishes T023, Dev B picks up T037 (handler tap-in).
5. Either dev picks up US2 (T025–T029) — minimal dependency footprint.
6. Polish + perf (Phase 6) jointly.

With one developer: just follow the priority order P1 → P2 → P3 → Polish.

---

## Notes

- [P] tasks operate on different files with no incomplete-task dependencies.
- [Story] label maps each task to its user story for traceability and parallel-team assignment.
- Tests-first: every story's test tasks fail on first run (the implementation hasn't landed yet) and pass after the matching implementation completes.
- Constitution gates: re-evaluated after Phase 5 lands (T042) — the post-design gates in [`plan.md`](plan.md) are the baseline.
- Commit after each task or logical group; the `/speckit-implement` workflow's commit cadence is finer-grained than `/speckit-tasks` provides.
- Stop at any checkpoint to demo / validate / collect feedback.
- Anti-patterns to avoid: same-file edits across [P]-marked tasks, cross-story dependencies that break independence (US3's T037 is the explicit exception, declared above), skipping the pinning baseline (FR-009 mandate).
