# Feature Specification: Package Skeleton

**Feature Branch**: `001-package-skeleton`

**Created**: 2026-05-18

**Status**: Draft

**Input**: User description: "scaffold the package skeleton"

## Clarifications

### Session 2026-05-18

- Q: How does Analyzer slice 001 declare its dependency on Customizer in `Analyzer.csproj`? → A: Anticipated minimum — pin to ≥ the Customizer version containing `PageviewCaptured` (slice 011, commit `05e989c`). Slice 002 (pageview-subscription) won't need to bump the dependency.
- Q: What test discipline does slice 001 ship with? → A: Unit + integration. Unit tests cover `IVisitorIdentifier` resolution branches (oid present, upn-fallback, no-identity) against a mock `ClaimsPrincipal`. Integration tests against a test Umbraco host exercise the three acceptance scenarios (clean boot, identity-seam resolution, bundle load). Customizer's xUnit v3 + FluentAssertions + SQLite-seam stack is the reference pattern. Public-surface pinning tests are deferred to slice 002 when `IAnalyticsEventStateProvider` introduces a richer surface worth pinning.
- Q: What DI lifetime does `IVisitorIdentifier` use? → A: Scoped (per-request). Matches `HttpContext` lifetime; idiomatic in ASP.NET Core / Umbraco; avoids the singleton foot-gun of accidentally caching one user's identity across requests if state is ever added.
- Q: What does the slice 001 backoffice bundle export? → A: Empty bundle with a detectable namespace token. The bundle exports a single marker (e.g. `window.Analyzer = { version }`) so US3 acceptance scenario 2 has a clean inspector hook, but no callable APIs. No `analyzer.send()` stub, no content-app skeleton — those are deferred to the slices that actually wire them server-side (custom events at slice 004, content app at slice 005 per inter-product contract §4).
- Q: Is the backoffice management-API route prefix pinned at slice 001, or deferred? → A: Deferred. Slice 001 hosts no management endpoints, so the actual prefix is set when the first endpoint lands (likely slice 005). FR-007's "distinct from `/umbraco/engage/...`" constraint still applies; only the concrete prefix string is deferred.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator installs Analyzer alongside Customizer in a host Umbraco site (Priority: P1)

A site reliability operator deploying the intranet stack installs the Customizer package and the Analyzer package side-by-side into an Umbraco 17.x host. The host application starts cleanly, the dependency-injection container resolves Analyzer's services, and no runtime errors appear in the application log during boot.

**Why this priority**: Without a buildable, host-installable package skeleton, every subsequent slice is unverifiable. This story delivers the load-bearing artifact that all later slices extend, and is the minimum viable definition of "Analyzer exists in production."

**Independent Test**: The operator provisions a fresh Umbraco 17.x site, adds project references (or NuGet references) to Customizer and Analyzer, and starts the application. Success = clean boot, no missing-dependency or container-resolution errors, and Analyzer's marker service is resolvable from `IServiceProvider`.

**Acceptance Scenarios**:

1. **Given** a fresh Umbraco 17.x host with Customizer installed and configured, **When** the operator adds the Analyzer package and starts the application, **Then** the application starts without errors and Analyzer's composition step appears in the startup log.
2. **Given** a host that has Analyzer installed **without** Customizer, **When** the application starts, **Then** startup fails fast at composition time with a single, explicit error message naming Customizer as a hard prerequisite (per Constitution Principle III).
3. **Given** Analyzer installed on the host, **When** an authenticated EntraID request reaches the rendering pipeline, **Then** no Analyzer-side error is logged and no event data is yet recorded (data-collection paths arrive in later slices).

### User Story 2 - Future slice resolves the current visitor's identity through Analyzer's identity seam (Priority: P2)

A developer working on a later Analyzer slice (e.g. custom events, sessions, scroll heatmap) needs to attribute an event to the current visitor. They resolve Analyzer's identity seam from DI, and for any authenticated EntraID request the seam returns the canonical visitor key (`oid`-first, `upn`-fallback) — without that slice needing to know how identity is resolved.

**Why this priority**: All event-recording slices depend on a single, consistent identity seam. Establishing this seam at slice 001 prevents every subsequent slice from re-inventing identity resolution and guarantees Principle I compliance everywhere.

**Independent Test**: A test host issues an authenticated EntraID request; the test resolves Analyzer's identity seam from DI and asserts the returned identifier equals the request's `oid` claim. A second test issues a request with `upn` but no `oid` and asserts the fallback path returns the `upn` value.

**Acceptance Scenarios**:

