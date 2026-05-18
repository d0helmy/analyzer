# Implementation Plan: Package Skeleton

**Branch**: `001-package-skeleton` | **Date**: 2026-05-18 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-package-skeleton/spec.md`

## Summary

Slice 001 establishes the Analyzer Razor Class Library skeleton — buildable, composer-wired into a host Umbraco 17.x site, with a fail-fast Customizer dependency check and a single `IVisitorIdentifier` (scoped) identity seam that reads through Customizer's existing identity layer (`oid`-first, `upn`-fallback per Constitution Principle I). A Vite-bundled backoffice client is registered via `umbraco-package.json` at `App_Plugins/Analyzer/` and exports a single detectable namespace token so the wiring channel is verifiable. No event recording, no management endpoints, no Analyzer-owned tables. Unit tests cover the seam's three identity branches; integration tests against a test Umbraco host exercise the three User Story acceptance scenarios end-to-end.

The Customizer dependency is declared with an anticipated minimum of slice 011 (commit `05e989c`, which publishes `PageviewCaptured` `INotification`) so slice 002 does not need to bump it.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (server); TypeScript 5.x (client)

**Primary Dependencies**:
- `Umbraco.Cms.Core` + `Umbraco.Cms.Web.Backoffice` 17.3.5 (pinned via Central Package Management)
- `Customizer` package, minimum version pinned to ≥ slice-011 (commit `05e989c`) per spec Clarification Q1
- `@umbraco-cms/backoffice` 17.3.5 (client)
- Build tooling: MSBuild (server), Vite + Rollup (client)

**Storage**: None owned by Analyzer at slice 001. Customizer owns the canonical `customizerVisitorProfile` and `customizerVisitorPageview` tables; Analyzer's `IVisitorIdentifier` is a read-through to Customizer's already-populated request-scoped identity state. No migration files in this slice.

**Testing**:
- **Unit**: xUnit v3 + FluentAssertions; mock `ClaimsPrincipal` / `HttpContext`; cover `IVisitorIdentifier` branches (oid-present, upn-fallback with warning log, unauthenticated → no-identity).
- **Integration**: a small test host bootstraps a real Umbraco 17.x service collection (mirrors Customizer's pattern) to verify composer wiring, Customizer-absent fail-fast, and request-scoped identity resolution. SQLite-seam where storage is touched; in-memory `IServiceCollection` otherwise.
- **Client**: a minimal Vite test (Vitest) asserts the bundle's `window.Analyzer` token export. Backoffice "loads without console errors" is asserted via the server-side integration test (HTTP 200 on the bundle URL + manifest registration); no Playwright/browser test at slice 001.
- **Public-surface pinning**: deferred to slice 002 per spec Clarification Q2.

**Target Platform**: Umbraco CMS 17.x hosts running .NET 10 on any platform supported by Umbraco (Linux / Windows / macOS for dev). Backoffice runs in evergreen browsers.

**Project Type**: Razor Class Library (server) + Vite-bundled TypeScript backoffice extension (client). Mirrors the layout established by Customizer (`src/Customizer/`, `src/Customizer.Tests/`).

**Performance Goals**:
- Composer registration adds < 50ms to host startup on a developer-grade machine (target; re-baseline if not met).
- Identity seam resolution: O(1) read from Customizer's request-scoped state; effectively no measurable cost.
- No NFR-PER-* targets from the requirements doc apply directly at slice 001 (those targets bind event recording, which is FR-008 out-of-scope here).

**Constraints**:
- No event recording or data write at slice 001 (FR-008).
- No anonymous identity path (Constitution Principle I).
- No modification of Customizer's pinned public surface (Constitution Principle III) — Analyzer does not import or re-export any `Customizer.Features.*.Application.Contracts.*` type into its own public namespace.
- Fail-fast on missing Customizer at composition (FR-002) — single explicit error message; no partial-registration state.
- Backoffice route prefix MUST NOT collide with `/umbraco/engage/...` (FR-007) — the concrete prefix string is deferred per Clarification Q5.

**Scale/Scope**:
- One PR, target 1–2 developer-days. ~6–10 server-side source files, ~4–6 client-side files, ~6–10 test files.
- Anticipated 13 further slices on this product per inter-product contract §4.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Per `.specify/memory/constitution.md` **v1.1.0** (amended from v1.0.0 during slice-001 to import five Customizer-derived principles VI–X; see Sync Impact Report at the head of the constitution).

| Principle | Gate question | Slice-001 verdict |
|---|---|---|
| I — EntraID-Only Identity (NON-NEGOTIABLE) | Does any new collection path record data without an authenticated EntraID identity? | **PASS** — slice 001 records nothing (FR-008). The identity seam exists and enforces oid-first / upn-fallback / no-anonymous-synthesis (FR-004, FR-005). |
| II — Spec-Grounded Scope with Declared Drops | Are any out-of-scope FR prefixes (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection, §6.2 cookie-consent features) cited as parity targets? | **PASS** — spec cites only in-scope parity targets (`FR-IDP-02`, `FR-IDP-04`, `FR-ID-05`, `NFR-USA-*`, `NFR-LIC-01/02`) and explicitly references `FR-DEP-*` as a *drop*, not a target. |
| III — Customizer Substrate, No Retrofit | Does any change modify Customizer's pinned public surface OR introduce a name collision with a Customizer public type? | **PASS** — `IVisitorIdentifier` is in `Analyzer.Features.Visitors.Application.Contracts`; deliberately distinct from Customizer's `Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile` and `IAnalyticsStateProvider`. No Customizer file is modified by slice 001. The Customizer dependency is declared at the slice-011 minimum, well past any pinning baseline. |
| IV — Additive-Only Storage, Cascade-Step Anonymisation | Does any new Analyzer event/side table lack a cascade-step registration or a FK to Customizer substrate? | **PASS (vacuous)** — slice 001 ships zero tables. Principle IV applies to slices that introduce them; slice 001 introduces none. (Role-gated UPN, previously part of IV, is now covered by Principle VII below.) |
| V — Slice-Driven Delivery via Speckit | Did the change reach this point via the speckit slice flow? Was Constitution Check applied at plan time? | **PASS** — slice flow is in progress: `/speckit-specify` → `/speckit-clarify` (5 questions resolved) → `/speckit-plan` (this gate). `/speckit-tasks` and `/speckit-implement` follow. |
| VI — Software Engineering Excellence | Does the slice follow SOLID + vertical-slice layout? Are public domain rules + extension contracts covered by automated tests? | **PASS** — `src/Analyzer/Features/Visitors/{Application/Contracts,Application,Domain}/` mirrors Customizer's vertical-slice layout. Test discipline (unit + integration; xUnit v3 + FluentAssertions + SQLite-seam where storage is touched) is mandated by spec Clarification Q2 and codified across all three User Story phases of `tasks.md`. |
| VII — Security by Design | Does any new management or reporting surface lack RBAC gating, lack an audit-log entry for state-changing actions, OR expose individual-level UPN data to a non-authorised user group? | **PASS (vacuous)** — slice 001 ships **no** management surface, **no** reporting surface, **no** UPN-displaying view, and **no** state-changing operator action. All four gate clauses apply to slices that introduce these; slice 001 introduces none. The `IVisitorIdentifier` seam returns `Oid`/`Upn` to in-process callers only — there is no externally-reachable surface to gate at slice 001. |
| VIII — Performance & Scalability First | Does any hot-path code introduce global locks, synchronous network I/O, N+1 access, OR bypass Customizer's slice-002 outbox for cross-boundary async work? | **PASS (vacuous)** — slice 001 has no hot-path code (FR-008 forbids event recording). The `IVisitorIdentifier` resolution is O(1) read-through of Customizer's already-populated per-request state; no I/O, no locks, no DB access. |
| IX — Umbraco-Native & Operator-First | Does any new backoffice UI use non-`@umbraco-cms/backoffice` primitives, OR does any new operator/analyst workflow require code changes? | **PASS** — backoffice bundle is empty + a detectable namespace token (spec Clarification Q4); no UI primitives are used yet. The Vite + `@umbraco-cms/backoffice` 17.3.5 toolchain is pinned in Tech Stack so future slices land on-convention by default. No operator/analyst workflow exists at slice 001. |
| X — Extensibility by Design | Does the new public-extension contract have DI registration with documented lifetime, a behaviour-compatible custom-impl story, and (where stable) pinning coverage? | **PASS** — `IVisitorIdentifier` is registered as **scoped / per-request** per spec Clarification Q3 (lifetime documented). A custom `IVisitorIdentifier` is behaviour-compatible because the interface is the only contract callers depend on. Public-surface pinning is intentionally deferred to slice 002 per Clarification Q2; Principle X explicitly permits this — the contract is "preview" at slice 001, not "announced as stable." When slice 002 introduces `IAnalyticsEventStateProvider`, the pinning landscape opens and both contracts are pinned together. |

**Result**: all ten gates PASS. No Complexity Tracking entries required. Proceeding to Phase 0.

### Post-design re-evaluation (after Phase 1)

After producing `research.md`, `data-model.md`, `contracts/IVisitorIdentifier.md`, and `quickstart.md`, the Constitution Check is re-applied per the speckit workflow. Re-evaluation findings (2026-05-18; constitution v1.1.0 ratified during slice-001):

- Principle I: still PASS. `data-model.md` confirms `IsAvailable=false` on unauthenticated requests with `Key=Guid.Empty`/`Oid=null`/`Upn=null` (no anonymous synthesis). The contract documents the `upn`-fallback warning log explicitly.
- Principle II: still PASS. Phase-1 artifacts cite only in-scope parity targets (`FR-ID-05`, `FR-IDP-02`, `FR-IDP-04`).
- Principle III: still PASS. The contract namespace `Analyzer.Features.Visitors.Application.Contracts.IVisitorIdentifier` is distinct from Customizer's pinned `IPersonalizationProfile` / `IAnalyticsStateProvider`. No Customizer file is modified by this plan.
- Principle IV: still PASS (vacuous). `data-model.md` reaffirms zero Analyzer-owned tables at slice 001.
- Principle V: still PASS. This plan completes the planning phase of the speckit slice flow; `/speckit-tasks` is the next phase.
- Principle VI (Software Engineering Excellence): still PASS. `tasks.md` codifies vertical-slice layout (`Features/Visitors/Application/...`), unit + integration test discipline (T017, T019, T020, T023), and behavioural test coverage for all 5 `IVisitorIdentifier` branches.
- Principle VII (Security by Design): still PASS (vacuous). No management surface, no UPN-displaying view, no audit-worthy state-changing action introduced by Phase-1 artifacts.
- Principle VIII (Performance & Scalability): still PASS (vacuous). No hot-path or cross-boundary async work introduced.
- Principle IX (Umbraco-Native & Operator-First): still PASS. Empty bundle + token; no UI primitives used yet; backoffice toolchain pinned.
- Principle X (Extensibility by Design): still PASS. `contracts/IVisitorIdentifier.md` documents scoped DI lifetime, the behaviour-compatibility contract, and the explicit deferral of public-surface pinning to slice 002 (compliant with Principle X's "before announced as stable" clause).

No new Complexity Tracking entries. The plan is consistent with the constitution post-design.

## Project Structure

### Documentation (this feature)

```text
specs/001-package-skeleton/
├── plan.md                # this file (Phase 2 output of /speckit-plan)
├── spec.md                # already committed at d19b712
├── checklists/
│   └── requirements.md    # already committed at d19b712
├── research.md            # Phase 0 output (this command)
├── data-model.md          # Phase 1 output (this command)
├── quickstart.md          # Phase 1 output (this command)
├── contracts/
│   └── IVisitorIdentifier.md   # Phase 1 output (this command)
└── tasks.md               # Phase 2 output (/speckit-tasks — NOT this command)
```

### Source Code (repository root)

Mirrors Customizer's `src/Customizer/` and `src/Customizer.Tests/` layout for symmetry between the two products.

```text
Analyzer.slnx                                            # solution
Directory.Packages.props                                 # Central Package Management (Umbraco 17.3.5, Customizer >= slice-011, xUnit v3, FluentAssertions)
docs/                                                    # specs + reference + inter-product contract (already exists)
specs/                                                   # per-slice specs (already in use)

