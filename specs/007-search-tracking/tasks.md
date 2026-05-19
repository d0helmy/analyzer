---

description: "Task list for slice 007 — Internal Search-Tracking Capture"
---

# Tasks: Internal Search-Tracking Capture

**Input**: Design documents from `/specs/007-search-tracking/`

**Prerequisites**: plan.md (✓), spec.md (✓), research.md (✓), data-model.md (✓), contracts/ (✓), quickstart.md (✓)

**Tests**: included — slice-004/005/006 precedent (unit + integration coverage on every public domain rule, handler, cascade step, repository, normaliser, and management endpoint).

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

**Purpose**: Declare the new table constant and create the slice-007 feature folder skeleton. No new package dependency (mirrors slice 006 — capture-only, no upstream library addition).

- [ ] T001 Add `Constants.Database.AnalyzerSearchEvent = "analyzerSearchEvent"` to `src/Analyzer/Constants.cs`. Update the XML docs to mirror slice-006's `AnalyzerScrollSample` precedent (note the PII tag per FR-SRC-04 — informational only, not load-bearing).
- [ ] T002 [P] Create the Features/Search feature folder skeleton at `src/Analyzer/Features/Search/{Application,Domain,Infrastructure,Web}/`. Add `Features/Search/Application/Anonymization/`, `Features/Search/Application/Normalisation/`, and `Features/Search/Infrastructure/Persistence/` subfolders. Mirrors slice-006's `Features/Scroll/` layout plus the new `Normalisation/` folder for the default normaliser.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Table, public surfaces (record + extension-point interface + state-provider member), default normaliser, fixture, state-store wiring, and shared abstractions that EVERY user-story phase consumes.

**⚠️ CRITICAL**: No US1 / US2 work can begin until this phase is complete.

