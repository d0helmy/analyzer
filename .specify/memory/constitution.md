<!--
Sync Impact Report
==================
Version change: TEMPLATE → 1.0.0 (initial ratification)
Modified principles: N/A (initial set)
Added sections:
  - Core Principles (I–V)
  - Additional Constraints (Tech Stack, Architectural Concepts)
  - Development Workflow (Speckit Slice Lifecycle, Constitution Check Gates)
  - Governance (amendment procedure, versioning policy)
Removed sections: none
Templates requiring updates:
  ✅ .specify/templates/plan-template.md — Constitution Check gate
     phrasing unchanged; per-slice gates now derived from Principles I–V.
  ✅ .specify/templates/spec-template.md — no constitution-level changes
     needed at spec time (constraints apply at plan time).
  ✅ .specify/templates/tasks-template.md — task categorization unchanged.
  ✅ CLAUDE.md — already references this file under "Repo layout" and
     "Workflow"; load-bearing framing already encoded.
  ✅ README.md — already references this file as the authoritative
     constitution.
  ✅ docs/INTER-PRODUCT-CONTRACT.md — already binding alongside this
     constitution; Principle III cites it directly.
Deferred items:
  - The reference requirements doc is cited as Analytics_Intranet_Requirements.md
    at the repository root, which is its current location. CLAUDE.md and
    README.md both flag a planned move to
    docs/Umbraco_Engage_Analytics_Intranet_Requirements.md. When that move
    lands, the link in Principle II must be updated (PATCH-level amendment).
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

### IV. Additive-Only Storage, Cascade-Step Anonymisation, Role-Gated UPN

Analyzer never duplicates the canonical pageview row. Every
Analyzer-owned event and side table (`analyzerSession`,
`analyzerCustomEvent`, `analyzerVideoEvent`, `analyzerFormsEvent`,
`analyzerScrollSample`, `analyzerSearchEvent`, and any future addition)
is foreign-keyed to `customizerPageview.Key` and/or
`customizerVisitorProfile.Key`. Every such table MUST register an
`IAnonymizationCascadeStep` so Customizer's operator-facing erasure
action re-keys it deterministically; the absence of a cascade-step
registration for a new Analyzer table is a Constitution Check failure.

Individual-level UPN data is role-gated in every backoffice surface
(`NFR-SEC-*`). The per-content-node Analytics content app, per-visitor
drill-downs, and any UPN-bearing export require an authorised user
group; the deploying organisation chooses which group.

Rationale: data integrity and erasure compliance are bounded by the
"one row per pageview" invariant Customizer owns and by a complete
cascade-step registry. Both must be enforced at plan time, not after
ship. UPN data is PII; visibility is a deploy-time policy choice with a
code-time enforcement seam.

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
Principles I–IV erode under refactor pressure and ad-hoc commits.

## Additional Constraints

### Tech Stack (pinned)

- **Server**: .NET 10 Razor Class Library targeting Umbraco CMS 17.x;
  central package management via `src/Analyzer/Directory.Packages.props`
  with **Umbraco 17.3.5 pinned**.
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
- Per-content-node Analytics content app, role-gated (Principle IV).
- Client-side event push API (`analyzer.send("event", category,
  action, label)`) for in-page custom events.

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

- Any new collection path records data without an authenticated EntraID
  identity (Principle I).
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
- Any new individual-level UPN surface lacks role-gating
  (Principle IV; `NFR-SEC-*`).
- Any change reaches `main` outside the speckit slice flow
  (Principle V).

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

**Version**: 1.0.0 | **Ratified**: 2026-05-18 | **Last Amended**: 2026-05-18
