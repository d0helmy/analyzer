# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Analyzer

An Umbraco package being built against the **Umbraco Engage Analytics**
module specification.

## Product framing (load-bearing)

Analyzer is an **intranet analytics** product. The single most important
framing constraint, restated for every future slice:

- **Every visitor is authenticated via Azure EntraID** (corporate SSO),
  projected into Umbraco via the standard external login provider.
- **There are no anonymous / "Unidentified Visitor" visitors and no
  cookie-consent / opt-out surface.** 100% of pageviews are identified
  from the first request by definition of the deployment context
  (corporate intranet, authenticated employees).
- Visitor identity, session attribution, audit "actor" attribution,
  and any event-dimension extractor payloads MUST all key off the
  EntraID identity claim (`oid`, fallback `upn`). `oid` is the
  immutable canonical storage key (survives mailbox renames); UPN
  is the human-readable display form shown in backoffice surfaces
  and audit logs. The fallback to `upn` covers only the
  configuration-error case where a host's external-login provider
  omits `oid`. No tracker-cookie or fingerprint-based identity is
  in scope at any layer.
- The following Engage parity items are **intentionally dropped from
  Analyzer's scope** (the reference doc remains the parity benchmark
  for what *is* in scope, but these sections are explicitly excluded):
  - **§2.4 Dependencies on other Engage modules** (all `FR-DEP-*`
    requirements) — Analyzer has no runtime, package, or source
    dependency on `Umbraco.Engage`; standalone-vs-engage-tier framing
    is not applicable.
  - **§3.3 Campaigns / UTM tracking** (`FR-DIM-04`) — public-internet
    framing; campaign attribution does not exist on an authenticated
    intranet. Analyzer ships **no UTM column** on its pageview row and
    **no campaign report**. Downstream products (e.g. Customizer's
    `campaigns` segment rule) may register an `IEventDimensionExtractor`
    that captures UTM into their own side table — see
    [`docs/INTER-PRODUCT-CONTRACT.md`](docs/INTER-PRODUCT-CONTRACT.md)
    §3 D6.
  - **§3.3 Geographic location tracking** (`FR-DIM-03`, off by default
    in Engage) — disabled entirely; not surfaced.
  - **§3.3 Bot detection as an active capability** — irrelevant on an
    authenticated intranet (bots cannot reach the front-end).
  - **§6.2 Public-website features such as cookie-consent banners** —
    no consent surface, no anonymous-tracker code path.
- Operational compliance items — publishing state-law electronic-
  monitoring notices and responding to CCPA right-to-know /
  right-to-delete requests on the front-end — are the **deploying
  organisation's responsibility**, not features of Analyzer. The
  product provides the backoffice export and delete operations needed
  to *support* those requests when they arrive.

This framing supersedes any ambiguous "intranet authenticated audience"
phrasing in older notes.

## Orientation for AI agents

- **Reference spec**: [`Analytics_Intranet_Requirements.md`](Analytics_Intranet_Requirements.md)
  (will move to `docs/Umbraco_Engage_Analytics_Intranet_Requirements.md`
  per the README) — Umbraco Engage v17 (LTS) reference, draft
  2026-05-12. This is the authoritative source for what we're
  building, *minus* the scope items explicitly dropped in the Product
  framing section above. Requirement IDs use the prefixes `FR-COL-*`,
  `FR-EVT-*`, `FR-ID-*`, `FR-IDP-*`, `FR-DIM-*`, `FR-FRM-*`,
  `FR-VID-*`, `FR-HMP-*`, `FR-ENR-*`, `FR-GOL-*`, `FR-FLT-*`,
  `FR-RPT-*`, `FR-SRC-*`, `FR-DEP-*`, and `NFR-USA-*`, `NFR-PER-*`,
  `NFR-SEC-*`, `NFR-CMP-*`, `NFR-MNT-*`, `NFR-LIC-*` — reference them
  in commits, PRs, and specs. The `FR-DEP-*` (Engage module
  dependencies) and `FR-DIM-04` (campaigns) prefixes are out of scope
  and MUST NOT be cited as parity targets in new specs; `FR-DIM-03`
  (geo) is off by default and only reachable behind explicit
  privacy/compliance owner approval.

