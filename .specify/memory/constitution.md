<!--
Sync Impact Report
==================
Version change: 1.1.0 → 1.1.1 (PATCH; clarification of Principle IV
wording. The MUST-register-a-cascade-step gate is unchanged; the
"re-keys" phrasing is broadened to include delete / soft-delete /
re-projection per the participation-pattern menu Customizer's
`GoalReachedCascadeStep` already established.)

Principle changes:
  - I, II, III, V, VI, VII, VIII, IX, X: unchanged.
  - IV: clarified — "Customizer's operator-facing erasure action
    re-keys it deterministically" replaced with explicit participation-
    pattern menu (delete / soft-delete / re-projection). No new
    requirement; no removed requirement; the gate criterion is
    unchanged.

Trigger: slice-002 /speckit-analyze finding C1 — the cascade-step
hard-delete chosen for `analyzerEventReceipt` (matching Customizer's
`GoalReachedCascadeStep` precedent) was load-bearing for slice-002's
plan-gate-IV PASS verdict, but the constitution's "re-keys" wording
could be misread as mandating per-table re-key semantics.

Templates requiring updates: none.

---

Prior Sync Impact Report (v1.0.0 → v1.1.0)
==========================================
Version change: 1.0.0 → 1.1.0 (MINOR; importing five core principles from
the Customizer constitution with adaptations for Analyzer's domain, plus
two new Section-2 subsections. No existing principle removed or
redefined; Principle IV's role-gated-UPN bullet is moved into the new
Principle VII to avoid duplication and broaden the security posture).

Principle changes:
  - I, II, III, V: unchanged.
  - IV: retitled "Additive-Only Storage, Cascade-Step Anonymisation"
    (was: "...Cascade-Step Anonymisation, Role-Gated UPN"). The
    role-gated-UPN paragraph is removed; the role-gating discipline is
    absorbed and broadened by the new Principle VII.
  - VI (NEW): Software Engineering Excellence — adopted from Customizer
    constitution v2.2.0 Principle I, adapted to "analytics product"
    wording.
  - VII (NEW): Security by Design — adopted from Customizer Principle
    II, adapted to US-jurisdiction language (CCPA/CPRA + state
    electronic-monitoring statutes per Analyzer commit 798aac9) and
    absorbing Analyzer IV's prior role-gated-UPN bullet.
  - VIII (NEW): Performance & Scalability First — adopted from
    Customizer Principle III, adapted to "event capture and reporting"
    wording.
  - IX (NEW): Umbraco-Native & Operator-First — adopted from Customizer
    Principle IV, adapted from "editor" to "operator/analyst"
    terminology (Analyzer's backoffice users configure Traffic Filters
    and read reports; they do not author personalizations).
  - X (NEW): Extensibility by Design — adopted from Customizer
    Principle V, citing Analyzer's three named extension surfaces.
  - Customizer Principles VI (Reporting & Integration Hub) and VII
    (Headless-Compatible Delivery) are NOT adopted as top-level
    Analyzer principles; their applicable parts land as new Section-2
    subsections.

Added sections (Section 2 — Additional Constraints):
  - "Reporting & Open Surface" — OpenAPI discipline + data-ownership
    claim for Analyzer's management/reporting API. Adapted from
    Customizer Principle VI; the "Analyzer emits its own webhooks"
    clause is dropped (Analyzer emits via Customizer's slice-002
    dispatcher per inter-product contract D7).
  - "Headless data-capture compatibility" — `analyzer.send()` and
    pageview capture must work identically on Razor-rendered and
    headless-rendered intranet frontends, sourcing identity from the
    same EntraID flow. Adapted (narrowed) from Customizer Principle VII.

Removed sections: none.

Constitution Check Gates: five new gates added (one per new principle)
under "Development Workflow → Constitution Check Gates".

