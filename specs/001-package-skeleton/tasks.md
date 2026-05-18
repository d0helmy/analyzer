---

description: "Tasks for slice 001 — Package Skeleton"
---

# Tasks: Package Skeleton

**Input**: Design documents from `specs/001-package-skeleton/`

**Prerequisites**: `plan.md` (required), `spec.md` (3 user stories), `research.md`, `data-model.md`, `contracts/IVisitorIdentifier.md`, `quickstart.md`

**Tests**: Included — spec Clarification Q2 requires unit + integration tests for slice 001 (`SC-006`).

**Organization**: Tasks are grouped by user story (US1, US2, US3) to enable independent verification of each acceptance scenario. The Foundational phase contains the wiring every story depends on; story phases add the tests that prove each User Story's acceptance scenarios.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: Maps to user story from spec (`US1`, `US2`, `US3`); Setup / Foundational / Polish have no story label
- File paths are repo-root-relative

## Path Conventions

Mirrors Customizer's layout (per `plan.md` Project Structure):

- Server source: `src/Analyzer/...`
- Tests: `src/Analyzer.Tests/...`
- Client: `src/Analyzer/Client/...`
- Bundled output: `src/Analyzer/wwwroot/App_Plugins/Analyzer/analyzer.js`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization — solution, projects, build configuration, central package management.

- [X] T001 [P] Create solution file `Analyzer.slnx` at repo root (slnx format; will accept projects added in T009)
- [X] T002 [P] Create `Directory.Packages.props` at repo root with Central Package Management enabled. Pin: `Umbraco.Cms.Core` and `Umbraco.Cms.Web.Backoffice` at `17.3.5`; `Customizer` with lower bound `[<slice-011-version>,)` (resolve concrete NuGet version of commit `05e989c` during this task; per research R5); `xunit.v3`, `xunit.v3.assert`, `FluentAssertions` for tests
- [X] T003 [P] Create RCL project file `src/Analyzer/Analyzer.csproj`: `<TargetFramework>net10.0</TargetFramework>`, references `Umbraco.Cms.Core` + `Umbraco.Cms.Web.Backoffice` + `Customizer`, declares `<PackageType>UmbracoCmsPackage</PackageType>`, includes `wwwroot/App_Plugins/Analyzer/**` as static web assets
- [X] T004 [P] Create test project file `src/Analyzer.Tests/Analyzer.Tests.csproj`: `<TargetFramework>net10.0</TargetFramework>`, `<IsPackable>false</IsPackable>`, references `xunit.v3` + `FluentAssertions`, `<ProjectReference>` to `src/Analyzer/Analyzer.csproj` and `../customizer/src/Customizer/Customizer.csproj`
- [X] T005 [P] Create `src/Analyzer/Constants.cs` declaring `AppPluginsPath = "App_Plugins/Analyzer"`, `BackofficeBundleFileName = "analyzer.js"`, `PackageName = "Analyzer"` (no concrete route prefix yet — deferred per Clarification Q5)
- [X] T006 [P] Create client manifest `src/Analyzer/Client/package.json`: declares dev-dependencies `vite`, `@umbraco-cms/backoffice@17.3.5`, `vitest`, `typescript@5.x`; scripts `build`, `watch`, `test`
- [X] T007 [P] Create `src/Analyzer/Client/tsconfig.json` (target ES2022, strict mode, JSX-react if used by Umbraco backoffice — verify against `@umbraco-cms/backoffice` 17.3.5 conventions during this task)
- [X] T008 [P] Create `src/Analyzer/Client/vite.config.ts`: output `analyzer.js` to `../wwwroot/App_Plugins/Analyzer/`, copy `public/umbraco-package.json` alongside, inject version from `package.json` via `define: { __ANALYZER_VERSION__: ... }`
- [X] T009 [P] Create `.gitignore` at repo root covering `bin/`, `obj/`, `node_modules/`, `dist/`, `src/Analyzer/wwwroot/App_Plugins/Analyzer/` (built artifact; rebuilt on `dotnet pack`)
- [X] T010 Add `src/Analyzer/Analyzer.csproj` and `src/Analyzer.Tests/Analyzer.Tests.csproj` to `Analyzer.slnx` (`dotnet sln Analyzer.slnx add ...`). Depends on T001 + T003 + T004.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Wire the artifacts every user story exercises. The `IVisitorIdentifier` seam + the `AnalyzerComposer` (which registers it and verifies Customizer presence) are foundational because US1 tests composer behavior, US2 tests seam behavior, and US3 indirectly exercises the host being booted (which requires composer success).