- **Inter-product contract**: [`docs/INTER-PRODUCT-CONTRACT.md`](docs/INTER-PRODUCT-CONTRACT.md)
  binds Analyzer and Customizer (the sibling personalization product
  at `../customizer`). **Analyzer depends on Customizer** (inverted
  from natural layering — see contract §1 rationale). Customizer
  owns: visitor identity + profile (`customizerVisitorProfile`),
  pageview capture (`customizerPageview` + middleware),
  `IAnalyticsStateProvider`, `IPersonalizationProfile`, goals
  definitions + `IVisitorReachedGoalsLookup`, UTM capture,
  anonymisation orchestrator + `IAnonymizationCascadeStep`, webhook
  outbox + dispatcher. Analyzer adds purely additive concerns:
  sessions, custom events, video, forms, scroll, search, reports,
  Traffic Filters, per-content-node Analytics content app,
  `IEventDimensionExtractor`, `IAnalyticsEventStateProvider`
  (separate name from Customizer's pinned `IAnalyticsStateProvider`),
  goal-completion reports. The only Customizer-side prerequisite is
  one small additive change (a `PageviewCaptured` `INotification`
  publish) — contract §6 item 1. Read the contract before opening
  Analyzer's first slice.

- **Repo state**: This repository is currently **spec-only**. Neither
  the `src/Analyzer/` package skeleton, the `specs/` directory,
  nor `.specify/memory/constitution.md` exist yet — they
  are anticipated by the README. Future agents should scaffold these
  on first implementation slice rather than assume they are present.

## Tech stack (planned per README)

- **Server**: .NET 10 Razor Class Library targeting Umbraco CMS 17.x,
  central package management via `src/Analyzer/Directory.Packages.props`
  (Umbraco 17.3.5 pinned).
- **Backoffice client**: TypeScript + Vite + `@umbraco-cms/backoffice`
  17.3.5, source in `src/Analyzer/Client/`. Bundle output at
  `wwwroot/App_Plugins/Analyzer/analyzer.js`.
- **Package manifest**: `src/Analyzer/Client/public/umbraco-package.json`
  declares the bundled JS entrypoint.

## Building

```bash
# Server (from repo root)
dotnet restore
dotnet build Analyzer.slnx

# Backoffice client (from src/Analyzer/Client/)
npm install
npm run build      # one-shot → wwwroot/App_Plugins/Analyzer/analyzer.js
npm run watch      # rebuild on change; pair with a host Umbraco site
                   # referencing the RCL as a project reference
```

## Repo layout (planned)

```
Analyzer.slnx                 # solution
docs/                         # specs, design notes (incl. reference doc)
src/Analyzer/                 # the RCL package
  Analyzer.csproj
  Directory.Packages.props
  Composers/                  # IComposer registrations
  Controllers/                # backoffice / management API
  Constants.cs
  Client/                     # TypeScript bundle (Vite)
specs/                        # per-slice specs + overall-scope.md
.specify/                     # speckit workflow templates + scripts
.claude/skills/speckit-*      # speckit slash-command skills
```

## Architecture pointers from the spec

The package is organised around three primary concepts (per the spec,
with the "Unidentified Visitor" state explicitly dropped — see Product
framing):

1. **Visitors** — identified employees keyed by the EntraID UPN,
   unified across sessions and devices (`FR-ID-*`, `FR-IDP-*`).
2. **Sessions** — bounded sequences of interactions by a single
   visitor on a single device, within a configurable inactivity
   timeout.
3. **Events** — pageviews, custom events, video, scroll, form
   interactions and goal triggers, all attached to a visitor and a
   content node (`FR-COL-*`, `FR-EVT-*`, `FR-VID-*`, `FR-FRM-*`,
   `FR-HMP-*`, `FR-GOL-*`, `FR-SRC-*`).

Analyzer-defined extension surfaces (independent names and namespaces;
not copies of Engage's API):

- `IVisitorIdentifier` / `BaseVisitorIdentifier` — derives the visitor
  identifier from authenticated EntraID claims (UPN-first; replaces
  Engage's cookie-first identification).
- `IEventDimensionExtractor` / `BaseEventDimensionExtractor` — enriches
  each event with custom dimensions (e.g. EntraID `department`,
  `officeLocation`) at request time (`FR-ENR-*`).
- An analytics state provider for reading current-pageview events,
  visitor identity and enrichment attributes server-side.
- A management API under the Analyzer backoffice route (**not** under
  `/umbraco/engage/...`) for dashboards, custom events, goals and
  Traffic Filters (`FR-RPT-*`, `FR-FLT-*`).
- A client-side event push API (e.g. `analyzer.send("event",
  category, action, label)`) for in-page custom events.
- Per-content-node Analytics content app — every content node exposes
  its own usage view (pageviews, unique visitors, average time on
  page, scroll heatmap) in the backoffice, role-gated so that
  individual-level UPN data is visible only to an authorised user
  group (`NFR-SEC-*`).

## Workflow

Use the speckit slash commands (after `/reload-plugins` or a fresh
session) to move from requirements → plan → tasks → implementation:

1. `/speckit-constitution` — capture project principles
2. `/speckit-specify` — create a focused spec for a slice
3. `/speckit-plan` — implementation plan
4. `/speckit-tasks` — actionable task breakdown
5. `/speckit-implement` — execute

Optional: `/speckit-clarify`, `/speckit-analyze`, `/speckit-checklist`.

<!-- SPECKIT START -->
Slice 007 (search tracking) **implementation complete** on branch
`007-search-tracking`; ready for PR + merge. Adds the
`analyzerSearchEvent` table (raw + normalised query + result count +
visitor-bound `pageviewKey`, PII-tagged per `FR-SRC-04`), a new
`IAnalyzerSearchQueryNormaliser` public extension point (first new
Analyzer-defined surface since slice 001's `IVisitorIdentifier`) with
default trim + NFKC + InvariantLower + whitespace-collapse
implementation, the `POST /umbraco/management/api/v1/analyzer/search-event`
management endpoint (strengthened with a visitor-bound `pageviewKey`
check vs slice 006), a client-side `window.analyzer.sendSearch(query,
resultCount)` helper in
`src/Analyzer/Client/src/features/search-tracking/` (per-call opt-out
evaluation; returns `Promise<{ eventKey } | { skipped: true }>`), and
a seventh `IAnonymizationCascadeStep` registration (hard-delete —
diverges from contract D8's re-key disposition; D8 amended this slice
per spec Clarifications §2). Audit-log emits ActorUpn/Oid + EventKey
+ PageviewKey + ResultCount but **never** the query (PII redaction,
SC-006). **No new package dependency**; no Customizer-side change.
**169/169 unit tests green; 58/58 Vitest green**; bundle 12.01 kB
(3.38 kB gzipped); public-surface pinning baseline regenerated
additively (3 new members: `IAnalyzerSearchQueryNormaliser`,
`AnalyticsSearchEvent`, `IAnalyticsEventStateProvider.CurrentRequestSearchEvents`).
T053 (quickstart walkthrough) is user-driven; T054 (post-merge
housekeeping) lands after merge.

Slice 006 (scroll tracking) shipped at `7a20451`; slice 005 (forms
tracking) at `5e868ef`; slice 004 (custom events) at `bbc5b27`.
<!-- SPECKIT END -->