src/
└── Analyzer/
    ├── Analyzer.csproj                                  # RCL targeting net10.0
    ├── Constants.cs                                     # static class — App_Plugins path token, future-route-prefix placeholder
    ├── Composers/
    │   └── AnalyzerComposer.cs                          # IComposer: validates Customizer presence, registers IVisitorIdentifier (scoped)
    ├── Features/
    │   └── Visitors/
    │       ├── Application/
    │       │   ├── Contracts/
    │       │   │   └── IVisitorIdentifier.cs            # public seam contract
    │       │   └── VisitorIdentifier.cs                 # implementation: reads Customizer.IPersonalizationProfile / equivalent
    │       └── Domain/                                  # (empty at slice 001; future-slice extension point)
    └── Client/
        ├── package.json                                 # Vite + @umbraco-cms/backoffice 17.3.5
        ├── tsconfig.json
        ├── vite.config.ts
        ├── public/
        │   └── umbraco-package.json                     # declares wwwroot/App_Plugins/Analyzer/analyzer.js entrypoint
        └── src/
            └── index.ts                                 # sets window.Analyzer = { version }; no callable APIs

src/Analyzer.Tests/                                      # test project
├── Analyzer.Tests.csproj                                # xUnit v3 + FluentAssertions + Customizer integration helpers
├── Unit/
│   └── Features/Visitors/Application/
│       └── VisitorIdentifierTests.cs                    # oid-present, upn-fallback (asserts warning log), no-identity branches
├── Integration/
│   └── HostBoot/
│       ├── ComposerSmokeTests.cs                        # clean boot with Customizer; fail-fast without Customizer
│       ├── IdentitySeamTests.cs                         # request-scoped resolution end-to-end
│       └── BackofficeBundleTests.cs                     # GET on App_Plugins URL returns 200; window.Analyzer token check is in the Vitest spec below
└── TestHelpers/
    └── UmbracoTestHost.cs                               # bootstraps a minimal Umbraco service collection + SQLite seam where storage is touched

src/Analyzer/Client/                                     # Vitest unit test alongside the bundle
└── src/
    └── index.test.ts                                    # asserts window.Analyzer = { version } after bundle import
```

**Structure Decision**: feature-folder vertical-slice layout mirroring Customizer (`Features/<Domain>/{Application,Domain,Infrastructure}/`) — chosen for symmetry between the two co-deployed products. Tests live in a parallel `Analyzer.Tests` project with `Unit/` and `Integration/` separation. Client bundle lives under `src/Analyzer/Client/` per the Constitution Tech Stack constraint, with its own Vitest co-located.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*Constitution Check produced zero violations. This section is intentionally empty.*