**⚠️ CRITICAL**: No user story phase can run until this phase is complete.

- [X] T011 [P] Define `IVisitorIdentifier` interface at `src/Analyzer/Features/Visitors/Application/Contracts/IVisitorIdentifier.cs` per `contracts/IVisitorIdentifier.md` (signature: `VisitorIdentity GetCurrent()`; XML docs cite Constitution Principle I and `FR-ID-05`)
- [X] T012 [P] Define `VisitorIdentity` `readonly record struct` at `src/Analyzer/Features/Visitors/Application/Contracts/VisitorIdentity.cs` per `data-model.md` (fields: `IsAvailable`, `Key`, `Oid`, `Upn`, `IsAnonymized`; invariants documented in XML docs)
- [X] T013 [P] Define `AnalyzerCompositionException : Exception` at `src/Analyzer/Composers/AnalyzerCompositionException.cs` (thrown by composer when Customizer is absent; single-arg ctor accepting the explanation string)
- [X] T014 Implement `VisitorIdentifier : IVisitorIdentifier` at `src/Analyzer/Features/Visitors/Application/VisitorIdentifier.cs`: injects `Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile` + `ILogger<VisitorIdentifier>`; parses `IPersonalizationProfile.IdentityRef` (`oid:` / `upn:` / `anonymized:` prefixes) into `VisitorIdentity`; emits warning log on `upn`-fallback per research R1. Depends on T011 + T012.
- [X] T015 Implement `AnalyzerComposer : IComposer` at `src/Analyzer/Composers/AnalyzerComposer.cs`: in `Compose(IUmbracoBuilder builder)`, probes `builder.Services` for a registered descriptor of `IPersonalizationProfile`; throws `AnalyzerCompositionException` with a message naming Customizer + linking `docs/INTER-PRODUCT-CONTRACT.md` if absent; otherwise registers `IVisitorIdentifier → VisitorIdentifier` as scoped. Per research R2. Depends on T011 + T013 + T014.
- [X] T016 [P] Implement `UmbracoTestHost` helper at `src/Analyzer.Tests/TestHelpers/UmbracoTestHost.cs`: factory methods `BuildWithCustomizer()` and `BuildWithoutCustomizer()`; uses an in-memory `IServiceCollection` with a minimal Umbraco service surface and Customizer wired in (or omitted); supports injecting a fake `IPersonalizationProfile` to simulate identity claims. Per research R4.

**Checkpoint**: Foundation complete. `dotnet build Analyzer.slnx` succeeds. User-story phases can begin in parallel.

---

## Phase 3: User Story 1 — Operator installs Analyzer alongside Customizer (Priority: P1) 🎯 MVP

**Goal**: Verify a host with Analyzer + Customizer boots cleanly; a host with Analyzer but no Customizer fails fast at composition with a single explicit error.

**Independent Test**: Run `ComposerSmokeTests` — both pass/fail scenarios assert on the composer's behavior in isolation from US2/US3.

### Tests for User Story 1 ⚠️

> Write tests FIRST; ensure they FAIL before implementation. (Implementation lives in Phase 2 foundational tasks T013 + T015; this Phase 3 task adds the verification harness.)

- [X] T017 [US1] Write `ComposerSmokeTests` at `src/Analyzer.Tests/Integration/HostBoot/ComposerSmokeTests.cs`:
   - `WithCustomizer_ComposesSuccessfully_RegistersIVisitorIdentifier` — uses `UmbracoTestHost.BuildWithCustomizer()`; asserts `IServiceProvider.GetService<IVisitorIdentifier>()` resolves a non-null `VisitorIdentifier` (covers spec US1 AS1).
   - `WithoutCustomizer_FailsFast_ThrowsAnalyzerCompositionException` — uses `UmbracoTestHost.BuildWithoutCustomizer()`; asserts `Compose(...)` throws `AnalyzerCompositionException` whose message contains "Customizer" and "INTER-PRODUCT-CONTRACT.md" (covers spec US1 AS2).
   - `WithCustomizer_HostBoots_NoAnalyzerErrorsInLog` — fully boots the host; captures `ILogger` output; asserts no `Analyzer` errors logged (covers spec US1 AS3).
   Depends on T015 + T016.