- [ ] T003 [P] DTO: `src/Analyzer/Features/Search/Infrastructure/Persistence/AnalyzerSearchEventDto.cs` — NPoco DTO per data-model §2.1. `[TableName(Constants.Database.AnalyzerSearchEvent)]`, `[PrimaryKey(nameof(Id), AutoIncrement = false)]`, `[Length(256)]` on `RawQuery` + `NormalisedQuery`, all four `[Index]` declarations (eventKey UX, visitor IDX, normalisedQuery IDX, pageview IDX, receivedUtc IDX) on the DTO.
- [ ] T004 [P] Migration: `src/Analyzer/Migrations/M0007_AddAnalyzerSearchEventTable.cs` per data-model §3.1. Idempotent via `TableExists` guard. SQL Server branch: raw-SQL FKs to `customizerVisitorProfile(key)` AND `analyzerSession(sessionKey)`; CHECK constraints `CK_analyzerSearchEvent_resultCount`, `CK_analyzerSearchEvent_rawQueryLength`, `CK_analyzerSearchEvent_normalisedQueryLength`. SQLite branch: table only (no FK / no CHECKs), matching slices 002/004/005/006.
- [ ] T005 Chain `M0007` into `AnalyzerMigrationPlan` after `M0006` (file: `src/Analyzer/Migrations/AnalyzerMigrationPlan.cs`). Confirms slice-007 migration runs on host boot.
- [ ] T006 [P] Public record: `src/Analyzer/Analytics/AnalyticsSearchEvent.cs` per data-model §4.1 + contracts/AnalyticsSearchEvent.md. Init-only required props on the record. Include the PII-notice XML doc block (FR-SRC-04 / NFR-SEC-05) so IntelliSense surfaces the role-gating obligation to consumers.
- [ ] T007 [P] Public extension-point interface: `src/Analyzer/Analytics/IAnalyzerSearchQueryNormaliser.cs` per contracts/IAnalyzerSearchQueryNormaliser.md. Includes the full XML doc block (lifetime, replacement convention, culture-stability MUST-clauses). **First new Analyzer-defined public extension surface since slice 001's `IVisitorIdentifier`** — treat with the same review rigour.
- [ ] T008 [P] Default normaliser impl: `src/Analyzer/Features/Search/Application/Normalisation/DefaultAnalyzerSearchQueryNormaliser.cs` per research §R2. Internal sealed class. Implements `Normalise` as `Trim → Normalize(NormalizationForm.FormKC) → ToLower(CultureInfo.InvariantCulture) → Regex.Replace(@"\s+", " ")`. Caches the compiled regex in a static `Regex` field with `RegexOptions.Compiled`.
- [ ] T009 [P] 100-pair normaliser fixture: `src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json` — checked-in JSON array of `{ "input": "...", "expected": "..." }` covering trim, case-fold, NFKC fullwidth/halfwidth, ligature decomposition (`ﬁ` → `fi`), compatibility characters (`①` → `1`), CRLF/tab/NBSP collapse, leading combining marks, accented Latin, and emoji passthrough per research §R2. Loaded by T010.
- [ ] T010 [P] Default normaliser unit tests: `src/Analyzer.Tests/Unit/Features/Search/Application/DefaultAnalyzerSearchQueryNormaliserTests.cs` covering the five MUST-clauses + 100-pair fixture (SC-002). Includes `Culture_stability_under_tr_TR` test that sets `CultureInfo.CurrentCulture = new CultureInfo("tr-TR")` for the test duration and re-runs the full fixture — proves invariant lowering. Includes `Idempotency_normalising_twice_is_a_no_op` over the fixture.
- [ ] T011 [P] Domain command: `src/Analyzer/Features/Search/Domain/AnalyzerSearchEventCapture.cs` per data-model §5. Record with `Actor : VisitorIdentity` (the slice-002 identity-projection type — carries `Key`, `Upn`, `Oid`, `IsAvailable`), `SessionKey`, `PageviewKey`, `ContentKey`, `RawQuery`, `ResultCount`, `ReceivedUtc`. Mirrors slice-006's `AnalyzerScrollEventCapture` shape — the `Actor` field carries the resolved identity into the domain layer so the handler can run the identity gate (T021) and the auditor can log `ActorUpn` + `ActorOid` without a second identity round-trip.
- [ ] T012 [P] Validation exception: `src/Analyzer/Features/Search/Domain/AnalyzerSearchPayloadValidationException.cs` — mirrors slice-004/005/006 exception pattern; carries property-name + validator-message slot.
- [ ] T013 Extend `SessionActivityKind` enum (slice 003 / 005 / 006) at `src/Analyzer/Features/Sessions/Application/SessionActivityKind.cs` with `SearchEvent = N` (next-available after slice-006's `ScrollEvent = 3`, so most likely `SearchEvent = 4`). Also extend `AnalyzerSessionResolver` at `src/Analyzer/Features/Sessions/Application/AnalyzerSessionResolver.cs` to dispatch `SearchEvent` → `TouchAsync` (intentional engagement parity with `CustomEvent` / `ScrollEvent` / `FormImpression`). Add additive unit case at `src/Analyzer.Tests/Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.cs` (`SearchEvent_TouchesLastActivity`). Confirm slices 003-006 callers unchanged.
- [ ] T014 [P] Extend `IAnalyticsEventStateProvider` interface at `src/Analyzer/Analytics/IAnalyticsEventStateProvider.cs` with `CurrentRequestSearchEvents : IReadOnlyList<AnalyticsSearchEvent>` (data-model §4.3). Documented as additive (slice-007 lineage).
- [ ] T015 Extend `AnalyticsEventStateStore` at `src/Analyzer/Features/Events/Application/AnalyticsEventStateStore.cs` with `AppendSearchEvent(...)` and a read-only accumulator backing the new interface member. Update slice-002/004/005/006 unit tests to assert the new accumulator is empty on store creation.
- [ ] T016 Regression gate: run `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --no-build --filter "Category!=Integration&Category!=Perf"`. Confirm 134/134 unit suite (slice-006 baseline) still green after the additive resolver / state-store / normaliser changes. The new T010 fixture-based tests should land on top of the existing baseline, bringing the count to ~145+. Failures here halt the slice.

**Checkpoint**: Foundation ready — US1 work can now begin.

---

## Phase 3: User Story 1 — Search submissions captured with normalised query + result count (Priority: P1) 🎯 MVP

**Goal**: Capture one row per accepted search submission with `rawQuery`, `normalisedQuery`, `resultCount`, and visitor/session/pageview/content correlation. Surface the management endpoint at `/umbraco/management/api/v1/analyzer/search-event` with the Principle-VII four-corner gate + visitor-bound `pageviewKey` check. Expose events via `IAnalyticsEventStateProvider.CurrentRequestSearchEvents`. Register the hard-delete cascade step. Audit-log entries carry actor + correlation IDs but **never** the query text.

**Independent Test**: Per quickstart §1 — invoke `await window.analyzer.sendSearch("design system", 12)` from an authenticated page console; verify exactly one row in `analyzerSearchEvent` with the expected raw/normalised/count + the structured log line carrying no query field.

- [ ] T017 [P] [US1] Repo interface: `src/Analyzer/Features/Search/Infrastructure/Persistence/IAnalyzerSearchEventRepository.cs` — `InsertAsync(AnalyzerSearchEventDto, CancellationToken)`, `DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken)`, `CountByVisitorAsync(Guid visitorProfileKey, CancellationToken)` (for perf-smoke), and `ResolvePageviewVisitorBindingAsync(Guid pageviewKey, CancellationToken) : Task<Guid?>` — returns the `visitorProfileKey` owning the supplied pageview, or `null` if the pageview does not exist. Used by the handler's visitor-binding check per research §R3.
- [ ] T018 [US1] Repo impl: `src/Analyzer/Features/Search/Infrastructure/Persistence/AnalyzerSearchEventRepository.cs` — uses `IScopeProvider` + NPoco. `InsertAsync` is a single INSERT (no idempotency guard — search events have no `(pageviewKey, normalisedQuery)` unique index per research §R7). `ResolvePageviewVisitorBindingAsync` projects `(visitorProfileKey)` from `customizerPageview` keyed by `pageviewKey` (single indexed read; no Customizer DTO import — raw SQL per Principle III).
- [ ] T019 [P] [US1] **DEFERRED** — Repo unit tests at `src/Analyzer.Tests/Unit/Features/Search/Infrastructure/AnalyzerSearchEventRepositoryTests.cs`. Mocking `IUmbracoDatabase` adds no incremental value over the integration tests `EndToEndCaptureTests` + `PageviewVisitorBindingTests` + `CascadeHardDeleteTests`, which exercise the real repo against Testcontainers MS SQL (matches slice 006 T017 precedent). Re-evaluate only if an internal-method-only bug demands unit-level shape.
- [ ] T020 [P] [US1] Handler interface: `src/Analyzer/Features/Search/Application/IAnalyzerSearchEventCaptureHandler.cs` per data-model §5 — single async `HandleAsync(AnalyzerSearchEventCapture command, CancellationToken ct)` returning the persisted `AnalyticsSearchEvent`. Internal contract (not pinned to public surface; matches slice-006's `IAnalyzerScrollEventCaptureHandler` internal-only treatment).
- [ ] T021 [US1] Handler impl: `src/Analyzer/Features/Search/Application/AnalyzerSearchEventCaptureHandler.cs` — identity gate (throw `UnauthorizedAccessException` if `command.Actor.IsAvailable == false` OR `command.Actor.Key == Guid.Empty`; controller maps to 401/403) → invoke `IAnalyzerSearchQueryNormaliser.Normalise(command.RawQuery)` → reject empty normalised output as `AnalyzerSearchPayloadValidationException` (defence in depth per data-model §5 step 3) → visitor-bound pageview lookup (reject 400 if `ResolvePageviewVisitorBindingAsync(command.PageviewKey) != command.Actor.Key`) → resolve session via `IAnalyzerSessionResolver.ResolveAsync(command.Actor.Key, ..., SessionActivityKind.SearchEvent, ...)` → repo `InsertAsync` (with `VisitorProfileKey = command.Actor.Key`) → `AnalyticsEventStateStore.AppendSearchEvent(...)` → `IAnalyzerSearchEventAuditor.AuditCaptureAsync(command.Actor.Upn, command.Actor.Oid, eventKey, command.PageviewKey, command.ResultCount, command.ReceivedUtc)` → return the persisted `AnalyticsSearchEvent`.
- [ ] T022 [P] [US1] Handler unit tests: `src/Analyzer.Tests/Unit/Features/Search/Application/AnalyzerSearchEventCaptureHandlerTests.cs` covering: `RejectsUnavailableActor`, `RejectsEmptyVisitorKey`, `RejectsEmptyRawQuery`, `RejectsEmptyNormalisedQuery` (custom normaliser that returns empty for one specific input — proves the defence-in-depth check), `RejectsNegativeResultCount`, `RejectsPageviewBelongingToDifferentVisitor` (proves R3), `RejectsNonExistentPageview`, `HappyPathInsertsAndAppendsStateAndAudits` (asserts auditor invoked exactly once + `AppendSearchEvent` called once + return value's `NormalisedQuery` matches the normaliser output).
- [ ] T023 [P] [US1] Auditor: `src/Analyzer/Features/Search/Application/IAnalyzerSearchEventAuditor.cs` + `AnalyzerSearchEventAuditor.cs` impl (ILogger-backed). Structured log per research §R6 carrying `EventKey, PageviewKey, ResultCount, ActorUpn, ActorOid, ReceivedUtc` — **MUST NOT** carry `RawQuery` or `NormalisedQuery` (FR-009 + SC-006). The log template MUST NOT include `{RawQuery}` or `{NormalisedQuery}` placeholders even unused — defence against future accidental `LogInformation` overloads picking them up.
- [ ] T024 [P] [US1] Auditor unit tests: `src/Analyzer.Tests/Unit/Features/Search/Application/AnalyzerSearchEventAuditorTests.cs` — assert log scope shape on the success path; **assert the log entry's parameters dictionary contains NEITHER `RawQuery` NOR `NormalisedQuery` keys** (SC-006 redaction). Run a property-based test feeding 50 distinct queries through the auditor and grep the captured log output for any of the query substrings — exactly zero hits expected.
- [ ] T025 [P] [US1] Management controller + payload: `src/Analyzer/Features/Search/Web/AnalyzerSearchEventManagementController.cs` + `AnalyzerSearchEventPayload.cs` per contracts/AnalyzerSearchEventManagementController.md. Route `POST /umbraco/management/api/v1/analyzer/search-event`. Principle-VII four-corner gate via `[Authorize(Policy = "BackOffice")]` + anti-forgery convention. Server reads `contentKey` from the validated `customizerPageview` lookup (NOT from the payload — defends against forged content-key correlations per controller contract). Map `AnalyzerSearchPayloadValidationException` to 400 problem-details; map `UnauthorizedAccessException` to 401/403. No 409 path (no idempotency).
- [ ] T026 [P] [US1] Controller unit tests: `src/Analyzer.Tests/Unit/Features/Search/Web/AnalyzerSearchEventManagementControllerTests.cs` (`HappyPathReturns202WithEventKey`, `RejectsEmptyQueryWith400`, `RejectsOversizeQueryWith400`, `RejectsNegativeResultCountWith400`, `RejectsResultCountAboveSanityCapWith400`, `RejectsEmptyPageviewKeyWith400`, `AnonymousReturns401`, `IdentityUnavailableReturns403`).
- [ ] T027 [US1] Composer: `src/Analyzer/Composers/AnalyzerSearchComposer.cs` registers `IAnalyzerSearchEventRepository` (Scoped), `IAnalyzerSearchEventCaptureHandler` (Scoped), `IAnalyzerSearchEventAuditor` (Singleton), `IAnalyzerSearchQueryNormaliser` → `DefaultAnalyzerSearchQueryNormaliser` (Scoped per research §R5; uses plain `AddScoped` to allow last-registration-wins host override), management controller (auto-registered by Umbraco), cascade step via `AnonymizationCascadeStepCollectionBuilder.Append<AnalyzerSearchEventCascadeStep>()` (Transient — matches slice 004/005/006 cascade-step lifetime).
- [ ] T028 [P] [US1] Cascade step: `src/Analyzer/Features/Search/Application/Anonymization/AnalyzerSearchEventCascadeStep.cs` implements `IAnonymizationCascadeStep` per contracts/AnalyzerSearchEventCascadeStep.md. Single-statement DELETE via repo's `DeleteByVisitorAsync`. Hard-delete participation pattern per Principle IV v1.1.1 + research §R8 (diverges from contract D8 — documented). `Order` = next-available after slice-006's `AnalyzerScrollSampleCascadeStep`.
- [ ] T029 [P] [US1] Cascade step unit tests: `src/Analyzer.Tests/Unit/Features/Search/Application/AnalyzerSearchEventCascadeStepTests.cs` (`ZeroRowsNoOp`, `HundredRowsDeletedOnce`, `RepoThrowsBubbles`).
- [ ] T030 [P] [US1] Client module: `src/Analyzer/Client/src/features/search-tracking/payload.ts` — shared TypeScript interface `SearchEventPayload { pageviewKey: string; query: string; resultCount: number; }`. Consumed by dispatcher + tests.
- [ ] T031 [P] [US1] Client module: `src/Analyzer/Client/src/features/search-tracking/search-event-dispatcher.ts` — POST helper for `/search-event`. `fetch` with `credentials: 'same-origin'`, `keepalive: true`, anti-forgery header. Returns `Promise<{ status: number, body: any }>` so the helper can map to the public response shape; never throws on network errors (caller maps to rejection).
- [ ] T032 [P] [US1] Client module: `src/Analyzer/Client/src/features/search-tracking/send-search.ts` — public helper `sendSearch(query, resultCount, options?)` per contracts/AnalyzerSendSearchClient.md. Validates inputs client-side (empty query, oversize query, non-finite / non-integer / negative resultCount, missing pageviewKey from options/global/meta-tag), POSTs via the dispatcher, maps 202 → `{ eventKey }` resolve / non-202 → `{ status, message }` reject. **Opt-out short-circuit is NOT wired in US1** — T042 (US2) adds it. The helper's *return type* declares `Promise<{ eventKey: string } | { skipped: true }>` so the US2 wiring is type-compatible; until US2 lands, the `{ skipped: true }` branch is unreachable.
- [ ] T033 [P] [US1] Client module: `src/Analyzer/Client/src/features/search-tracking/index.ts` — exports `attachSendSearch(opts)` entrypoint per contracts/AnalyzerSendSearchClient.md. Wires `sendSearch` onto `window.analyzer.sendSearch`.
- [ ] T034 [US1] Wire search-tracking into the bundle: extend `src/Analyzer/Client/src/analyzer-bundle.ts` to import and initialise the search-tracking module after `DOMContentLoaded`, AFTER slice-006's scroll-tracking init (sequential init keeps the `window.analyzer` attachment ordering deterministic). Update `globals.d.ts` (or equivalent ambient declaration) to declare the new `sendSearch` member. Add `npm run build` verification step.
- [ ] T035 [P] [US1] Vitest unit tests: `src/Analyzer/Client/src/features/search-tracking/send-search.test.ts` covering the **15 non-opt-out** conformance items in contracts/AnalyzerSendSearchClient.md (happy path, trim, empty/whitespace/oversize/negative/non-integer/NaN/Infinity rejection, three pageviewKey sources, pageviewKey unavailable, server 400, server 202, anti-forgery header). The opt-out conformance item (item 1 in the contract doc) is deferred to T043 / T044 (US2). Use the slice-004/005/006 Vitest harness.
- [ ] T036 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/EndToEndCaptureTests.cs` — POST a valid payload → one row in `analyzerSearchEvent`; multi-visitor disjoint rows; cross-visitor rows untouched. Asserts the structured-log substrate captured an `AnalyzerSearchEventCaptured` entry with `EventKey` + `PageviewKey` + `ResultCount` + `ActorUpn` + `ActorOid` + `ReceivedUtc` **AND** zero entries containing the query text (SC-006).
- [ ] T037 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/NormalisationAggregationTests.cs` (SC-007) — seed 3 000 rows: 1 000 distinct queries × 3 variants each (e.g. `["Hello World", "  hello world  ", "ＨＥＬＬＯ ＷＯＲＬＤ"]`); assert `SELECT COUNT(DISTINCT normalisedQuery) FROM analyzerSearchEvent` equals exactly 1 000. Tests both the normaliser correctness AND the index's `GROUP BY` stability.
- [ ] T038 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/PageviewVisitorBindingTests.cs` — three cases: (a) POST with `pageviewKey` belonging to a different visitor → 400 with structured error mentioning pageviewKey, zero rows; (b) POST with non-existent `pageviewKey` → 400, zero rows; (c) POST with valid pageview belonging to the same visitor → 202, one row. Per research §R3 + spec Edge Cases.
- [ ] T039 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/CascadeHardDeleteTests.cs` — `DeletesTargetVisitorOnly`, `CompletesUnderTwoHundredMsForOneThousandRows` (SC-004), `ZeroRowNoOp`, **`PIICleanupVerification`** — seed visitor V with a row whose `rawQuery` is a unique seed string (e.g. `"slice-007-pii-test-12345"`); after cascade, assert `SELECT COUNT(*) FROM analyzerSearchEvent WHERE rawQuery LIKE '%slice-007-pii-test%'` returns zero. Proves the literal query text is gone, not just the link.
- [ ] T040 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/CascadeRollbackTests.cs` — `Throw_after_search_step_rolls_back_the_delete` (slice-002/004/006 precedent — register a sentinel `IAnonymizationCascadeStep` that throws after the search step; assert rows remain).
- [ ] T041 [P] [US1] Integration test: `src/Analyzer.Tests/Integration/Search/CustomNormaliserOverrideTests.cs` — register an `UppercaseNormaliser` in a composer that runs after `AnalyzerSearchComposer`; POST `"hello"`; assert persisted `normalisedQuery == "HELLO"`. Proves the last-registration-wins replacement convention from R5 + FR-005 works end-to-end.

**Checkpoint**: US1 MVP complete — search submissions captured + normalised + role-gated + PII-redacted in logs; cascade hard-delete works; custom normaliser override works. Slice is independently shippable here.

---

## Phase 4: User Story 2 — Opt-out via `analyzer-no-tracking` attribute (Priority: P2)

**Goal**: Wire the slice-006 shared `isOptedOut()` predicate into the search helper at **per-call** evaluation time (differs from slice 006's init-only read because there is no long-lived listener to mute). On opt-out, the Promise resolves with `{ skipped: true }`.

**Independent Test**: Per quickstart §2 — render a page with `<body analyzer-no-tracking>`, invoke `analyzer.sendSearch(...)`, assert zero POSTs in DevTools + zero rows in DB + Promise resolves to `{ skipped: true }`. Dynamically remove the attribute; the next call captures normally.

- [ ] T042 [US2] Wire `isOptedOut()` into `send-search.ts` (T032's helper) at **per-call** evaluation time: insert as the first check inside the helper body, before client-side input validation. If `true`, resolve immediately with `{ skipped: true }` and short-circuit (no fetch, no validation error). Import from the existing slice-006 shared module `src/Analyzer/Client/src/shared/opt-out-attribute.ts` — no extraction work needed; slice 006 already shared it. **This activates the `{ skipped: true }` branch declared by T032's return type — until this task lands, the helper never returns the skipped sentinel.**
- [ ] T043 [P] [US2] Vitest test: `src/Analyzer/Client/src/features/search-tracking/opt-out.test.ts` — stub `isOptedOut()` to return `true`, invoke `sendSearch("anything", 3)` ten times, assert `fetch` was never called and every Promise resolved with `{ skipped: true }`. Then stub it to return `false`, invoke once more, assert exactly one `fetch` fires.
- [ ] T044 [P] [US2] Vitest test: `src/Analyzer/Client/src/features/search-tracking/opt-out-per-call.test.ts` — proves per-call evaluation. Setup: stub `isOptedOut()` to return `true` on first call, `false` on second; invoke `sendSearch(...)` twice; assert first resolves `{ skipped: true }` (no fetch), second resolves `{ eventKey: '...' }` (one fetch). This is the key behavioural delta from slice 006's scroll observer.
- [ ] T045 [P] [US2] Integration test: `src/Analyzer.Tests/Integration/Search/OptOutComplianceTests.cs` — render a synthetic page via the test host with `<body analyzer-no-tracking>`; drive the search helper through 100 invocations; assert zero `analyzerSearchEvent` rows + zero `AnalyzerSearchEventCaptured` audit-log entries.

**Checkpoint**: US2 complete — opt-out attribute respected at the client boundary on every call; defence-in-depth confirmed (no server-side opt-out logic needed because the client never POSTs).

---

## Phase 5: Polish & Cross-Cutting Concerns

**Purpose**: Public-surface pinning, perf-smoke baselines, audit-log redaction integration verification, contract-amendment note, and post-merge housekeeping.

- [ ] T046 [P] Public-surface pinning baseline regenerated via `ANALYZER_REGENERATE_SNAPSHOTS=1`. New members: `Analyzer.Analytics.IAnalyzerSearchQueryNormaliser` (interface), `Analyzer.Analytics.AnalyticsSearchEvent` (record), `IAnalyticsEventStateProvider.CurrentRequestSearchEvents` (additive interface member). Diff is purely additive. Baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt`.
- [ ] T047 [P] Perf-smoke test: `src/Analyzer.Tests/Perf/SearchThroughputSmokeTests.SustainedRate_one_hundred_searches_persist_within_1s_each` — 100 dispatches, asserts ≥99 / 100 complete within 1 s (SC-001 envelope). `[Trait("Category", "Perf")]`.
- [ ] T048 [P] Perf-smoke test: `src/Analyzer.Tests/Perf/SearchThroughputSmokeTests.Cascade_one_thousand_rows_under_two_hundred_ms` — seeds 1 000 rows, asserts cascade DELETE under 200 ms (SC-004). `[Trait("Category", "Perf")]`.
- [ ] T049 [P] **COVERED AT UNIT + INTEGRATION LEVEL** — audit-log PII-redaction fidelity (SC-006) is verified by `AnalyzerSearchEventAuditorTests` (unit; asserts log parameter dictionary contains no `RawQuery` / `NormalisedQuery` keys) + `EndToEndCaptureTests` (integration; asserts zero log substrate entries containing the query text). The two layers together are sufficient; an additional dedicated test would duplicate coverage. Documented here for /speckit-analyze visibility — same rationale slice 006 applied to its audit-log path (T048).
- [ ] T050 [P] **COVERED AT UNIT LEVEL** — identity-gate behaviour (SC-005) is verified by `AnalyzerSearchEventCaptureHandlerTests.RejectsUnavailableActor` + `RejectsEmptyVisitorKey` + controller `AnonymousReturns401` + `IdentityUnavailableReturns403`. Per-layer coverage is sufficient; an HTTP-boundary integration test would re-prove the same property and stays blocked on issue #23 (mgmt-API 404 in test host).
- [ ] T051 [P] **PR description note** — flag the cascade-disposition divergence from contract §3 D8 (this slice ships hard-delete; contract says re-key). Cite spec Clarifications §2 + research §R8 + contracts/AnalyzerSearchEventCascadeStep.md. Recommend a follow-up commit to `docs/INTER-PRODUCT-CONTRACT.md` amending D8's `analyzerSearchEvent` row to "hard-delete (PII per FR-SRC-04)" — can land Analyzer-side alone since contract D8 is an Analyzer-owned doc. This is a meta-task: the change itself is the PR description text, not a code change.
- [ ] T052 [P] Optional housekeeping: amend `docs/INTER-PRODUCT-CONTRACT.md` §3 D8 row for `analyzerSearchEvent` from "re-key to anonymised visitor key" to "hard-delete (PII per FR-SRC-04)". Single-line edit. Lands in the same PR as the slice (or as a fast-follow). T051 captures the PR-description note; T052 captures the doc edit.
- [ ] T053 Run quickstart.md walkthrough end-to-end on a freshly-built Aspire AppHost. **User-driven** — captures results in the PR description; not gated on automated CI. If any step fails, file a follow-up issue and tag it `slice-007-followup`.
- [ ] T054 Post-merge housekeeping (after PR merges to `main`): reset the CLAUDE.md SPECKIT block back to its "Last shipped" form pointing at slice 007 (new slice-007 head commit), matching the slice 002-006 cadence. Move project board #9 item for the slice-007 tracker (if filed) or update the PR's project-board card to Status=Done.

---

## Deferred items (slice-scoped, ship with this PR's "Known limitations")

The following deferred items mirror slice 004 + 005 + 006 precedent and are NOT blockers for slice 007 ship:

- **HTTP-boundary integration test for the management endpoint** (would belong in `EndToEndCaptureTests.cs` as a `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`-driven case): gated on issue #23 (mgmt-API 404 in the WAF test host). Slices 004 + 005 + 006 left the same gap. When #23 lands, add a single test that POSTs via the WAF client and asserts the round-trip.
- **Click-through attribution (`FR-SRC-03`)**: out of scope for v1 per spec FR-011 + Assumptions. Becomes a derived read-side metric in the eventual Events-report slice that joins `analyzerSearchEvent` to subsequent `customizerPageview` rows in the same session. The `IDX_analyzerSearchEvent_pageview` index shipped here is the prerequisite for that join.
- **Custom-normaliser conformance-test scaffold for third parties**: `ContractConformanceTestBase<TNormaliser>` referenced in `contracts/IAnalyzerSearchQueryNormaliser.md` is a nice-to-have for downstream-implementer ergonomics. Ship only the production interface in v1; the conformance base lands in slice-007-followup if/when an external host registers a custom normaliser.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories. T009 (fixture) MUST land before T010 (tests that consume it). T013 (session-kind extension) is sequential vs the `[P]` set because it touches a shared file (`SessionActivityKind.cs`).
- **US1 (Phase 3)**: Depends on Foundational; everything inside US1 is independently shippable as the slice MVP. T021 (handler impl) depends on T017/T018 (repo) + T023 (auditor) + T011 (command) + T012 (validation exception) + the foundational normaliser registration. T027 (composer) depends on T017/T020/T023/T025/T028.
- **US2 (Phase 4)**: Depends on Foundational + US1's `send-search.ts` (T032) existing — T042 edits that file to insert the per-call opt-out check. T042 MUST NOT run before T032.
- **Polish (Phase 5)**: Depends on US1 + US2 being complete (perf-smoke needs a working capture path; pinning needs the public surface stable).

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational (Phase 2). Self-contained.
- **US2 (P2)**: Depends on US1's `send-search.ts` shipping (T032). The per-call opt-out check is a small additive change inside that file.

### Within Each User Story

- DTOs / domain commands / public records before services (Phase 2).
- Repo + auditor + normaliser before handler (handler depends on all three).
- Handler before controller (controller depends on the handler).
- Server before client OR develop in parallel using the contracted payload shape (slice-004/005/006 precedent).
- Tests are written alongside their target file (no strict TDD; xUnit + Vitest harnesses already wired).

### Parallel Opportunities

- All `[P]` tasks in Phase 2 can run in parallel (T003, T004, T006, T007, T008, T009, T010, T011, T012, T014). Caveat: T009 must complete before T010 runs successfully; if you launch them together, T010 will need a quick rerun after T009 commits.
- Within US1: client modules (T030-T033), repo interface (T017), handler interface (T020), auditor (T023), controller (T025), cascade step (T028), and most unit + integration tests (T022, T024, T026, T029, T035-T041) are parallelisable.
- Polish tasks T046-T052 all parallelisable (T053 + T054 are user-driven / post-merge — naturally sequenced).

---

## Parallel Example: Phase 2 Foundational

```bash
# Launch all parallelisable Foundational tasks together:
Task: "DTO AnalyzerSearchEventDto in src/Analyzer/Features/Search/Infrastructure/Persistence/AnalyzerSearchEventDto.cs"
Task: "Migration M0007_AddAnalyzerSearchEventTable in src/Analyzer/Migrations/M0007_AddAnalyzerSearchEventTable.cs"
Task: "Public record AnalyticsSearchEvent in src/Analyzer/Analytics/AnalyticsSearchEvent.cs"
Task: "Public extension-point IAnalyzerSearchQueryNormaliser in src/Analyzer/Analytics/IAnalyzerSearchQueryNormaliser.cs"
Task: "Default normaliser impl in src/Analyzer/Features/Search/Application/Normalisation/DefaultAnalyzerSearchQueryNormaliser.cs"
Task: "100-pair fixture in src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json"
Task: "Default normaliser unit tests in src/Analyzer.Tests/Unit/Features/Search/Application/DefaultAnalyzerSearchQueryNormaliserTests.cs"  # depends on fixture
Task: "Domain command AnalyzerSearchEventCapture in src/Analyzer/Features/Search/Domain/AnalyzerSearchEventCapture.cs"
Task: "Validation exception AnalyzerSearchPayloadValidationException in src/Analyzer/Features/Search/Domain/"
Task: "Extend IAnalyticsEventStateProvider with CurrentRequestSearchEvents"
```

T005 (migration plan chaining), T013 (resolver enum + dispatch — shared file), T015 (state-store extension), and T016 (regression gate) are sequential after the `[P]` set lands.

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (CRITICAL — blocks US1).
3. Complete Phase 3: US1.
4. **STOP and VALIDATE**: walk quickstart §1 manually; the slice is shippable here even without US2.

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → search submissions captured + normalised + cascade-deletable + PII-redacted-in-logs → ship-candidate-1.
3. US2 → opt-out layered on (per-call evaluation) → ship-candidate-2.
4. Polish → pinning + perf-smoke + contract D8 amendment → ship.

### Slice-ship cadence (slice 003 / 004 / 005 / 006 precedent)

3-commit push-through onto `007-search-tracking`:
- **Commit A**: Setup + Foundational (T001-T016). One CI-green commit.
- **Commit B**: US1 (T017-T041). One CI-green commit.
- **Commit C**: US2 + Polish (T042-T054). One CI-green commit.

Then `git push -u origin 007-search-tracking`, open PR, rebase-merge to `main` (squashing if requested by the user). Post-merge: T054 housekeeping.

---

## Notes

- `[P]` tasks = different files, no dependencies on incomplete tasks.
- `[Story]` label maps each task to its user story for traceability.
- Tests are written alongside their target file (slice-004/005/006 precedent — no strict TDD; harnesses already wired from slice 001).
- **Audit-log PII-redaction (SC-006) discipline is load-bearing for slice 007's compliance posture.** Reviewers MUST sanity-check T023 + T024 specifically: any log template that includes `{RawQuery}` or `{NormalisedQuery}` placeholders — even unused — is a constitutional violation under Principle VII and FR-SRC-04. Run a grep for `Query=` across the slice-007 source before merge.
- **Cascade-disposition divergence from contract D8 (hard-delete vs re-key) is intentional** — documented in spec Clarifications §2, research §R8, contracts/AnalyzerSearchEventCascadeStep.md. T051 (PR description note) + T052 (optional doc amendment) close the loop.
- After every Phase, run the foundational regression gate (T016's pattern): `dotnet test --filter "Category!=Integration&Category!=Perf"` to ensure no slice-002/003/004/005/006 unit suite has regressed.
- Slice envelope projection: **54 tasks across 5 phases** — top end of the plan's 40-55 estimate. Drivers vs slice 006: new public extension point adds ~4 tasks (T007 interface + T008 default impl + T009 fixture + T010 fixture-driven tests + T041 custom-impl override integration test); visitor-bound pageviewKey check adds 1 integration test (T038); PII-redaction discipline adds 1 dedicated unit-test concern inside T024 + the cleanup verification inside T039; contract D8 amendment adds 2 polish tasks (T051 + T052). The two task pairs (T013 / T015 / T016 unchanged from slice 006's T010 / T013 / T014; T039 mirroring slice 006's T037) keep the slice envelope predictable.
