# Analyzer

An Umbraco package targeting Umbraco CMS 17 (.NET 10) that delivers native,
first-party **analytics** for **US-based corporate intranets**, where every
visitor is authenticated via Azure EntraID SSO and identified by their UPN.

## Relationship to Umbraco Engage

Analyzer is an **independent product** — it has **no runtime, package, or
source dependency on `Umbraco.Engage`** and **does not duplicate any Engage
code**. The Engage Analytics module is used purely as a public-domain
reference for the *mechanics* of server-side analytics, per-content-node
reporting and custom events; Analyzer's own requirements are scoped to an
authenticated intranet audience and diverge from Engage wherever the
public-internet framing of Engage doesn't apply (notably: 100% of pageviews
are identified from the first request, the visitor identifier is derived
from the EntraID `oid` claim (with `upn` as display form and fallback) rather than a tracker cookie, and the
"Unidentified Visitor" state does not exist). Several Engage analytics
surfaces are explicitly out of scope: §2.4 dependencies on other Engage
modules (`FR-DEP-*`), §3.3 Campaigns/UTM tracking (`FR-DIM-04`), §3.3
Geographic location tracking (`FR-DIM-03`, off by default), §3.3 Bot
detection as an active capability, and §6.2 public-website features such as
cookie-consent banners. Operational compliance items — publishing state-law
electronic-monitoring notices and responding to CCPA right-to-know /
right-to-delete requests on the front-end — are the deploying organisation's
responsibility, not features of Analyzer; the product provides the
backoffice export and delete operations needed to *support* those requests
when they arrive. See [CLAUDE.md](CLAUDE.md) for the canonical
scope-drop list.

The reference mechanics document lives at
[`docs/Umbraco_Engage_Analytics_Intranet_Requirements.md`](docs/Umbraco_Engage_Analytics_Intranet_Requirements.md)
(Umbraco Engage v17 LTS, draft dated 2026-05-12). Analyzer's own
authoritative requirements live under [`specs/`](specs/) and the project
[constitution](.specify/memory/constitution.md).

For the current slice plan — every planned feature, its epic, and
completion status — see [`specs/overall-scope.md`](specs/overall-scope.md).

## Repository layout

```
.
├── Analyzer.slnx                # solution file
├── docs/                        # specifications and design notes
└── src/
    └── Analyzer/                # Razor Class Library (the package)
        ├── Analyzer.csproj
        ├── Directory.Packages.props   # pinned Umbraco 17.3.5 packages
        ├── Composers/                  # IComposer registrations
        ├── Controllers/                # backoffice / management API controllers
        ├── Constants.cs
        └── Client/                     # backoffice TypeScript (Vite + @umbraco-cms/backoffice)
            ├── package.json
            ├── public/umbraco-package.json
            └── src/
```

## Prerequisites

- .NET SDK **10.0+**
- Node **20.17+** (LTS) for the `Client/` workspace
- An Umbraco CMS 17.x host site to consume the RCL as a project reference
  during development

## Building

```bash
# Server-side
dotnet restore
dotnet build

# Backoffice client
cd src/Analyzer/Client
npm install
npm run build           # one-shot build → wwwroot/App_Plugins/Analyzer/analyzer.js
npm run watch           # rebuild on change (use alongside a referencing Umbraco site)
```

## Architecture (inspired by Engage mechanics)

Analyzer borrows the *shape* of analytics from Engage — concepts that are
well-established in the web-analytics domain — but implements them from
scratch against intranet-specific requirements:

- **Visitors** — identified employees, keyed by the EntraID `oid` (UPN as
  display form) and unified across sessions and devices; no anonymous /
  unidentified state
- **Sessions** — bounded sequences of interactions by a single visitor on a
  single device, within a configurable inactivity timeout
- **Events** — pageviews, custom events, video, scroll, and form
  interactions, all attached to a visitor and a content node

Analyzer-defined extension surfaces (independent names and namespaces; not
copies of Engage's API):

- `IVisitorIdentifier` / `BaseVisitorIdentifier` — the integration point
  that derives the visitor identifier from the authenticated EntraID claims
  (replaces Engage's cookie-first identification with `oid`-first
  identification; UPN is the display form)
- `IEventDimensionExtractor` / `BaseEventDimensionExtractor` — custom
  extractors that enrich each event with additional dimensions (e.g. EntraID
  `department`, `officeLocation`) at request time
- An analytics state provider for reading current-pageview events,
  visitor identity and enrichment attributes server-side
- A management API under the Analyzer backoffice route (not under
  `/umbraco/engage/...`) for surfacing dashboards, custom events, goals and
  Traffic Filters
- A client-side event push API (e.g. `analyzer.send("event", category,
  action, label)`) for in-page custom events such as search submissions and
  quick-link clicks
- Per-content-node Analytics content app — every content node exposes its
  own usage view (pageviews, unique visitors, average time on page, scroll
  heatmap) directly in the Umbraco backoffice, gated by role so that
  individual-level UPN data is visible only to an authorised user group
- Web-component-based dashboard tiles and reporting visualisations
  delivered through the backoffice client bundle