1. **Given** an authenticated request with both `oid` and `upn` claims, **When** the identity seam is queried, **Then** it returns the `oid` value as the canonical visitor key and the `upn` as the display form.
2. **Given** an authenticated request whose claims contain `upn` but no `oid`, **When** the identity seam is queried, **Then** it returns the `upn` value as both canonical key and display form, and emits a warning-level log indicating the configuration-error fallback (per Constitution Principle I).
3. **Given** an unauthenticated request, **When** the identity seam is queried, **Then** it returns "no identity" (the spec's "no anonymous fallback visitor" semantic — `FR-ID-05`) and no analytics event recording occurs downstream.

### User Story 3 - Operator loads the Analyzer backoffice bundle in the host backoffice without errors (Priority: P3)

When the operator opens the Umbraco backoffice after installing Analyzer, the host registers Analyzer's client bundle via the standard Umbraco package manifest and the bundle loads without console errors. The bundle currently contains no functional panels — those arrive in later slices — but its presence is verifiable and the registration channel is established.

**Why this priority**: The backoffice integration channel needs to exist before any visible Analyzer surface can ship. Establishing the empty-but-loadable bundle in slice 001 separates "channel works" from "channel carries content," so subsequent slices ship features through a known-good wiring path.

**Independent Test**: Open the Umbraco backoffice with Analyzer installed. Browser DevTools shows the Analyzer bundle loaded from `App_Plugins/Analyzer/` with HTTP 200 and zero JavaScript errors. The bundle exports a recognizable Analyzer namespace token (verifiable by inspecting the runtime).

**Acceptance Scenarios**:

1. **Given** a host with Analyzer installed, **When** the operator opens the Umbraco backoffice, **Then** the Analyzer client bundle is requested and loaded with HTTP 200, with no JavaScript errors logged to the browser console.
2. **Given** the bundle has loaded, **When** an inspector queries the backoffice runtime, **Then** Analyzer's presence is detectable (e.g. via a registered namespace or manifest entry) even though no visible UI panels are yet rendered.

### Edge Cases

- **Customizer absent at boot**: composition fails with an explicit, single-line error referencing the inter-product contract dependency. No partial-startup; no Analyzer DI registrations are made.
- **Customizer present but at an unsupported version**: composition fails with a version-mismatch error referencing the pinned Customizer version range. No silent fallback.
- **EntraID claims include neither `oid` nor `upn`**: this is a misconfigured external login provider on the host; the identity seam returns "no identity" and a warning is logged. No event recording proceeds for that request.
- **Two pageviews arrive concurrently for the same authenticated visitor**: the identity seam returns the same canonical key for both (deterministic). No race-condition handling is required at slice 001 because no Analyzer-owned writes occur yet.
- **Backoffice bundle 404 (missing or unbuilt asset)**: the operator sees a clear error path (HTTP 404 in browser DevTools and a log entry on the server) — composition itself does not fail, since the bundle is not load-bearing for server-side correctness.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Analyzer MUST ship as a single packageable artifact installable into an Umbraco 17.x host alongside Customizer, with no Engage-module runtime dependency (per `FR-DEP-*` scope drop).
- **FR-002**: Analyzer's composition step MUST register the Analyzer service graph into the host's DI container at application startup, and MUST fail fast with a single explicit error if the Customizer dependency is absent (per Constitution Principle III).
- **FR-003**: Analyzer MUST expose a single identity seam (the Analyzer-side `IVisitorIdentifier` abstraction, registered with **scoped / per-request** DI lifetime) that returns the canonical visitor key for an authenticated request, sourced from the existing Customizer identity layer (per inter-product contract D1, D10; satisfies the Analyzer-side parity coverage of `FR-IDP-04`).
- **FR-004**: The identity seam MUST return `oid` as the canonical key, `upn` as the display form, and MUST fall back to `upn` for both when `oid` is absent — emitting a warning log to flag the misconfiguration (per Constitution Principle I; aligns with `FR-IDP-02`).
- **FR-005**: For unauthenticated requests, the identity seam MUST return "no identity" rather than synthesize an anonymous fallback identifier (per `FR-ID-05` and Constitution Principle I).
- **FR-006**: Analyzer MUST register a backoffice client bundle via the standard Umbraco package manifest, served from `App_Plugins/Analyzer/`, that loads in the backoffice without JavaScript errors. The bundle MUST export a single detectable namespace token (e.g. `window.Analyzer = { version }`) so the bundle's presence is verifiable from a runtime inspector, but MUST NOT yet expose a callable client API (`analyzer.send()` is deferred to slice 004; the per-content-node Analytics content app is deferred to slice 005). Per `NFR-USA-*` and the backoffice integration shape established in slice 001 for later slices.
- **FR-007**: Analyzer MUST use a backoffice route prefix distinct from `/umbraco/engage/...` for any future management API or backoffice surface (per Constitution Tech Stack constraint; avoids collision with the unrelated commercial Engage module). Slice 001 hosts no management endpoints — the concrete prefix string is pinned by the slice that introduces the first endpoint (anticipated: slice 005, per-content-node Analytics content app).
- **FR-008**: Analyzer MUST NOT record any analytics event in slice 001. The identity seam exists; event-collection paths arrive in slice 002 and later. Any data-write attempt from slice 001 code is out-of-scope and would itself be a Constitution Principle IV violation (no event tables exist yet).
- **FR-009**: Analyzer's published artifact MUST carry licensing metadata consistent with the project's chosen licence model (per `NFR-LIC-01`, `NFR-LIC-02`).

### Key Entities *(include if feature involves data)*

- **Visitor (read-only at this slice)**: An identified employee, keyed by EntraID `oid` (immutable canonical) with `upn` as display form. Slice 001 does not own the Visitor record — that lives in Customizer's `customizerVisitorProfile` per inter-product contract D1. Analyzer's identity seam is purely a read-through.

*(No Analyzer-owned event entities exist at this slice. They arrive in slice 002 and later, each FK'd to the Customizer substrate per Constitution Principle IV.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can install Analyzer + Customizer into a fresh Umbraco 17.x host and reach a clean boot in a single try (no rollback, no errors in the application log attributable to Analyzer).
- **SC-002**: An automated build of the package artifact completes from a clean checkout within standard CI build-time expectations for an Umbraco RCL (no manual steps, no out-of-band installers).
- **SC-003**: For any authenticated EntraID request reaching the rendering pipeline, the identity seam returns a canonical visitor key in 100% of test cases that match the documented `oid`/`upn` claim shapes — and returns "no identity" in 100% of unauthenticated test cases.
- **SC-004**: The backoffice bundle loads with HTTP 200 and zero JavaScript console errors when the operator opens the backoffice for the first time after installation.
- **SC-005**: An author opening slice 002 has zero remaining setup work to wire DI, register a composer, or define an identity seam — slice 002's work begins at "subscribe to pageviews and persist events" with no skeleton churn.
- **SC-006**: Slice 001 ships with passing unit tests covering all three `IVisitorIdentifier` resolution branches (oid-present, upn-fallback with warning log, unauthenticated → no-identity), plus integration tests against a test Umbraco host that execute User Story 1, 2, and 3 acceptance scenarios end-to-end. Test stack mirrors Customizer's reference pattern (xUnit v3 + FluentAssertions + SQLite-seam where storage is exercised, in-memory `IServiceCollection` otherwise).

## Assumptions

- The Customizer package is installed on the host before Analyzer is added. Always-deployed-together is the operator commitment per inter-product contract §1.
- The host's Umbraco external-login provider is configured to surface EntraID claims (`oid`, `upn`) to the standard Umbraco authentication pipeline. Slice 001 does not configure the external login provider; it only reads the claims that an already-correctly-configured host surfaces.
- The Customizer identity layer (slice-003) is the single source of truth for visitor identity; Analyzer's identity seam reads through it rather than parsing claims independently. This is per inter-product contract D1 / D10.
- The reference parity document is `Analytics_Intranet_Requirements.md` at the repository root; if/when it moves to `docs/Umbraco_Engage_Analytics_Intranet_Requirements.md`, this spec's references update via a follow-up amendment.
- Slice 001 ships no visible backoffice surfaces; the bundle is intentionally empty of functional UI to keep the wiring channel separable from feature content. Functional panels arrive in later slices.
- The pinned Umbraco version is 17.3.5 (declared in central package management). A host running an older Umbraco minor is out-of-scope; a host running a newer compatible minor is expected to remain installable but is verified per-slice.
- Slice 001 has no Customizer-side prerequisite. The single Customizer addition called out in inter-product contract §6 item 1 (`PageviewCaptured` `INotification` publish) is required only for slice 002's pageview subscription, not for slice 001.
- The Customizer package dependency in `Analyzer.csproj` declares an anticipated minimum: **Customizer ≥ slice 011 (commit `05e989c`)**, i.e. the version that publishes the `PageviewCaptured` notification. Slice 001 itself only needs Customizer's slice-003 identity substrate, but pinning the higher minimum here avoids a dependency bump when slice 002 ships. This trade is justified by the always-deployed-together operator commitment (inter-product contract §1) — there is no scenario where a host runs an old Customizer alongside a newer Analyzer.