### Implementation for User Story 1

*(All implementation lives in Phase 2 foundational tasks T013 + T015. No additional implementation tasks here — US1's value is the verified behavior of the composer.)*

- [X] T018 [US1] Execute `dotnet test --filter "FullyQualifiedName~ComposerSmokeTests"`; verify all three scenarios pass; record the run output (depends on T017)

**Checkpoint**: User Story 1 verified. Operator can install Analyzer alongside Customizer with confidence; missing-Customizer case fails fast as designed.

---

## Phase 4: User Story 2 — Identity seam resolves visitor identity (Priority: P2)

**Goal**: `IVisitorIdentifier.GetCurrent()` returns the correct `VisitorIdentity` for every claim shape: `oid+upn`, `upn`-only (with warning log), `oid`-only, no identity, anonymized.

**Independent Test**: Unit tests cover the 5 branches against mock claims; integration test exercises end-to-end resolution via the wired composer.

### Tests for User Story 2 ⚠️

> Write tests FIRST; ensure they FAIL before T014 (the implementation) is complete.

- [X] T019 [P] [US2] Write `VisitorIdentifierTests` at `src/Analyzer.Tests/Unit/Features/Visitors/Application/VisitorIdentifierTests.cs` covering all five branches per `contracts/IVisitorIdentifier.md`:
   - `GivenOidAndUpn_ReturnsAvailable_OidIsCanonical`
   - `GivenUpnWithoutOid_ReturnsAvailable_LogsWarningOnce`
   - `GivenOidWithoutUpn_ReturnsAvailable_NoWarning`
   - `GivenNoIdentity_ReturnsNotAvailable_NoLog`
   - `GivenAnonymized_ReturnsAvailable_OidAndUpnNull`
   Uses a fake `IPersonalizationProfile` + captured `ILogger<VisitorIdentifier>` (e.g. `NullLogger<T>` with a `TestLoggerProvider` for the warning assertion). Depends on T014.
- [X] T020 [P] [US2] Write `IdentitySeamTests` at `src/Analyzer.Tests/Integration/HostBoot/IdentitySeamTests.cs` covering spec US2 AS1, AS2, AS3:
   - `OidAndUpnPresent_ReturnsOidCanonical_UpnAsDisplay`
   - `UpnOnly_ReturnsUpnCanonical_LogsWarning`
   - `Unauthenticated_ReturnsNoIdentity_NoEventRecorded`
   Uses `UmbracoTestHost.BuildWithCustomizer()` with a synthetic `HttpContext` carrying various claim shapes. Depends on T015 + T016.

### Implementation for User Story 2

*(Implementation is T014 in Phase 2. Phase 4 tasks add tests + verification.)*

- [X] T021 [US2] Execute `dotnet test --filter "FullyQualifiedName~VisitorIdentifierTests|FullyQualifiedName~IdentitySeamTests"`; verify all five unit branches + three integration scenarios pass (depends on T019 + T020)

**Checkpoint**: User Story 2 verified. The identity seam is correct across all claim shapes and ready for slice 002's pageview-subscription consumers.

---

## Phase 5: User Story 3 — Backoffice bundle loads (Priority: P3)

**Goal**: Vite-built bundle is served from `App_Plugins/Analyzer/analyzer.js` with HTTP 200 and zero JavaScript console errors; `window.Analyzer = { version }` is detectable from the runtime.

**Independent Test**: Server-side integration test fetches the bundle URL and asserts 200; Vitest asserts the token export.

### Tests for User Story 3 ⚠️

> Write tests FIRST; ensure they FAIL before bundle exists.

- [X] T022 [P] [US3] Write `index.test.ts` at `src/Analyzer/Client/src/index.test.ts` (Vitest): imports the bundle's entry; asserts `window.Analyzer` is `{ version: <package-json-version> }`. Covers spec US3 AS2.
- [X] T023 [P] [US3] Write `BackofficeBundleTests` at `src/Analyzer.Tests/Integration/HostBoot/BackofficeBundleTests.cs`:
   - `BundleUrl_ReturnsHttp200` — boots host with Analyzer + Customizer; uses an `HttpClient` against an integration `TestServer`; asserts `GET /App_Plugins/Analyzer/analyzer.js` returns 200.
   - `ManifestUrl_ReturnsHttp200_AndDeclaresAnalyzerJs` — asserts `GET /App_Plugins/Analyzer/umbraco-package.json` returns 200 and the body declares `analyzer.js` as an entrypoint.
   Covers spec US3 AS1. Depends on T016.

### Implementation for User Story 3

- [X] T024 [P] [US3] Create `src/Analyzer/Client/public/umbraco-package.json` declaring `analyzer.js` as the bundled extension entrypoint (per Umbraco 17.x backoffice package manifest schema)
- [X] T025 [P] [US3] Create `src/Analyzer/Client/src/index.ts` setting `(globalThis as any).Analyzer = { version: __ANALYZER_VERSION__ }`. No other exports, no UI elements registered (per spec Clarification Q4)
- [X] T026 [US3] Add MSBuild target to `src/Analyzer/Analyzer.csproj`: `<Target Name="BuildClient" BeforeTargets="Build">` runs `npm install && npm run build` from `Client/`; gates on `Condition="!Exists('Client/node_modules')"` for incremental dev builds. Depends on T006 + T008 + T024 + T025.
- [X] T027 [US3] Run `npm install && npm run build` from `src/Analyzer/Client/`; verify `src/Analyzer/wwwroot/App_Plugins/Analyzer/analyzer.js` and `umbraco-package.json` emit. Depends on T024 + T025 + T026.
- [X] T028 [US3] Run `npm test` from `src/Analyzer/Client/`; verify T022 (`index.test.ts`) passes. Depends on T022 + T025.
- [X] T029 [US3] Run `dotnet test --filter "FullyQualifiedName~BackofficeBundleTests"`; verify both server-side bundle assertions pass. Depends on T023 + T027.

**Checkpoint**: User Story 3 verified. Backoffice bundle wiring channel is live and ready for slice 004+ to ship content through.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Verify Success Criteria SC-001 through SC-006 + slice-001 publish hygiene.

- [X] T030 [P] Verify `SC-001` (clean boot in one try): install the built RCL into a fresh Umbraco 17.x host alongside Customizer; record the boot log; confirm no Analyzer-attributable errors. Per `quickstart.md` "Smoke tests — US1".
- [X] T031 [P] Verify `SC-002` (build from clean checkout): from a fresh clone run `dotnet restore && dotnet build Analyzer.slnx && cd src/Analyzer/Client && npm install && npm run build`; confirm zero errors and within standard CI build-time expectations.
- [X] T032 [P] Verify `SC-003` (identity seam returns canonical key 100% of test cases): inspect the test report from T021; confirm all five branches + three integration cases pass on the first run.
- [X] T033 [P] Verify `SC-004` (bundle HTTP 200 + zero JS console errors): open the host backoffice in a browser; check DevTools Network for `/App_Plugins/Analyzer/analyzer.js` HTTP 200 and DevTools Console for zero analyzer-related errors. Per `quickstart.md` "Smoke tests — US3".
- [X] T034 [P] Verify `SC-005` (slice 002 unblocked): confirm `AnalyzerComposer` registers `IVisitorIdentifier` and that an imaginary slice 002 author can inject it with zero added wiring.
- [X] T035 [P] Verify `SC-006` (test suite passes): run `dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj` + `npm test` from `src/Analyzer/Client/`; confirm all unit + integration + Vitest tests pass.
- [X] T036 [P] Add LICENSE + version metadata to `src/Analyzer/Analyzer.csproj` (`<PackageLicenseExpression>`, `<Version>`, `<Authors>`, `<PackageTags>`) per `FR-009` and `NFR-LIC-01`/`NFR-LIC-02`. The license expression mirrors Customizer's choice (verify during this task).
- [X] T037 Run `quickstart.md` end-to-end on a fresh host: complete the US1 / US2 / US3 manual smoke tests as documented; record any deviation. Depends on T030 + T033.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No external dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories.** T011/T012/T013 can run in parallel; T014 depends on T011+T012; T015 depends on T011+T013+T014; T016 is independent of the others.
- **User Story 1 (Phase 3)**: Depends on Foundational. T017 depends on T015+T016. T018 depends on T017.
- **User Story 2 (Phase 4)**: Depends on Foundational. T019 + T020 can run in parallel after T014+T015+T016. T021 depends on T019+T020.
- **User Story 3 (Phase 5)**: Depends on Foundational (only T016 for the integration test seam; the bundle assets in T024/T025 are independent of the server). T024+T025 can run with Phase 4 in parallel.
- **Polish (Phase 6)**: Depends on all user story phases completing.

### User Story Dependencies (deployment-time independence)

- **US1 (P1)**: Once Foundational completes, can be verified standalone via T017+T018.
- **US2 (P2)**: Requires the same foundational tasks as US1; can be verified in parallel with US1 (different test files, same composer wiring).
- **US3 (P3)**: Independent of US1+US2 at the implementation level (client bundle is a separate file tree). Integration test T023 reuses `UmbracoTestHost`; otherwise unrelated.

### Critical Path

```
T001..T009 (Setup, parallel)
    └─► T010 (sln add)
         └─► T011 + T012 + T013 (parallel)
              └─► T014 (VisitorIdentifier)
                   └─► T015 (AnalyzerComposer)
                        └─► T017 + T019 + T020 + T023 (test stubs, parallel)
                             └─► T018 + T021 + T029 (verification runs)
                                  └─► T030..T037 (polish, mostly parallel)
                                       └─► DONE
```

### Parallel Opportunities

- All of T001–T009 (different scaffolding files) can be done in one batch.
- T011 + T012 + T013 (three independent .cs files) can be done together.
- T019 (unit tests) + T020 (integration tests) + T024 (manifest) + T025 (index.ts) can all be done in parallel after the foundational phase.
- T030–T036 (verification of success criteria) are independent measurements — all [P].

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Complete Phase 1 (Setup) — `dotnet build` succeeds against empty scaffolding.
2. Complete Phase 2 (Foundational) — composer + seam + test host built; project builds.
3. Complete Phase 3 (US1) — `ComposerSmokeTests` pass.
4. **STOP**: at this point slice 001 has shipped the minimum that lets a host install Analyzer alongside Customizer and boot cleanly. Could be released as an internal "0.1.0-alpha" pre-tag if needed.

### Incremental Delivery

1. Setup + Foundational + US1 → ship MVP (clean boot verified).
2. Add US2 → ship (identity seam available for downstream slices).
3. Add US3 → ship (backoffice bundle channel verified).
4. Polish (SC verifications + LICENSE metadata) → version-tag the slice and merge.

Each increment leaves the previous one intact; nothing is rewritten.

### Parallel Team Strategy

With a single agent (typical case):

1. Run Setup in one batch (T001–T009 + T010).
2. Run Foundational in two waves: T011/T012/T013 parallel, then T014→T015, then T016 parallel.
3. Run US1+US2+US3 implementations in parallel where possible (T017 / T019+T020 / T024+T025); their test executions serialize.
4. Run Polish verifications in parallel.

With two agents (less typical for a slice this small): one takes US2+US3, the other US1+US3 bundle integration test. Coordinate on the foundational phase as a synchronous prerequisite.

---

## Notes

- `[P]` tasks = different files, no incomplete dependencies.
- `[US1]` / `[US2]` / `[US3]` map directly to spec user-story numbering.
- Tests are mandatory in this slice per spec Clarification Q2; verify each test set FAILS before its implementation tasks complete.
- Commit cadence: at minimum, commit after T010 (setup complete), T015 (foundational complete), T018 (US1 verified), T021 (US2 verified), T029 (US3 verified), and T037 (polish complete). The `/speckit-git-commit` hook can be invoked at any of these checkpoints.
- The exact NuGet version string for Customizer slice-011 must be resolved during T002 (research outstanding item, R5).