Templates requiring updates:
  ✅ .specify/templates/plan-template.md — Constitution Check
     placeholder is principle-agnostic; new gates are auto-picked up by
     reference.
  ✅ .specify/templates/spec-template.md — principle-agnostic.
  ✅ .specify/templates/tasks-template.md — principle-agnostic.
  ✅ CLAUDE.md — no inline principle list; references this file. No
     edit needed.
  ✅ README.md — references this file as the authoritative constitution
     without inline content. No edit needed.
  ✅ docs/INTER-PRODUCT-CONTRACT.md — no inline principle list;
     Analyzer-side Principle III continues to cite it directly. No
     edit needed.

Deferred items (carried forward from v1.0.0):
  - The reference requirements doc is cited as
    Analytics_Intranet_Requirements.md at the repository root. When it
    moves to docs/Umbraco_Engage_Analytics_Intranet_Requirements.md,
    the link in Principle II must be updated (PATCH-level amendment).

Prior version (1.0.0, 2026-05-18): initial ratification — Core
Principles I–V (EntraID-Only Identity, Spec-Grounded Scope, Customizer
Substrate, Additive-Only Storage with Cascade-Step Anonymisation +
Role-Gated UPN, Slice-Driven Delivery via Speckit).
-->

# Analyzer Constitution

## Core Principles

### I. EntraID-Only Identity (NON-NEGOTIABLE)

Every visitor is authenticated via Azure EntraID before any Analyzer
event records. The visitor identifier is derived from the EntraID `oid`
claim (immutable canonical key — survives mailbox renames); the `upn`
claim is the human-readable display form shown in backoffice surfaces
and audit logs, and serves as the configuration-error fallback when a
host's external-login provider omits `oid`. **No anonymous,
cookie-based, or fingerprint-based identity path exists at any layer.**
"Unidentified Visitor" is not a state Analyzer models. Consent
surfaces, anonymous tracker code paths, and identity-less collection
are out of scope.

Rationale: Analyzer is intranet-scoped — 100% of pageviews are
identified by deployment context. Anything modelling an anonymous case
introduces dead code, a misleading security boundary, and a parity
target that does not apply.

### II. Spec-Grounded Scope with Declared Drops

Every functional change cites an `FR-*` or `NFR-*` requirement ID from
[`Analytics_Intranet_Requirements.md`](../../Analytics_Intranet_Requirements.md)
(the Umbraco Engage Analytics v17 LTS reference, intranet-scoped, draft
dated 2026-05-12). The following requirement prefixes and items are
**permanently out of scope** and MUST NOT be cited as parity targets in
any spec, plan, or task:

- `FR-DEP-*` — Engage-module dependencies (Analyzer has none).
- `FR-DIM-04` — Campaigns / UTM tracking (no campaign attribution on
  an authenticated intranet; downstream products may capture UTM via
  `IEventDimensionExtractor` into their own side tables per
  [`docs/INTER-PRODUCT-CONTRACT.md`](../../docs/INTER-PRODUCT-CONTRACT.md)
  §3 D6).
- `FR-DIM-03` — Geographic location tracking (off by default in
  Engage; disabled entirely here).
- §3.3 bot detection as an active capability (irrelevant on an
  authenticated intranet).
- §6.2 public-website features (cookie-consent banners, anonymous
  tracker code paths).

Operational compliance items — state-law electronic-monitoring notices
and front-end CCPA right-to-know / right-to-delete responses — are the
deploying organisation's responsibility. Analyzer provides the
backoffice export and delete operations needed to **support** those
requests; it does not own the user-facing surface.

Rationale: scope drift across slices is the failure mode this principle
prevents. The reference doc is the parity benchmark for what *is* in
scope; the drop list keeps it from also becoming a parity benchmark for
what *isn't*.

### III. Customizer Substrate, No Retrofit

Analyzer depends on Customizer per
[`docs/INTER-PRODUCT-CONTRACT.md`](../../docs/INTER-PRODUCT-CONTRACT.md)
§1. Customizer's pinned public surface — in particular
`Customizer.Analytics.IAnalyticsStateProvider`,
`IPersonalizationProfile`, `IVisitorReachedGoalsLookup`,
`IAnonymizationCascadeStep`, and the slice-002 webhook dispatcher — is
**unchanged** by anything Analyzer ships. Customizer's pinned
`PublicSurfacePinningTests` MUST NOT regress.

