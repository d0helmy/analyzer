# Phase 0 Research — Package Skeleton

**Feature**: `001-package-skeleton`
**Date**: 2026-05-18

This document records the decisions made before design (Phase 1). No `NEEDS CLARIFICATION` markers remain from spec or plan; the items below were the load-bearing choices behind the plan's Technical Context.

---

## R1 — How does Analyzer's identity seam read from Customizer's identity layer?

**Decision**: `Analyzer.Features.Visitors.Application.VisitorIdentifier` resolves the current visitor by injecting Customizer's `Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile` (per-request DI surface) and projecting its `IsAvailable` / `VisitorKey` / `IdentityRef` members into Analyzer's own seam shape.

Customizer's `IPersonalizationProfile.IdentityRef` is the canonical `oid:<guid>` / `upn:<name>` / `anonymized:<key>` string. Analyzer parses this into a strongly-typed `VisitorIdentity` value (canonical Guid key + display form) at the boundary, so downstream Analyzer code never deals with the prefix-encoded string.

**Rationale**:
- Customizer already implements the `oid`-first / `upn`-fallback rule (verified at `../customizer/src/Customizer/Features/Visitors/Domain/VisitorIdentity.cs`); duplicating that logic in Analyzer would violate Constitution Principle III by creating a second source of truth.
- `IPersonalizationProfile` is one of Customizer's pinned public contracts (per the audit of `PublicSurfacePinningTests`), so Analyzer is binding to a surface that won't move silently.
- The `IsAvailable` flag is the documented FR-002-equivalent degraded-path signal in Customizer; Analyzer's "no identity" sentinel maps directly onto it.

**Alternatives considered**:
- *Read `ClaimsPrincipal` directly*: rejected — bypasses Customizer's already-implemented claim parsing, creates dual-source-of-truth risk, breaks Principle III.
- *Read `IAnalyticsStateProvider.CurrentPageviewKey`*: rejected for slice 001 — that surface is the pageview-level read seam (slice 002's concern). At slice 001 we only need visitor identity, not pageview state.
- *Define a new `IVisitorIdentityReader` in Customizer*: rejected — adds a new public type to Customizer's pinned surface; Constitution Principle III says no Customizer retrofit beyond the single `PageviewCaptured` notification already shipped.

---

## R2 — How is Customizer's presence verified at composition time (FR-002 fail-fast)?

**Decision**: `AnalyzerComposer` (an Umbraco `IComposer`) probes the host's `IServiceCollection` for a registered service descriptor whose service type is `IPersonalizationProfile` (the contract Analyzer reads from). If absent, the composer throws a single `AnalyzerCompositionException` with a message naming Customizer as the missing prerequisite and pointing at `INTER-PRODUCT-CONTRACT.md`. No Analyzer service is registered if the check fails.

**Rationale**:
- Checking for a service descriptor (not a resolved instance) avoids triggering Customizer's own composer side-effects from inside Analyzer's composer.
- `IPersonalizationProfile` is the surface Analyzer actually depends on, so its presence is the load-bearing precondition, not the presence of the Customizer assembly per se.
- Single-throw / no-partial-registration semantics match the spec's Acceptance Scenario 2 ("startup fails fast at composition time with a single, explicit error message naming Customizer as a hard prerequisite").

**Alternatives considered**:
- *Assembly probe by name*: rejected — brittle to NuGet vs project-reference shape; doesn't catch a Customizer installed but mis-configured (composer never ran).
- *Lazy check on first request*: rejected — defers failure past startup; violates FR-002 fail-fast semantics.
- *Customizer-presence sentinel type registered for Analyzer to detect*: rejected — would require touching Customizer to add the sentinel, violating Constitution Principle III.

---

## R3 — Backoffice bundle registration via `umbraco-package.json`

**Decision**: A single `umbraco-package.json` lives at `src/Analyzer/Client/public/umbraco-package.json`, declares `analyzer.js` as the bundled entrypoint at `wwwroot/App_Plugins/Analyzer/analyzer.js`. The `index.ts` source sets a single `window.Analyzer = { version }` token (no callable APIs, no UI elements registered) per spec Clarification Q4. The version field is populated from the package's published version at build time via Vite's `define` plugin.

**Rationale**:
- The `App_Plugins/<name>/umbraco-package.json` convention is the Umbraco 14+ standard for loading backoffice extensions; staying on-convention means zero custom plumbing for slice 001.
- An empty bundle + token (Q4 Option B) gives US3 acceptance scenario 2 a clean inspector hook ("Analyzer's presence is detectable... via a registered namespace or manifest entry") without committing to any client API surface that slice 004 might want to redesign.

**Alternatives considered**:
- *No client bundle at slice 001*: rejected — defers the bundle-loading channel verification past the point where it's cheap to establish; US3 covers this on purpose.
- *Stub `analyzer.send()`*: rejected per Clarification Q4 — risks fossilizing an API signature that should be designed alongside the server-side event-recording path (slice 004).
- *Pre-register backoffice content-app element*: rejected per Clarification Q4 — slice 005 is when the content app gets its data sources, and pre-registering an empty slot adds churn risk.

---

## R4 — Integration test host pattern

**Decision**: A `tests/Analyzer.Tests/TestHelpers/UmbracoTestHost.cs` helper bootstraps a minimal Umbraco service collection in-process for integration tests. Customizer is wired in via project reference. SQLite is used as the storage seam when integration tests touch Customizer's substrate (mirrors Customizer's slice-003 SQLite seam pattern). For composer-only smoke tests, an in-memory `IServiceCollection` without storage is sufficient.

