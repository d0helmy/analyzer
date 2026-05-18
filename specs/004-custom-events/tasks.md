---

description: "Task list for slice 004 — custom events"
---

# Tasks: Custom Events

**Input**: Design documents from `/specs/004-custom-events/`

**Prerequisites**: plan.md (loaded), spec.md (loaded), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Plan.md Testing section explicitly requires unit + integration + pinning + perf-smoke + Vitest coverage; test tasks ARE included.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. Phases run in priority order (P1 = MVP → P2 → P3 → Polish).

## Format: `[ID] [P?] [Story] Description`

## Path Conventions

Razor Class Library at `src/Analyzer/`. Test project at `src/Analyzer.Tests/`. Client bundle at `src/Analyzer/Client/`. Paths are relative to repo root unless absolute. Mirrors slice 001 / 002 / 003 layout.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify slice 003 + 002 + 001 prerequisites are in place.

- [X] T001 Confirm slice-003 + slice-002 commits are on `main` (`git log --oneline main..HEAD | head -1` shows recent slice-004 commits; `481b425` and earlier are on `main`).
- [X] T002 Verify Aspire AppHost + SQL container reachable (`docker info`; `dotnet run --project aspire/Analyzer.AppHost --launch-profile https`; backoffice at `https://localhost:44364/umbraco`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Constants, public record, schema, and slice-003 contract extensions. Every user story depends on these landing first.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [X] T003 Add `Database.AnalyzerCustomEvent = "analyzerCustomEvent"` + new nested `AuditLog.CustomEventCapture = "custom-event-capture"` constant in `src/Analyzer/Constants.cs`.
- [X] T004 [P] Create public record `Analyzer.Analytics.AnalyticsCustomEvent` (9 positional properties: `EventKey, SessionKey, VisitorProfileKey, ReceiptKey?, Category, Action, Label?, Value?, ReceivedUtc`) with full XML docs per data-model §2 in `src/Analyzer/Analytics/AnalyticsCustomEvent.cs`.
- [X] T005 [P] Create `AnalyzerCustomEventDto` NPoco DTO in `src/Analyzer/Features/CustomEvents/Infrastructure/Persistence/AnalyzerCustomEventDto.cs` per data-model §1 (10 columns with `[Column]` / `[Index]` / `[Length]` / `[Decimal(18,4)]` / `[NullSetting]` attributes; composite `(category, action)` index emitted by raw SQL in M0003).
- [X] T006 [P] Extend `src/Analyzer/Features/Sessions/Application/IAnalyzerSessionResolver.cs` — add `SessionActivityKind { Pageview, CustomEvent }` enum + `SessionActivityKind activityKind` parameter on `ResolveAsync` per research §3 + data-model §10. Internal interface change.
- [X] T007 [US1-prep] Update `src/Analyzer/Features/Sessions/Application/AnalyzerSessionResolver.cs` — dispatch internally to `ExtendAsync` (Pageview) or `TouchAsync` (CustomEvent) based on the new `activityKind` parameter.
- [X] T008 Extend `src/Analyzer/Features/Sessions/Infrastructure/Persistence/IAnalyzerSessionRepository.cs` with new `Task TouchAsync(Guid sessionKey, DateTimeOffset newLastActivityUtc, CancellationToken ct)` per data-model §10 + Clarification §1.
- [X] T009 Implement `TouchAsync` in `src/Analyzer/Features/Sessions/Infrastructure/Persistence/AnalyzerSessionRepository.cs` per research §4 — 1 indexed UPDATE; `WHERE sessionKey = @0 AND isActive = 1`; idempotent on already-closed rows.
- [X] T010 Update slice-003 `src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs` call site — pass `SessionActivityKind.Pageview` to `resolver.ResolveAsync(...)` (compile-time change; no behavior delta).
- [X] T010b **Regression gate** — immediately after T010, run the full unit suite (`dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --configuration Debug -- -trait- "Category=Integration" -trait- "Category=Perf"`) and confirm the slice-002 + slice-003 corpus (~58 unit tests) still passes. Catches signature-change regressions early instead of at T049. (Analyze finding A4.)
- [X] T011 Create migration `src/Analyzer/Migrations/M0003_AddAnalyzerCustomEventTable.cs` (AsyncMigrationBase): `Create.Table<AnalyzerCustomEventDto>().Do()` guarded by `TableExists`; raw-SQL hard FKs (`FK_analyzerCustomEvent_VisitorProfile`, `FK_analyzerCustomEvent_Session`) + composite `(category, action)` index on SQL Server only (SQLite skip via `Database.DatabaseType.GetProviderName()` per lesson #39). Full body per data-model §9.
- [X] T012 Chain `M0003` after `M0002` in `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs` — add `.To<M0003_AddAnalyzerCustomEventTable>("0003-AddAnalyzerCustomEventTable")`.

**Checkpoint**: Schema, public record, slice-003 resolver + repo extensions ready. User-story implementation can begin.

---

## Phase 3: User Story 1 — Page scripts record custom engagement events (Priority: P1) 🎯 MVP

**Goal**: Authenticated employees' page scripts call `analyzer.send("event", category, action, label?, value?)` → POST to management endpoint → session resolved + touched → row persisted → state-store updated → HTTP 202 with `eventKey`.

**Independent Test**: Run `window.analyzer.send("event", "engagement", "click", "header-cta")` in browser console on a Razor-rendered page. SQL: exactly one `analyzerCustomEvent` row with `category = "engagement"`, `action = "click"`, `label = "header-cta"`, `value IS NULL`, `sessionKey` matching the visitor's active session, `receivedUtc` within 1 second.

### Tests for User Story 1 (write FIRST; fail before implementation)

- [ ] T013 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Infrastructure/Persistence/AnalyzerSessionRepositoryTests.cs` — add `TouchAsync` scenarios (advances `lastActivityUtc`; does NOT change `pageviewCount`; idempotent on already-closed row).
- [ ] T014 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.cs` — add scenario: `ResolveAsync` with `SessionActivityKind.CustomEvent` dispatches to `TouchAsync` (not `ExtendAsync`); `pageviewCount` unchanged.
- [ ] T015 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/Events/Application/AnalyticsEventStateStoreTests.cs` — `AppendCustomEvent` grows list in append order; `CurrentRequestCustomEvents` returns same instance within scope; empty list on fresh store (never null).
- [ ] T016 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/CustomEvents/Application/CustomEventCaptureHandlerTests.cs` — orchestrator calls resolver → TouchAsync (covered by resolver dispatch) → repository.InsertAsync → state-store.AppendCustomEvent → auditor.Audit in correct order; receiptKey populated when `CurrentRequestReceipt` is non-null, null otherwise.
- [ ] T017 [P] [US1] Unit test `src/Analyzer.Tests/Unit/Features/CustomEvents/Web/AnalyzerCustomEventControllerTests.cs` — happy-path returns 202 with `CustomEventResponse { EventKey }`; ModelState invalid returns 400 ProblemDetails; anonymous returns 401.
- [ ] T018 [P] [US1] Vitest test `src/Analyzer/Client/src/analytics/send.spec.ts` — `send()` POSTs JSON; threads anti-forgery header; resolves with `{ eventKey }` on 202; rejects with `{ status, message }` on 4xx/5xx.
- [ ] T019 [P] [US1] Integration test `src/Analyzer.Tests/Integration/CustomEvents/EndToEndCaptureTests.cs` — `[Theory] [InlineData(1)] [InlineData(3)] [InlineData(10)]` parameterised (SC-001): POST N events for one visitor; assert N rows persisted, all with same `sessionKey`, ordered by `receivedUtc`; `CurrentRequestCustomEvents` reflects all N. `[Trait("Category","Integration")]`.
- [ ] T020 [P] [US1] Integration test `…CustomEvents/LazyCloseSessionTests.cs` (US1 AS3, SC-002) — POST after session-inactivity expired; resolver lazy-closes old session + opens new; exactly two `analyzerSession` rows; custom event attaches to the new session.
- [ ] T021 [P] [US1] Integration test `…CustomEvents/BurstAttributionTests.cs` (US1 AS5) — POST two consecutive events within timeout; both attach to same session; `lastActivityUtc` advances on the session; `pageviewCount` UNCHANGED.
- [ ] T021b [P] [US1] Integration test `src/Analyzer.Tests/Integration/CustomEvents/ReceiptCorrelationTests.cs` (US1 AS6) — two scenarios: (a) typical request with no slice-002 receipt populated in the request scope → custom event row has `receiptKey IS NULL`; (b) synthetic in-request co-capture — pre-populate `AnalyticsEventStateStore.SetCurrentReceipt(receipt)` in the test's request scope before POSTing the custom event → custom event row has `receiptKey = receipt.PageviewKey`. Test setup may need a custom `WebApplicationFactory` middleware that seeds the state store. (Analyze finding A1.)
- [ ] T022 [P] [US1] Integration test `…StateProvider/ScopedLifetimeTests.cs` (US1 AS4) — extend slice-003 corpus: assert `CurrentRequestCustomEvents` is empty in a fresh scope; non-empty in a scope where the controller handled a POST.

### Implementation for User Story 1

- [ ] T023 [P] [US1] Implement `IAnalyzerCustomEventRepository` interface in `src/Analyzer/Features/CustomEvents/Infrastructure/Persistence/IAnalyzerCustomEventRepository.cs` with `InsertAsync(dto, ct)` + `DeleteByVisitorKeyAsync(visitorKey, ct)` per data-model §10.
- [ ] T024 [US1] Implement `AnalyzerCustomEventRepository` in `src/Analyzer/Features/CustomEvents/Infrastructure/Persistence/AnalyzerCustomEventRepository.cs` — NPoco-backed; nested `IScopeProvider.CreateScope()` per call; `InsertAsync` via `scope.Database.InsertAsync(dto)`; `DeleteByVisitorKeyAsync` via raw-SQL DELETE.
- [ ] T025 [P] [US1] Create `CustomEventCapture` in-process command record in `src/Analyzer/Features/CustomEvents/Application/CustomEventCapture.cs` per data-model §8 (`Actor, Category, Action, Label?, Value?, UserAgent?, ReceivedUtc`).
- [ ] T026 [US1] Implement `CustomEventCaptureHandler` in `src/Analyzer/Features/CustomEvents/Application/CustomEventCaptureHandler.cs` — orchestrates: `resolver.ResolveAsync(visitor, UA, now, SessionActivityKind.CustomEvent, ct)` → build DTO with `EventKey = Guid.NewGuid()` + resolved `SessionKey` + `ReceiptKey = stateStore.CurrentRequestReceipt?.PageviewKey` → `repository.InsertAsync(dto, ct)` → `stateStore.AppendCustomEvent(projection)` → `auditor.Audit(actor, eventKey, category, action, now)` → return `eventKey`.
- [ ] T027 [P] [US1] Create `ICustomEventAuditor` + `CustomEventAuditor` (impl) in `src/Analyzer/Features/CustomEvents/Application/`. `CustomEventAuditor` injects `ILogger<CustomEventAuditor>`; emits structured `LogInformation` per data-model §11 + research §5 with named properties `AuditAction, ActorUpn, ActorOid, EventKey, Category, Action, ReceivedUtc`.
- [ ] T028 [US1] Extend `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` with private `List<AnalyticsCustomEvent> _currentCustomEvents = new();` field + `IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents => _currentCustomEvents.AsReadOnly();` accessor + `public void AppendCustomEvent(AnalyticsCustomEvent customEvent) { ArgumentNullException.ThrowIfNull(customEvent); _currentCustomEvents.Add(customEvent); }`.
- [ ] T029 [US1] Extend public interface `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` with `IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }` member per data-model §3 + `contracts/IAnalyticsEventStateProvider.md`.
- [ ] T030 [US1] Extend `src/Analyzer/Analytics/AnalyticsEventStateProvider.cs` with `public IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents => _store.CurrentRequestCustomEvents;` projection.
- [ ] T031 [P] [US1] Create request DTO `src/Analyzer/Features/CustomEvents/Web/CustomEventPayload.cs` per data-model §6 (DataAnnotations: `[Required]`, `[StringLength(64, MinimumLength=1)]` on Category/Action, `[StringLength(256)]` on Label).
- [ ] T032 [P] [US1] Create response DTO `src/Analyzer/Features/CustomEvents/Web/CustomEventResponse.cs` per data-model §7 (`Guid EventKey` init-only).
- [ ] T033 [US1] Implement `AnalyzerCustomEventController` in `src/Analyzer/Features/CustomEvents/Web/AnalyzerCustomEventController.cs` per `contracts/AnalyzerCustomEventController.md` — `[ApiController]`, `[Authorize]` per Umbraco backoffice policy, `[Route("management/api/v1/analyzer/custom-event")]`, single `[HttpPost]` action returning `Task<IActionResult>`. Action body: ModelState check → manual whitespace check → `IVisitorIdentifier.GetCurrent()` → read UA from `Request.Headers.UserAgent` → build `CustomEventCapture` → `await _handler.HandleAsync(command, ct)` → return `Accepted(new CustomEventResponse { EventKey = eventKey })`.
- [ ] T034 [US1] Wire US1 services in `src/Analyzer/Composers/AnalyzerComposer.cs`: `services.AddScoped<IAnalyzerCustomEventRepository, AnalyzerCustomEventRepository>()`, `services.AddScoped<ICustomEventAuditor, CustomEventAuditor>()`, `services.AddScoped<CustomEventCaptureHandler>()` (no public interface for handler; controller depends directly). Controller auto-registered by ASP.NET Core via `[ApiController]`. Confirm Umbraco's management-API root group registration picks up the route attribute.
- [ ] T035 [P] [US1] Create client-side `send()` in `src/Analyzer/Client/src/analytics/send.ts` per research §2 — `fetch`-based wrapper; reads Umbraco anti-forgery token; threads `X-XSRF-TOKEN` header; resolves `{ eventKey }` on 202; rejects on 4xx/5xx.
- [ ] T036 [US1] Wire `send` onto `window.analyzer` in `src/Analyzer/Client/src/index.ts` — re-export the function + attach to a global `window.analyzer = { send }` (or extend if `window.analyzer` already exists from slice 001's bundle).

**Checkpoint**: US1 fully functional. POST → row + state-store update + audit emit; client API callable from any authenticated page.

---

## Phase 4: User Story 2 — Operator-triggered visitor anonymisation hard-deletes the visitor's custom events (Priority: P2)

**Goal**: When Customizer's anonymisation runs, Analyzer's third cascade step hard-deletes the visitor's `analyzerCustomEvent` rows inside the outer NPoco scope.

**Independent Test**: Seed visitors A + B with custom events; invoke Customizer's `AnonymizeVisitorProfileCommand` for A; assert A's `analyzerCustomEvent` rows are gone, B's untouched, A's receipts deleted (slice-002), A's sessions soft-anonymised (slice-003).

### Tests for User Story 2

- [ ] T037 [P] [US2] Unit test `src/Analyzer.Tests/Unit/Features/CustomEvents/Application/Anonymization/AnalyzerCustomEventCascadeStepTests.cs` — repo called with visitor key; zero-row no-op; `Guid.Empty` short-circuits; idempotent rerun.
- [ ] T038 [P] [US2] Integration test `src/Analyzer.Tests/Integration/CustomEvents/CascadeHardDeleteTests.cs` (US2 AS1, SC-004) — end-to-end through `AnonymizeVisitorProfileCommand`; assert A's `analyzerCustomEvent` rows = 0; B's untouched; A's receipts also gone (slice-002); A's sessions soft-anonymised (slice-003); visitor row anonymised (slice-007). **Latency assertion (A3 / SC-004)**: seed visitor A with 1 000 `analyzerCustomEvent` rows; measure cascade run via `Stopwatch`; assert `stopwatch.ElapsedMilliseconds <= 200`.
- [ ] T039 [P] [US2] Integration test `…CustomEvents/CascadeRollbackTests.cs` (US2 AS2) — inject a throwing cascade step after Analyzer's custom-event step; assert all state reverts atomically (custom events re-appear; receipts re-appear; sessions revert; visitor row untouched).

### Implementation for User Story 2

- [ ] T040 [US2] Implement `AnalyzerCustomEventCascadeStep` in `src/Analyzer/Features/CustomEvents/Application/Anonymization/AnalyzerCustomEventCascadeStep.cs` per `contracts/AnalyzerCustomEventCascadeStep.md` — internal sealed; ctor injects `IAnalyzerCustomEventRepository` + `ILogger`; `ExecuteAsync` short-circuits on `Guid.Empty`, delegates to `repository.DeleteByVisitorKeyAsync`, logs at Information level.
- [ ] T041 [US2] Register `services.AddScoped<IAnonymizationCascadeStep, AnalyzerCustomEventCascadeStep>()` in `src/Analyzer/Composers/AnalyzerComposer.cs` (third cascade step registration, alongside slice-002 receipt + slice-003 session cascade steps).

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 3 — Validation, RBAC, and audit-log on the management surface (Priority: P3)

**Goal**: Endpoint rejects anonymous (401), malformed (400), missing anti-forgery (400/403); audits only successful captures.

**Independent Test**: Three malformed POSTs (anonymous, empty category, NaN value) all rejected with structured errors; one well-formed POST succeeds with audit-log entry containing actor UPN + oid + eventKey.

### Tests for User Story 3

- [ ] T042 [P] [US3] Unit test `src/Analyzer.Tests/Unit/Features/CustomEvents/Web/CustomEventPayloadValidatorTests.cs` — eight scenarios via `[Theory]`: empty category, empty action, whitespace-only category, whitespace-only action, `Length > 64` category, `Length > 64` action, `Length > 256` label, `Value = NaN` (rejected by JSON deserialiser).
- [ ] T043 [P] [US3] Unit test `src/Analyzer.Tests/Unit/Features/CustomEvents/Application/CustomEventAuditorTests.cs` — structured log shape: assert `LogInformation` invoked with template containing `{AuditAction}`, `{ActorUpn}`, `{ActorOid}`, `{EventKey}`, `{Category}`, `{Action}`, `{ReceivedUtc}` named properties.
- [ ] T044 [P] [US3] Integration test `src/Analyzer.Tests/Integration/CustomEvents/ValidationAndAuditTests.cs` (US3 AS1-AS4, SC-005/006/007) — six scenarios via `[Theory]`: (a) anonymous POST → 401 + 0 rows + 0 audit entries (AS1); (b) empty category → 400 + 0 rows (AS2); (c) `Length > 64` action → 400 + 0 rows (AS2); (d) NaN value → 400 + 0 rows (AS2); (e) **missing/invalid anti-forgery token → 400 (or 403 per Umbraco convention) + 0 rows + 0 audit entries (AS3; Analyze finding A2)**; (f) well-formed POST → 202 + 1 row + 1 audit entry containing UPN + oid + eventKey (AS4). The anti-forgery scenario requires the integration-test base to expose a path that bypasses the auto-attached header so the request reaches the framework's anti-forgery filter without a valid token.

**Checkpoint**: All three USes functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: pinning baseline, perf smoke, regression on slice-002/003 tests, final build + test validation.

- [ ] T045 [P] Implement perf-smoke test `src/Analyzer.Tests/Perf/CustomEventThroughputSmokeTests.cs` per plan §Performance — 1000 POST/s × 60s synthetic load; assert cache-hit p95 ≤ 5 ms + cache-miss p95 ≤ 12 ms + zero duplicate `eventKey` collisions. `[Trait("Category","Perf")]`.
- [ ] T046 Regenerate pinning baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` via `ANALYZER_REGENERATE_SNAPSHOTS=1 dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "FullyQualifiedName~PublicSurfacePinningTests"`; verify the diff matches research §9 (2 additive lines: new `AnalyticsCustomEvent` type block + new `CurrentRequestCustomEvents` property on the interface); rerun WITHOUT the env var and confirm byte-match passes; amend the existing `spec.md` Assumptions entry titled "Public-surface pinning regeneration" with the 2 specific additive baseline diff lines as a Sync Impact-style note, confirming MINOR per Principle X.
- [ ] T047 [P] Update slice-003 integration test `src/Analyzer.Tests/Integration/StateProvider/ScopedLifetimeTests.cs` if not already updated by T022 — confirm slice-003 + slice-004 state-provider assertions co-exist (regression coverage).
- [ ] T048 Run quickstart.md verification steps: open browser console at a Razor-rendered page; call `window.analyzer.send("event", "engagement", "click", "header-cta")`; confirm Promise resolves with `{eventKey}`; query `analyzerCustomEvent` table for the row; trigger anonymisation; confirm row deleted.
- [ ] T049 Run full unit + integration suite locally (`dotnet build Analyzer.slnx --configuration Release` clean; `dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --configuration Release -- -trait- "Category=Perf"`); document any flakes. Run Vitest: `cd src/Analyzer/Client && npm test`.
- [ ] T050 Build clean: `dotnet build Analyzer.slnx` → 0 errors; NU190x warnings expected (slice-003 baseline); zero xUnit1051 (lesson #35); zero NEW warnings introduced.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories. Includes the slice-003 resolver/repo signature changes (T006-T010); slice-003 corpus regression must hold after these.
- **User Stories (Phases 3-5)**: All depend on Foundational. US1 is the MVP; US2 + US3 may proceed in parallel once US1's repository + cascade-step impl are in place.
- **Polish (Phase 6)**: Depends on all user stories complete.

### Within Each User Story

- Tests first; impls after (TDD discipline).
- Records + DTOs + interfaces before services.
- Services + handlers before controllers/endpoints.
- Composer wiring last.

### Parallel Opportunities

- **Foundational [P]**: T004, T005, T006 independent (different files).
- **US1 tests [P]**: T013-T022 all independent.
- **US1 impls [P]**: T023, T025, T027, T031, T032, T035 independent (different files); T024, T026, T028, T029, T030, T033, T034, T036 sequence on file dependencies.
- **US2 tests [P]**: T037, T038, T039 all independent.
- **US3 tests [P]**: T042, T043, T044 all independent.
- **Polish [P]**: T045, T047 independent.

---

## Parallel Example: User Story 1 implementation (after tests author)

```bash
# Tests (T013-T022) authored. Foundational complete. Parallel impls:
Task: "Create AnalyticsCustomEvent record (T004 — already in Foundational)"
Task: "Implement IAnalyzerCustomEventRepository interface (T023)"
Task: "Create CustomEventCapture command record (T025)"
Task: "Create ICustomEventAuditor + CustomEventAuditor (T027)"
Task: "Create CustomEventPayload DTO (T031)"
Task: "Create CustomEventResponse DTO (T032)"
Task: "Create send.ts (T035)"

# Then sequentially:
Task: "Implement AnalyzerCustomEventRepository (T024)" — depends on T023, T005
Task: "Implement CustomEventCaptureHandler (T026)" — depends on T023, T025, T027, T028
Task: "Extend AnalyticsEventStateStore (T028)" — depends on T004
Task: "Extend IAnalyticsEventStateProvider (T029)" — depends on T004
Task: "Extend AnalyticsEventStateProvider impl (T030)" — depends on T028, T029
Task: "Implement AnalyzerCustomEventController (T033)" — depends on T026, T031, T032
Task: "Wire US1 services in AnalyzerComposer (T034)" — depends on all US1 impls
Task: "Wire window.analyzer.send (T036)" — depends on T035
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — includes slice-003 resolver/repo signature change; verify slice-003 + slice-002 corpus still passes after T010).
3. Complete Phase 3: User Story 1.
4. **STOP and VALIDATE**: render a page, call `window.analyzer.send(...)` in console, verify row in DB + state-store update + audit-log entry.
5. The MVP demonstrates the in-request capture path; ready to demo.

### Incremental Delivery

1. Land Foundational → slice-003 corpus still passes (regression-free).
2. Add User Story 1 → MVP demo: page-script capture works end-to-end.
3. Add User Story 2 → demo: operator anonymisation deletes custom events.
4. Add User Story 3 → demo: anonymous rejected; malformed rejected; audit-log emits.
5. Polish → pinning baseline + perf-smoke + quickstart verification → PR.

### Single-Agent Strategy (this slice's expected mode)

With one Claude Code agent + the user:

1. Land Foundational in one commit (~12 tasks).
2. Land US1 MVP in one commit (~24 tasks; 10 tests + 14 impls).
3. Land US2 + US3 + Polish in one commit (~14 tasks; cascade + validation + pinning regen + perf-smoke + manual verification).
4. Open PR with the three commits + the three setup commits already on the branch (`c5fbbd6`, `d855fb1`).

This mirrors the slice-003 ship pattern (9 commits on main post-rebase).

---

## Notes

- [P] tasks = different files, no dependencies.
- [Story] label maps task to specific user story.
- Each user story should be independently completable + testable.
- Verify tests fail before implementing (TDD discipline; slice-002/003 precedent).
- Commit at phase boundaries — `Foundational landed`, `US1 MVP landed`, `US2 + US3 + Polish landed`.
- Stop at any checkpoint to validate the story independently.
- Avoid: vague tasks, same-file conflicts in `[P]` tasks, cross-story dependencies that break independence.