Analyzer ships parallel-named contracts wherever conceptual overlap
would otherwise cause a name collision (e.g.
`Analyzer.Analytics.IAnalyticsEventStateProvider`, deliberately
distinct from Customizer's pinned `IAnalyticsStateProvider`). The
**only** Customizer-side prerequisite for Analyzer is the additive
`PageviewCaptured` `INotification` publish from
`PageviewCaptureMiddleware` (Customizer slice 011; contract §6 item 1).
No other Customizer change is in scope under this constitution.

Rationale: Customizer is shipped, tested, pinned. Analyzer is paper.
Touching Customizer costs a major-version bump and a retrofit slice;
touching Analyzer costs nothing. The inverted layering (Analyzer →
Customizer) is the pragmatic choice that preserves Customizer's
public contract while letting Analyzer evolve freely.

### IV. Additive-Only Storage, Cascade-Step Anonymisation

Analyzer never duplicates the canonical pageview row. Every
Analyzer-owned event and side table (`analyzerSession`,
`analyzerCustomEvent`, `analyzerVideoEvent`, `analyzerFormsEvent`,
`analyzerScrollSample`, `analyzerSearchEvent`, and any future addition)
is foreign-keyed to `customizerPageview.Key` and/or
`customizerVisitorProfile.Key`. Every such table MUST register an
`IAnonymizationCascadeStep`. Customizer's operator-facing erasure
action re-keys the visitor's identity deterministically (overwriting
`IdentityRef` from `oid:…` to `anonymized:…` on the same
`customizerVisitorProfile.Key`); each Analyzer table participates by
doing what is table-appropriate — hard-delete (the established
pattern; matches Customizer's `GoalReachedCascadeStep` and slice-002's
`AnalyzerEventReceiptCascadeStep`), soft-delete, or re-projection —
chosen per slice and pinned in the slice's `plan.md` Constitution
Check section. The absence of a cascade-step registration for a new
Analyzer table is a Constitution Check failure regardless of which
participation pattern the table chooses.

Rationale: data integrity and erasure compliance are bounded by the
"one row per pageview" invariant Customizer owns and by a complete
cascade-step registry. Both must be enforced at plan time, not after
ship. (UPN role-gating, previously included in this principle, is now
covered by the broader RBAC + audit-log discipline of Principle VII.)

### V. Slice-Driven Delivery via Speckit

Every change reaches `main` through the speckit slice flow:
`/speckit-specify → /speckit-plan → /speckit-tasks →
/speckit-implement`. Each slice has its own `specs/NNN-name/`
directory, cites the `FR-*`/`NFR-*` IDs it satisfies, and is
independently testable. The Constitution Check section of `plan.md` is
a hard gate before tasks are generated; principle violations are
either fixed at plan time or justified in the plan's Complexity
Tracking table with the rejected simpler alternative.

Cross-slice or cross-product behavioural changes require a paired
commit on both repos, referenced cross-wise, per
[`docs/INTER-PRODUCT-CONTRACT.md`](../../docs/INTER-PRODUCT-CONTRACT.md)
§7.

Rationale: the workflow is the discipline. Without slice-level gates,
the load-bearing principles erode under refactor pressure and ad-hoc
commits.

### VI. Software Engineering Excellence
*(`NFR-MNT-01..03`)*

- Code MUST follow SOLID principles and clean architecture: explicit
  dependencies, narrow interfaces, no leakage of infrastructure
  concerns into domain code.
- Features SHOULD be organised as vertical slices (or CQRS where the
  read/write split is material), keeping each capability self-contained
  from controller down to persistence. Analyzer mirrors Customizer's
  `Features/<Domain>/{Application,Domain,Infrastructure}/` layout for
  symmetry between the two co-deployed products.