**Rationale**:
- Customizer already has this exact pattern (per the audit: "xUnit v3 + FluentAssertions + SQLite/SQL Server test seams"); reusing the shape keeps Analyzer's test discipline symmetrical with Customizer's and lets future test helpers be lifted/shared if it becomes warranted.
- In-process boot is the right level of fidelity for slice 001's three User Stories — none requires a real HTTP pipeline, only that the DI graph composes and `IVisitorIdentifier` resolves with a synthetic `HttpContext`.
- SQLite seam covers the "fail-fast on missing Customizer" composer test by setting up a host *without* Customizer and asserting the expected exception.

**Alternatives considered**:
- *No integration tests; observe in a real host post-install*: rejected per Clarification Q2 — slice 001's testable claims (fail-fast, identity branches, bundle load) are valuable to gate at commit time, not at release time.
- *Spin up a full Umbraco web host (TestServer)*: rejected for slice 001 — overkill for three User Stories where the HTTP pipeline isn't itself under test.
- *Reuse Customizer's test host directly via NuGet*: rejected — Customizer doesn't ship its test helpers as a package (and shouldn't); copying the pattern is fine.

---

## R5 — Customizer dependency declaration shape in `Analyzer.csproj`

**Decision**: `Directory.Packages.props` declares `<PackageVersion Include="Customizer" Version="[<slice-011-version>,)" />` (lower bound inclusive, no upper bound), per spec Clarification Q1 (anticipated minimum = slice 011, commit `05e989c`). The actual NuGet version string for the slice-011 publish is filled in at implementation time when the slice-011 publish artifact's version is known.

**Rationale**:
- Lower-bound-only constraint reflects the always-deployed-together commitment (inter-product contract §1) while still failing at restore time if a host accidentally pins an older Customizer.
- Pinning at slice-011 (rather than the strictly-required slice-003) means slice 002 doesn't need to bump the dep — single PR for slice 002 stays narrow.
- Central Package Management keeps the version declaration in one place; the per-project `.csproj` only does `<PackageReference Include="Customizer" />`.

**Alternatives considered**:
- *Strict per-slice minimum (slice-003)*: rejected per Q1 — forces a dep bump in slice 002.
- *No version pin*: rejected per Q1 — silently accepts incompatible Customizer versions; weakens FR-002 fail-fast (the failure would surface as a missing-type runtime exception, not a clear restore-time error).
- *Project reference only (no NuGet)*: rejected per Q1 — works during development but doesn't translate to a real NuGet-published distribution shape.

---

## R6 — RCL packaging conventions for Umbraco 17.x

**Decision**: `Analyzer.csproj` declares `<TargetFramework>net10.0</TargetFramework>`, references Umbraco 17.3.5 packages via Central Package Management, and ships the compiled `analyzer.js` bundle as an embedded static web asset under `wwwroot/App_Plugins/Analyzer/`. The Vite build emits directly to that path during development; CI runs `npm run build` before `dotnet pack` to produce the final NuGet artifact.

**Rationale**:
- `wwwroot/App_Plugins/<name>/` is the Umbraco 14+ convention for backoffice client bundles bundled inside an RCL — no separate static-files server, no manual copy step needed.
- Tying `npm run build` to a pre-`dotnet pack` step (likely via a `BeforeTargets="Pack"` MSBuild target or a CI script) means the published NuGet always contains a fresh bundle.

**Alternatives considered**:
- *Ship the bundle as a separate NuGet package*: rejected — splits the install into two packages with version-skew risk; Customizer ships one combined package, Analyzer matches.
- *Build the bundle at host install time*: rejected — pushes Node.js into the host's prerequisites; deploying organisations should not need to build the client.

---

## Outstanding items

**One outstanding item, deferred to implementation (T002):** the exact NuGet version string for the Customizer publish that contains the `PageviewCaptured` notification (slice-011, commit `05e989c`). The version is known by reference to that commit but not yet pinned as a NuGet version string in `Directory.Packages.props`. T002 resolves the lookup.

Aside from that single lookup, the plan and spec are fully resolved at this phase.