- Every public domain rule, command handler and event-capture or
  reporting path MUST be covered by automated tests (unit +
  integration). Public extension contracts MUST be exercised by
  integration tests before being announced as stable in release notes.
- Readability, separation of concerns and long-term maintainability
  take precedence over short-term shortcuts. Any deviation MUST be
  justified in the plan's Complexity Tracking table with a rejected
  simpler alternative.

Rationale: Analyzer is an extension that other developers and host
applications will depend on. Sloppy internal design propagates into
every consumer; engineering discipline is the cheapest form of
compatibility insurance.

### VII. Security by Design
*(`NFR-SEC-01..10`)*

- Defense-in-depth is mandatory: every layer (controller, application,
  domain, persistence, reporting API, backoffice surface) MUST validate
  its own inputs and enforce its own authorisation, not rely on an
  upstream layer.
- All Analyzer surfaces (reports, dashboards, per-content-node
  Analytics content app, Traffic Filter management, export and erasure
  operations) MUST be gated on Umbraco backoffice permissions (RBAC).
  Anonymous access to management or reporting surfaces is prohibited.
- **Individual-level UPN data is role-gated in every backoffice
  surface** (`NFR-SEC-*`). The per-content-node Analytics content app,
  per-visitor drill-downs, and any UPN-bearing export require an
  authorised user group; the deploying organisation chooses which
  group.
- Sensitive data — API keys, integration tokens, webhook secrets
  emitted through Customizer's outbox, Traffic Filter credentials —
  MUST be encrypted at rest. Plain-text storage of any credential is a
  constitutional violation.
- Every state-changing action on Traffic Filters, goal-completion
  report configuration, anonymisation triggers and integration
  configuration MUST emit an audit log entry capturing actor (the
  authorised Umbraco user, by UPN), action, target and timestamp.
- All inputs from HTTP, the backoffice and webhook callbacks MUST be
  sanitised and validated at the boundary; the domain layer MUST NOT
  trust external input.
- Analytics data (visitor profiles read through Customizer, Analyzer-
  owned event tables) MUST be processed using server-side, first-party
  collection only, stored inside the host Umbraco database, with
  configurable retention and anonymisation periods sufficient for the
  deploying organisation's compliance posture. The reference posture
  for Analyzer is **US-jurisdiction**: support for **CCPA/CPRA
  right-to-know and right-to-delete operations** (executed by the
  deploying org via Analyzer's backoffice export and delete surfaces)
  and **state electronic-monitoring notice statutes** (NY §52-c, CT
  §31-48d, DE Title 19 §705 — publication of the notice is the
  deploying organisation's responsibility, not a feature of Analyzer).

Rationale: this is a paid Umbraco capability whose value depends on
customer data ownership and regulatory defensibility. Audit logging,
encryption and RBAC are also the only credible answers when a
customer's compliance team asks "show me who changed what and who saw
which UPN". UPN role-gating sits inside Security by Design because
it is the in-product enforcement of the privacy-by-design posture, not
a standalone storage concern.

### VIII. Performance & Scalability First
*(`NFR-PER-01..03`)*

- Pageview capture (subscribed from Customizer's `PageviewCaptured`
  notification) and event recording MUST run on the request hot path
  with latency negligible compared with baseline CMS rendering. Slice
  002's pageview subscription handler MUST not regress Customizer's
  slice-003 throughput envelope (1000 pv/s sustained, 5000 pv/s peak —
  see inter-product contract D2).
- Long-running, retryable or bulk work (report aggregation, video
  position derivation, scroll-heatmap rollups, Traffic Filter
  re-evaluations) MUST run on a reliable background queue with retry,
  throttling and dead-letter handling. Where the work crosses the
  product boundary (e.g. webhook delivery), it MUST emit through
  Customizer's slice-002 outbox dispatcher per inter-product contract
  D7, not a separate Analyzer transport.
- Hot paths MUST be designed for parallelism: no global locks in
  request handling, no synchronous network I/O during page resolution,
  no N+1 database access patterns in event capture or reporting
  queries.
- Database access MUST use bounded queries (indexed predicates,
  pagination, projection of only the columns required). Reporting
  views MUST remain responsive at the maximum supported event volume.
- The architecture MUST be horizontally scalable: no per-process
  in-memory state for visitor identity, session aggregation, or
  Traffic Filter evaluation that cannot be reconstructed from the
  database or a shared cache.

Rationale: event capture sits on every authenticated pageview. A
performance regression here is felt by every employee on the intranet
and cannot be opted out of. The queue / throttle / retry discipline
also protects the host site when downstream integrations (BI
exporters, dashboards) misbehave.

### IX. Umbraco-Native & Operator-First
*(`NFR-USA-01..03`, `NFR-CMP-01..04`)*

- Analyzer MUST feel like core Umbraco functionality. Backoffice UI
  MUST be built on `@umbraco-cms/backoffice` primitives and follow the
  Umbraco design system. Bespoke UI patterns require explicit
  justification in the plan's Complexity Tracking table.
- All operator and analyst workflows — Traffic Filter configuration,
  report filtering, goal-completion review, anonymisation triggers,
  per-content-node analytics inspection — MUST be operable from the
  Umbraco backoffice without code changes, once a developer has
  installed Analyzer.
- Analyzer MUST integrate with Umbraco core systems: Content (per-node
  reporting, content-app element), Document Types (segmentation
  prerequisite shared with Customizer + Adjuster), and the Umbraco
  event/notification system (subscribed to Customizer's
  `PageviewCaptured` per inter-product contract D2). External
  integrations are optional — internal Umbraco integration is not.
- User-facing errors MUST be actionable and, where applicable, link
  directly to the affected content nodes or visitor profiles
  (subject to UPN role-gating per Principle VII).
- Configuration (Traffic Filters, goal-completion report definitions,
  per-content-node visibility settings) MUST be transferable between
  environments via Umbraco Deploy where Umbraco's infrastructure
  supports it.

Rationale: this is an analytics tool delivered through a CMS. Forcing
operators or analysts to drop into developer surfaces defeats the
product category, and visual or behavioural inconsistency with the
host backoffice erodes trust.

### X. Extensibility by Design
*(`FR-ENR-*`, `NFR-MNT-01..02`)*

- Every Analyzer-defined extension surface — `IVisitorIdentifier` /
  `BaseVisitorIdentifier`, `IEventDimensionExtractor` /
  `BaseEventDimensionExtractor`, and `IAnalyticsEventStateProvider` —
  MUST expose a public interface that third-party developers can
  implement and register via dependency injection.
- Custom implementations MUST be functionally indistinguishable from
  Analyzer's built-in implementations at runtime: identical lifetime
  semantics, identical evaluation order, identical observability hooks.
- Breaking changes to public extension contracts are PROHIBITED outside
  MAJOR releases. MINOR releases MAY add behaviour-compatible members;
  PATCH releases are limited to clarifications and fixes. Public
  extension contracts MUST be covered by `PublicSurfacePinningTests`-
  style pinning before they are announced as stable (Analyzer's
  pinning landscape is introduced at slice 002 per the
  `001-package-skeleton` slice's Clarification Q2).
- The Composer / DI registration pattern MUST follow Umbraco
  conventions (transient / scoped / singleton lifetime choices made
  deliberately and documented per extension point — `IVisitorIdentifier`
  is registered as scoped per slice 001 Clarification Q3).

Rationale: third parties build on these contracts (e.g. an HR-tenant
custom `IEventDimensionExtractor` that enriches events with internal-
job-band data), and Analyzer has no other channel to manage their
migrations. A pluggable architecture also lets Analyzer evolve
domain-by-domain (sessions, custom events, video, forms, scroll,
search) without forcing a monolithic version bump on consumers.

## Additional Constraints

### Tech Stack (pinned)

- **Server**: .NET 10 Razor Class Library targeting Umbraco CMS 17.x;
  central package management via `src/Analyzer/Directory.Packages.props`
  with **`Umbraco.Cms.Web.Common` / `Umbraco.Cms.Web.Website` pinned at
  17.4.1** to match Customizer's floor; the meta `Umbraco.Cms` packages
  float on `17.*` for the sample host.
- **Backoffice client**: TypeScript + Vite + `@umbraco-cms/backoffice`
  17.3.5; source under `src/Analyzer/Client/`; bundle emitted to
  `wwwroot/App_Plugins/Analyzer/analyzer.js`.
- **Package manifest**: `src/Analyzer/Client/public/umbraco-package.json`
  declares the bundled JS entrypoint.
- **Backoffice route prefix**: management APIs live under the Analyzer
  namespace, NOT under `/umbraco/engage/...` (avoid collision with the
  unrelated commercial Engage module).
- **Identity claim ordering**: `oid`-first, `upn`-fallback per
  Principle I; matches Customizer's slice-003 implementation.

### Architectural Concepts

Three primary domain concepts: **Visitors**, **Sessions**, **Events**.
The "Unidentified Visitor" state from the Engage reference is
explicitly absent (Principle I).

Analyzer-defined extension surfaces (independent names; not copies of
Engage's API):

- `IVisitorIdentifier` / `BaseVisitorIdentifier` — derives the visitor
  identifier from authenticated EntraID claims.
- `IEventDimensionExtractor` / `BaseEventDimensionExtractor` — enriches
  each event with custom dimensions (e.g. `department`,
  `officeLocation`) at request time (`FR-ENR-*`).
- `IAnalyticsEventStateProvider` — Analyzer-side request-state contract;
  intentionally distinct name from Customizer's pinned
  `IAnalyticsStateProvider` (Principle III).
- Per-content-node Analytics content app, role-gated (Principle VII).
- Client-side event push API (`analyzer.send("event", category,
  action, label)`) for in-page custom events.

### Reporting & Open Surface

Analyzer MUST NOT be a black box. All data exposed in the backoffice —
pageview counts, session aggregates, custom-event tallies, video
engagement, scroll heatmaps, search query reports, goal-completion
reports, Traffic Filter audit trails — MUST also be available through
a public, versioned, RESTful management/reporting API rooted under
Analyzer's backoffice namespace (per the Tech Stack route-prefix
constraint).

- The API MUST follow OpenAPI conventions with a machine-readable
  schema published alongside each release. Endpoint authentication
  MUST use first-class Umbraco backoffice authentication primitives,
  same as the operator backoffice (Principle VII).
- Data ownership stays inside the customer's Umbraco database
  (Principle IV). Analyzer MUST make that data trivially accessible to
  external BI tools, CRMs and custom dashboards through the API
  (Analyzer does not ship its own webhook transport — outbound events
  emit through Customizer's slice-002 dispatcher per inter-product
  contract D7).
- UPN role-gating (Principle VII) MUST apply identically through the
  API as through the backoffice UI; individual-level UPN data MUST NOT
  be readable through the reporting API for non-authorised user
  groups.

### Headless data-capture compatibility

When a deploying organisation renders its intranet headlessly (e.g.
Next.js / Nuxt frontends consuming Umbraco's Delivery API or a custom
headless endpoint), Analyzer's data capture MUST work identically to
the Razor-rendered path:

- Pageview capture (subscribed from Customizer's `PageviewCaptured`
  notification) does not depend on the rendering layer and is
  unaffected.
- The client-side event push API (`analyzer.send(…)`) MUST function in
  a headless browser context with the same payload shape and the same
  authentication flow as in a Razor context.
- Visitor identification for headless event submission MUST use the
  same EntraID-projected identity that Razor flows use (per Principle
  I). No parallel visitor-identity scheme (cookie, fingerprint,
  anonymous token) is permitted, irrespective of rendering layer.

This is a constraint, not a separate principle, because Analyzer does
not *deliver* content; it captures engagement on whatever the host
delivers.

## Development Workflow

### Speckit Slice Lifecycle

1. `/speckit-specify` — capture the slice's scope and `FR-*`/`NFR-*`
   coverage. Optional: `/speckit-clarify` to de-risk before planning.
2. `/speckit-plan` — write the implementation plan. **Constitution
   Check is a hard gate here.** Optional: `/speckit-checklist` after,
   to validate completeness.
3. `/speckit-tasks` — generate the task breakdown. Optional:
   `/speckit-analyze` for cross-artifact consistency.
4. `/speckit-implement` — execute tasks. When tests are part of the
   slice, they must fail before implementation per the slice's own
   test discipline.
5. Auto-commit hooks configured in `.specify/extensions.yml` propose a
   commit at each phase boundary; honour or skip per slice judgment.

### Constitution Check Gates (applied per slice at plan time)

A plan FAILS the Constitution Check if any of the following hold:

- Any new collection path records data without an authenticated
  EntraID identity (Principle I).
- Any spec, plan, or task cites an out-of-scope requirement
  (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection as an
  active capability, or §6.2 public-website features) as a parity
  target (Principle II).
- Any change modifies Customizer's pinned public surface, OR
  introduces a name collision with Customizer's existing public types
  (Principle III).
- Any new Analyzer event or side table lacks a documented
  `IAnonymizationCascadeStep` registration, OR a foreign key to the
  appropriate Customizer substrate row (Principle IV).
- Any change reaches `main` outside the speckit slice flow
  (Principle V).
- Any new code lacks vertical-slice organisation or unit + integration
  test coverage of public domain rules and extension contracts
  (Principle VI).
- Any new management or reporting surface lacks RBAC gating, lacks an
  audit-log entry for state-changing actions, OR exposes individual-
  level UPN data to a non-authorised user group (Principle VII;
  `NFR-SEC-*`).
- Any hot-path code introduces global locks, synchronous network I/O,
  N+1 database access, OR fails to use Customizer's slice-002 outbox
  dispatcher for cross-boundary asynchronous work (Principle VIII).
- Any new backoffice UI uses non-`@umbraco-cms/backoffice` primitives,
  OR any new operator/analyst workflow requires code changes to
  operate (Principle IX).
- Any new public-extension contract lacks DI registration with
  documented lifetime, lacks behaviour-compatible custom-impl story,
  OR breaks an existing contract outside a MAJOR release
  (Principle X).

Failures must be either resolved before tasks are generated, or
justified in the plan's Complexity Tracking table with the rejected
simpler alternative documented.

## Governance

This constitution supersedes ad-hoc practices and personal preferences.
PRs MUST verify compliance with the principles above before merge; the
plan's Constitution Check is the primary mechanism.

**Amendment procedure.** Constitution changes require:

1. A PR modifying `.specify/memory/constitution.md` with a Sync Impact
   Report at the head of the file.
2. A coordinated sweep of dependent artifacts: `CLAUDE.md`, `README.md`,
   `docs/INTER-PRODUCT-CONTRACT.md` (if the change touches the
   Customizer boundary), and the speckit templates under
   `.specify/templates/`.
3. If the amendment changes the inter-product contract, a paired PR on
   the `customizer` repo referenced cross-wise per
   [`docs/INTER-PRODUCT-CONTRACT.md`](../../docs/INTER-PRODUCT-CONTRACT.md)
   §7.

**Versioning policy** (semver applied to this document):

- **MAJOR**: backward-incompatible governance changes; removing or
  redefining a principle in a way existing slices no longer satisfy.
- **MINOR**: adding a principle, or materially expanding scope-drop or
  gate coverage.
- **PATCH**: clarifications, wording, typo fixes, non-semantic
  refinements (including updating cross-document links after a file
  move).

For day-to-day runtime guidance see [`CLAUDE.md`](../../CLAUDE.md).

**Version**: 1.1.1 | **Ratified**: 2026-05-18 | **Last Amended**: 2026-05-18
