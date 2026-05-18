# Requirements Specification

## Analytics Module — Intranet Deployment

**Product:** Umbraco Engage (add-on for Umbraco CMS)
**Deployment context:** US-based corporate intranet, users authenticated by Azure Entra ID and identified by UPN
**Module in scope:** Analytics (standalone — no other Engage modules in scope)

| | |
|---|---|
| **Document type** | Module requirements specification |
| **Module** | Analytics (standalone) |
| **Product** | Umbraco Engage |
| **Reference version** | Umbraco Engage v17 (LTS) |
| **Status** | Draft |
| **Date** | May 12, 2026 |

---

## 1. Introduction

### 1.1 Purpose

This document specifies the functional, non-functional and integration requirements for the **Analytics module of Umbraco Engage** as deployed on a US-based corporate intranet. The deployment under scope is an internal portal where every visitor is a pre-authenticated employee whose identity is established by Azure Entra ID and represented by their User Principal Name (UPN).

The specification is intended for product owners, internal communications stakeholders, intranet editors, developers, data analysts and privacy/compliance officers involved in operating intranet analytics on Umbraco Engage.

### 1.2 Scope

The Analytics module shall be deployed **standalone**: only the analytics capabilities of Umbraco Engage are in scope. The Personalization, A/B Testing, Profiling and external Reporting modules are explicitly out of scope (see §6.2). The Analytics module's own dashboards inside the Engage > Analytics section are in scope.

The module must:

- collect server-side first-party analytics on every authenticated pageview;
- attribute every event to the authenticated employee using their **UPN as the canonical visitor identifier**, eliminating the "unidentified visitor" state typical of public-website tracking;
- support the analytics dimensions and tools that are useful for an intranet (content consumption, internal search, video, forms, custom events, goals) while explicitly excluding marketing-oriented features that have no place internally;
- respect the elevated privacy expectations of employee monitoring, including transparency, role-based access to individual-level data, retention controls and lawful-basis configuration.

### 1.3 Definitions and Acronyms

| Term | Definition |
|---|---|
| **Entra ID** | Microsoft Entra ID (formerly Azure Active Directory), the identity provider that authenticates all intranet users. |
| **UPN** | User Principal Name. The Entra ID attribute used as the canonical employee identifier (e.g. `alice.smith@contoso.com`). |
| **OIDC** | OpenID Connect, the authentication protocol used between the intranet and Entra ID. |
| **Pageview** | A server-side recorded event representing the rendering of a single page for a single visitor. |
| **Visitor / Profile** | The persistent record representing one employee across sessions and devices, keyed by UPN. |
| **Session** | A series of interactions by the same visitor within a configurable inactivity timeout. |
| **Goal** | A configurable conversion definition (e.g. a specific pageview, a form submission, a custom event). |
| **Tracker** | The `umbracoEngage.analytics.js` script + supporting server endpoints that collect and persist analytics data. |
| **Document Type** | Umbraco content schema defining the structure of content nodes. |
| **Engage section** | The Umbraco Engage area in the Umbraco backoffice; in this deployment only the Analytics sub-section is operational. |

### 1.4 References

- Umbraco Engage product page — https://umbraco.com/products/add-ons/engage/
- Umbraco Engage — Analytics documentation — https://docs.umbraco.com/umbraco-engage/marketers-and-editors/analytics
- Umbraco Engage — Developer documentation, Analytics section — https://docs.umbraco.com/umbraco-engage/developers/analytics
- Umbraco Engage — Extending Analytics — https://docs.umbraco.com/umbraco-engage/developers/analytics/extending-analytics
- Umbraco Engage — Security and privacy — https://docs.umbraco.com/umbraco-engage/security-and-privacy
- Microsoft — Entra ID OpenID Connect protocol — https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols-oidc
- California Civil Code §§1798.100 et seq. (CCPA / CPRA) — employee data provisions
- New York Civil Rights Law §52-c — Notice of electronic monitoring

---

## 2. Module Overview

### 2.1 Position within Umbraco Engage

Engage is delivered as a single `Umbraco.Engage` NuGet package containing five capability areas: Analytics, A/B Testing, Personalization, Profiling and Reporting. Analytics is the foundational area and the only one in scope for this deployment. Although the package physically contains the other modules, their backoffice sections shall be hidden from users, their license features shall not be activated, and no operational requirement of the intranet shall depend on them.

### 2.2 Conceptual Model

The Analytics module operates on three primary concepts:

- **Visitor** — one employee, uniquely identified by UPN, persistent across devices and sessions.
- **Session** — a bounded sequence of pageviews and events from one visitor on one device.
- **Event** — an atomic interaction (pageview, form interaction, video event, scroll measurement, custom event, goal trigger).

All events are attached to a visitor; all visitors in this deployment are **identified from their very first pageview** because the intranet is only accessible to authenticated users. The conventional "Identified vs. Unidentified" split shown in Engage on public websites is therefore not applicable: 100% of visitors shall be identified at all times.

### 2.3 Primary Users

- **Internal communications and intranet editors** — view dashboards to understand how content is being consumed and to prioritise content work.
- **Content owners** — see usage per page or per Document Type for the pages they own.
- **HR / Learning & Development** — track training video completion and policy acknowledgement form submissions.
- **IT / intranet platform team** — operate the module, manage Document Type configuration, custom events and Traffic Filters.
- **Privacy/Compliance Officer** — validates that analytics processing remains compliant with employee privacy obligations and applicable state-law disclosure requirements (see §5.3).

### 2.4 Standalone Operation — No Dependencies on Other Engage Modules

The deliberate isolation of the Analytics module mirrors the equivalent section in the Customizer (Personalization) specification and in the Adjuster (A/B Testing) specification.

| Engage module | Required in this deployment? | Notes |
|---|---|---|
| Analytics | **Yes (sole module in scope)** | Provides all functionality required by this specification. |
| Personalization | **No** | The backoffice section shall be hidden; no segments, personalizations or persona scoring shall be configured. |
| A/B Testing | **No** | No experiments shall be defined. The section shall be hidden. |
| Profiling | **No (as a standalone module)** | The internal visitor records that Analytics inherently creates are part of Analytics itself; the Profiling module's backoffice section and 360° features shall not be activated. |
| Reporting | **No** | The dedicated Reporting module is out of scope. Only the dashboards and reports that live **within** the Analytics section are in scope. |

| ID | Requirement |
|---|---|
| **FR-DEP-01** | The intranet shall function fully using only the Analytics capabilities of Umbraco Engage. No business requirement in this document shall require Personalization, A/B Testing, Profiling or Reporting to be configured or used. |
| **FR-DEP-02** | The Engage license shall be configured for a tier that enables Analytics; tiers that include Personalization, A/B Testing or Profiling shall not be required and shall not be assumed. |
| **FR-DEP-03** | The backoffice sections for Personalization, A/B Testing, Profiling and Reporting shall be hidden from all non-administrator users through Umbraco user-group permissions, to prevent accidental configuration of out-of-scope features. |
| **FR-DEP-04** | No extension implemented for this intranet (custom extractors, custom events, identity integration) shall take a hard dependency on types or services from the Personalization, A/B Testing, Profiling or Reporting modules. |
| **FR-DEP-05** | Should a future phase introduce one of the out-of-scope modules, the Analytics implementation defined here shall remain valid without modification. |

---

## 3. Functional Requirements

### 3.1 Data Collection

The Analytics module shall use Engage's hybrid collection model: server-side capture of each page request, complemented by a client-side script for events that can only be observed in the browser.

| ID | Requirement |
|---|---|
| **FR-COL-01** | Every page request rendered by the Umbraco front-end shall be recorded as a pageview by the server-side analytics pipeline, independent of whether the client-side tracker script executes. |
| **FR-COL-02** | The client-side tracker (`umbracoEngage.analytics.js`) shall be included on every page of the intranet and shall send client-side events (custom events, video, scroll, form interactions) to the server. |
| **FR-COL-03** | The server-side tracker shall not rely on third-party services, third-party cookies or external analytics providers. All collection shall be first-party. |
| **FR-COL-04** | The system shall persist all collected data in the same Umbraco database as the CMS, with no data leaving the customer's hosting environment. |
| **FR-COL-05** | The tracker shall continue collecting data correctly when client-side blockers (ad blockers, tracker blockers) are present, by virtue of server-side collection. |
| **FR-COL-06** | The `umbraco-engage-no-tracking` attribute shall be respected on elements and forms where editors explicitly want to suppress tracking (e.g. on sensitive HR forms where field-level tracking is not appropriate). |

### 3.2 Visitor Identification via Entra ID / UPN

This is the central deviation from the default Engage behaviour. Because the intranet is exclusively accessible to authenticated users, visitor identity shall be established **deterministically from the authentication context**, not from a tracker cookie.

| ID | Requirement |
|---|---|
| **FR-ID-01** | Every analytics event shall be attributed to the authenticated user's UPN. The UPN shall be obtained from the OIDC claim `preferred_username` (or `upn`, as configured) returned by Entra ID. |
| **FR-ID-02** | The system shall use the UPN as the canonical visitor identifier. The visitor profile in the Engage database shall be keyed by a stable hash or direct value derived from UPN, so that the same employee is recognised as a single visitor across sessions, devices and browsers. |
| **FR-ID-03** | The visitor identifier shall be assigned **at or before the first pageview of each session**, so that no pageview is ever recorded as "Unidentified". |
| **FR-ID-04** | The mapping from UPN to visitor identifier shall be deterministic and reversible only inside the Engage backoffice for authorised users. UPNs shall not be exposed in cookies, URLs or browser-visible storage. |
| **FR-ID-05** | If a request reaches the Umbraco front-end without an authenticated Entra ID session (e.g. an internal monitoring probe), the request shall not be recorded as a pageview. The system shall not generate an anonymous fallback visitor. |
| **FR-ID-06** | UPN changes (e.g. due to surname change, domain migration) shall be supported via a documented administrative operation that merges the historical visitor profile with the new UPN, preserving analytics history. |
| **FR-ID-07** | When an employee leaves the organisation, the configured retention or anonymisation policy (see §5.3) shall be applied to their visitor profile and associated analytics history. |

### 3.3 Tracked Dimensions

The Analytics module shall record at least the following dimensions for each pageview / session, where applicable to an intranet context.

| Dimension | In scope? | Notes |
|---|---|---|
| Pageviews and unique pageviews | Yes | Per page, per Document Type, per section of the intranet. |
| Sessions, session duration, pages per session | Yes | Bounded by configurable inactivity timeout. |
| Visitors (active employees) | Yes | Counted by distinct UPN over the selected date range. |
| Entry / exit pages | Yes | Useful to identify common landing pages from the intranet home. |
| Time on page | Yes | Useful proxy for content engagement on long-form policy pages. |
| Referrer (internal links only) | Yes | Internal navigation paths. External referrers shall be expected to be empty or irrelevant. |
| Device type, browser, OS | Yes | Useful for support and to validate device coverage of intranet features. |
| Geographic location | Optional | May be enabled only where multi-region offices justify it. See §5.3 for privacy considerations. |
| Campaigns (UTM) | **Out of scope** | Marketing-oriented; no business need. Not used. |
| Bot detection | **Not relevant** | Authenticated intranet — bots cannot reach the front-end. The capability remains but is not relied upon. |

| ID | Requirement |
|---|---|
| **FR-DIM-01** | The Analytics dashboards in the Engage backoffice shall present at minimum: pageviews, unique pageviews, sessions, average session duration, pages per session, and unique visitors, filterable by date range and content node. |
| **FR-DIM-02** | All metrics shall be available aggregated and broken down by Document Type. |
| **FR-DIM-03** | Geographic data collection shall be **disabled by default** in this deployment, and shall only be enabled with explicit approval from the organisation's privacy/compliance owner. |
| **FR-DIM-04** | Campaign tracking shall not be configured, and the system shall not surface campaign reports to standard intranet editors. |

### 3.4 Forms Tracking

Internal forms (HR requests, policy acknowledgements, internal feedback, IT tickets) are a primary use case on an intranet. Engage Analytics tracks forms automatically when Umbraco Forms is used.

| ID | Requirement |
|---|---|
| **FR-FRM-01** | Form analytics shall be enabled automatically for all Umbraco Forms on the intranet when the analytics JavaScript is included on the page. |
| **FR-FRM-02** | The system shall record, per form: number of impressions, number of starts, number of successful submissions, abandonment rate, time-to-start and time-to-submit. |
| **FR-FRM-03** | Field-level analytics shall record focus/unfocus events and whether each field contained data at the time of unfocus, enabling identification of fields that cause abandonment. |
| **FR-FRM-04** | Forms or individual fields containing especially sensitive data (e.g. HR grievance forms) shall be excluded from tracking by adding the `umbraco-engage-no-tracking` attribute. The system shall respect this attribute. |
| **FR-FRM-05** | Submitted form entries in Umbraco Forms shall be linkable to the corresponding visitor profile via the Visitor ID field type provided by Engage. |

### 3.5 Video Tracking

| ID | Requirement |
|---|---|
| **FR-VID-01** | The Analytics module shall track embedded videos (YouTube, Vimeo, native HTML5) hosted on intranet pages without additional configuration, provided the analytics script is loaded. |
| **FR-VID-02** | For each tracked video, the system shall record: number of impressions (loads), number of plays, completion percentage milestones (25%, 50%, 75%, 100%) and total watch time. |
| **FR-VID-03** | Video analytics shall be visible per page and aggregated per video across pages. |
| **FR-VID-04** | The system shall not interfere with existing YouTube IFrame Player instances already initialised on the page (defensive against double initialisation). |

### 3.6 Scroll Heatmap

| ID | Requirement |
|---|---|
| **FR-HMP-01** | A scroll heatmap shall be available for each content node, showing the proportion of visitors reaching successive depths of the page. |
| **FR-HMP-02** | The scroll heatmap shall be accessible to authorised editors as a content-app view on the relevant content node. |
| **FR-HMP-03** | Scroll heatmap data shall be aggregated; individual visitor scroll traces shall not be exposed by default. |

### 3.7 Custom Events

| ID | Requirement |
|---|---|
| **FR-EVT-01** | Developers shall be able to push custom client-side events via `umbEngage("send", "event", category, action, label)`. |
| **FR-EVT-02** | Custom events shall appear in the Events report inside the Analytics section, filterable by category, action and label. |
| **FR-EVT-03** | At a minimum, the following intranet-specific events shall be captured: clicks on top-navigation entries, clicks on quick-link tiles, opens of policy / handbook PDFs, downloads of attachments, and submissions of internal-search queries (see §3.9). |
| **FR-EVT-04** | Custom event definitions shall be documented in the intranet's developer handbook and shall use a consistent category/action/label naming convention. |

### 3.8 Goals

| ID | Requirement |
|---|---|
| **FR-GOL-01** | The system shall allow authorised users to define goals based on: a specific pageview, a form submission, or a custom event. |
| **FR-GOL-02** | Goal completion shall be reportable per page, per Document Type, and per employee group (see §4.3 for enrichment). |
| **FR-GOL-03** | Initial goals shall include: completion of mandatory training video, submission of annual policy acknowledgement form, and first visit to a newly published key page. |

### 3.9 Internal Search Tracking

Internal search is a critical signal of content findability on an intranet. Engage's custom events shall be used as the implementation mechanism.

| ID | Requirement |
|---|---|
| **FR-SRC-01** | Each submission of a search query through the intranet's search bar shall raise a custom event with category `Search`, action `Query`, and label = the search term. |
| **FR-SRC-02** | A separate event with category `Search`, action `NoResults` shall be raised whenever a search returns zero results. |
| **FR-SRC-03** | The Analytics section's Events report shall be used to identify the most common queries, queries returning no results, and queries followed by no click-through. |
| **FR-SRC-04** | Search queries shall be considered potentially personal data (they may include names of colleagues or sensitive topics). Retention and access rules for search events shall be aligned with §5.3. |

### 3.10 Reporting and Dashboards (within Analytics)

| ID | Requirement |
|---|---|
| **FR-RPT-01** | All reporting required by this specification shall be delivered through the dashboards available **within the Engage > Analytics section** of the Umbraco backoffice. The standalone Engage Reporting module shall not be used (see §6.2). |
| **FR-RPT-02** | The Analytics dashboard shall be the default landing view in the Engage section for users with the appropriate role. |
| **FR-RPT-03** | Each content node shall expose an Analytics content app showing pageviews, unique visitors, average time on page and the scroll heatmap for that node. |
| **FR-RPT-04** | Editors shall be able to filter reports by date range (preset ranges plus custom range) without involving developers or external BI tools. |
| **FR-RPT-05** | Reports shall reflect collected data with end-to-end latency low enough that a pageview occurring during business hours is visible in dashboards within the same working day. |

### 3.11 Traffic Filters and Suspicious Activity

Even on an intranet, monitoring traffic and IT probes can pollute analytics. The Traffic Filters and Suspicious Activity features introduced in recent Engage versions shall be used.

| ID | Requirement |
|---|---|
| **FR-FLT-01** | Traffic Filters shall be configured to exclude IT monitoring probes, automated uptime checks and any service accounts that may issue authenticated requests, using matching by URL, user agent and/or IP/CIDR. |
| **FR-FLT-02** | A Traffic Filter shall be configured to exclude requests originating from the operations team's load-testing tooling on non-production environments mirrored to production data, where applicable. |
| **FR-FLT-03** | The Suspicious Activity overview shall be monitored periodically; any flagged visitor with abnormal pageview patterns shall be reviewed and, if confirmed as automation, added to Traffic Filters. |

---

## 4. Identity Integration Requirements

This section defines what is required to integrate Engage Analytics with Entra ID-issued identities. It assumes that the intranet itself authenticates users via Entra ID (OIDC) at the application level; this specification covers only what Analytics needs from that authentication, not the authentication mechanism itself.

### 4.1 Authentication Context

| ID | Requirement |
|---|---|
| **FR-IDP-01** | Every front-end request that reaches the Umbraco rendering pipeline shall carry a verified Entra ID authentication context. The intranet's authentication middleware shall reject anonymous traffic before it reaches the analytics pipeline. |
| **FR-IDP-02** | The minimum claims required from Entra ID are: `preferred_username` (or `upn`) for identification, and `oid` (object ID) as a stable fallback if UPN changes. Additional optional claims used for enrichment are defined in §4.3. |
| **FR-IDP-03** | Token lifetime and refresh shall be handled by the intranet's authentication layer. Engage Analytics shall not store, refresh or transmit Entra ID tokens. |

### 4.2 UPN as Visitor Identifier — Implementation Boundary

| ID | Requirement |
|---|---|
| **FR-IDP-04** | A custom extension shall integrate the authenticated identity into the Engage analytics pipeline at request time. This extension shall use Engage's documented "Extending Analytics" extractor mechanism to set the visitor identifier based on UPN. |
| **FR-IDP-05** | The visitor identifier persisted in the Engage database shall be either the UPN itself or a deterministic hash thereof. Where a hash is used, it shall be: salted with a value held only on the server, computed using a cryptographic hash (SHA-256 or stronger), and consistent across application instances so the same UPN always maps to the same identifier. |
| **FR-IDP-06** | The UPN (in cleartext) shall be retrievable from the visitor profile **only** by users in an explicitly authorised Umbraco user group (e.g. "Intranet Analytics Administrators"). For standard editors, the visitor shall be represented by a pseudonymous handle or display name. |
| **FR-IDP-07** | Visitor cookies issued by the default Engage tracker may still be set for session continuity within a single browser session, but they shall not be the authoritative identity source. On every request, the authenticated UPN shall override any cookie-derived identifier. |

### 4.3 Profile Enrichment from Entra ID Claims

Beyond raw UPN, additional Entra ID attributes are valuable for intranet analytics segmentation (e.g. department-level content consumption) but raise privacy questions and shall be opt-in.

| ID | Requirement |
|---|---|
| **FR-ENR-01** | The integration shall be able to enrich the visitor profile with selected Entra ID claims: `department`, `jobTitle`, `officeLocation`, `country`, `companyName`. The exact set shall be confirmed with the organisation's privacy/compliance owner before activation. |
| **FR-ENR-02** | Enrichment claims shall be refreshed on each session start, so changes (e.g. department change) are reflected without delay. |
| **FR-ENR-03** | The reports inside the Analytics section shall be filterable by enrichment attributes (e.g. "pageviews from the Finance department"), provided the attribute has been activated per FR-ENR-01. |
| **FR-ENR-04** | No enrichment attribute classed as sensitive (e.g. health, ethnicity, religion, sexual orientation, union membership, immigration status) shall be loaded into Engage, irrespective of whether such attributes are available in Entra ID. |
| **FR-ENR-05** | Filters and reports shall enforce a **minimum cohort size** (e.g. n ≥ 10) before showing department- or team-level breakdowns, to prevent de-facto re-identification of individuals in small groups. |

### 4.4 Cross-device Profile Unification

| ID | Requirement |
|---|---|
| **FR-IDP-08** | Because identification is based on UPN rather than on cookies, the same employee accessing the intranet from corporate laptop, corporate phone and managed tablet shall be represented as a single visitor with a unified history. |
| **FR-IDP-09** | Device and browser dimensions shall remain available per session, so cross-device usage patterns can still be analysed per visitor. |

---

## 5. Non-Functional Requirements

### 5.1 Usability

| ID | Requirement |
|---|---|
| **NFR-USA-01** | All analytics consumption (dashboards, content apps, exports) shall be operable by communications and content staff without developer involvement. |
| **NFR-USA-02** | The Analytics section shall be the default Engage landing view for users in the standard editor and content-owner roles. Sections for out-of-scope modules shall be hidden via user-group permissions. |
| **NFR-USA-03** | Every report shall provide a clear date-range selector and a clear indication of the data freshness at the time of viewing. |

### 5.2 Performance

| ID | Requirement |
|---|---|
| **NFR-PER-01** | Analytics collection shall add negligible latency to page rendering. Server-side collection shall be asynchronous or pipelined such that it does not block the response to the user. |
| **NFR-PER-02** | The dashboards shall remain responsive at the expected scale of the intranet (target: 10,000 employees, 200,000 pageviews per working day). Concrete performance targets shall be agreed during platform design. |
| **NFR-PER-03** | The custom visitor-identification extension shall not perform synchronous network calls to Entra ID per request; UPN shall be read from the in-process authentication context only. |

### 5.3 Security and Data Handling

Intranet analytics is a form of employee monitoring and triggers obligations that go beyond standard public-website analytics. This deployment targets corporations operating in the United States. The requirements below cover what the codebase must provide and what the deploying organisation must be able to configure. Broader policy items (employee handbook language, HR governance, etc.) are the deploying organisation's responsibility and are not features of Analyzer. This subsection is intentionally more developed than its counterpart in the Customizer (Personalization) specification.

| ID | Requirement |
|---|---|
| **NFR-SEC-01** | All analytics data shall be stored in the customer's own Umbraco database, hosted in a tenant under organisational control. No third-party processor shall receive analytics data without a separate data-processing agreement. |
| **NFR-SEC-02** | Processing of analytics shall be documented in the organisation's internal data-handling records, including the purposes of processing, the categories of data collected, the retention periods applied and the access controls in place. The documentation shall be sufficient to respond to data subject inquiries under applicable state laws (e.g. CCPA/CPRA for California-resident employees). |
| **NFR-SEC-03** | A transparent **employee privacy notice** shall be published on the intranet, describing what is collected, for what purpose, how long it is retained, and who can access individual-level data. The notice shall be visible to every employee at first logon and accessible at any time thereafter. |
| **NFR-SEC-04** | Where required by state law (e.g. New York Civil Rights Law §52-c, Connecticut General Statutes §31-48d, Delaware Code Title 19 §705, or equivalents adopted by other states), the deploying organisation shall publish the required prior written notice of electronic monitoring before activating analytics. Analyzer does not generate or display this notice. |
| **NFR-SEC-05** | Access to individual-level data (the visitor profile view including UPN) shall be restricted to a named user group, audited, and shall not be granted to line managers as a default. Standard editors shall see aggregated data only. |
| **NFR-SEC-06** | Retention periods shall be configured explicitly using Engage's retention settings: raw event data ≤ 13 months by default (rationale: enable year-over-year comparison only); aggregated reports may be retained longer. The configured values shall be reviewed annually. |
| **NFR-SEC-07** | Anonymisation shall be applied automatically once an employee's account in Entra ID is disabled or deprovisioned, on a configurable delay. After anonymisation, historical events shall remain in aggregate counts but shall no longer be linkable to a former employee. |
| **NFR-SEC-08** | An authorised administrator shall be able to **export**, in a structured format, all analytics events linked to a given UPN, and to **permanently delete** that subject's analytics history on lawful request. These operations support the right-to-know and right-to-delete obligations under CCPA/CPRA for California-resident employees, and equivalent rights in other states as they adopt them. |
| **NFR-SEC-09** | Analytics shall not be used as the sole basis for individual performance evaluation or disciplinary action. This restriction shall be documented in the privacy notice and in role-based access guidance. |
| **NFR-SEC-10** | Individual-level dashboards shall not surface out-of-hours usage of identifiable employees in a way that could enable surveillance of working time outside an employee's agreed schedule. Aggregate or de-identified out-of-hours patterns remain available. |

### 5.4 Compatibility and Integration

| ID | Requirement |
|---|---|
| **NFR-CMP-01** | The module shall be delivered as the `Umbraco.Engage` NuGet package on a supported Umbraco CMS version per the Engage release notes. |
| **NFR-CMP-02** | The module shall be compatible with Umbraco Forms (used for internal forms tracking) and shall not require any other Umbraco add-on. |
| **NFR-CMP-03** | The module shall co-exist with the intranet's authentication middleware (Entra ID OIDC integration on the public-facing site) without interfering with the authentication flow. |
| **NFR-CMP-04** | Configuration shall be transferable between environments (development, staging, production) using Umbraco Deploy or equivalent, including Document Type segmentation flags, custom event definitions and goal definitions. |
| **NFR-CMP-05** | If Adjuster (A/B Testing) or Customizer (Personalization) are also deployed, Analytics shall coexist with them without conflict and shall not depend on either. Custom event names used as analytics goals shall follow the same naming convention as the goals defined in the Adjuster specification. |

### 5.5 Maintainability and Extensibility

| ID | Requirement |
|---|---|
| **NFR-MNT-01** | The custom visitor-identification extension and any custom extractors shall be implemented using Engage's documented extension points and shall be covered by automated tests. |
| **NFR-MNT-02** | All custom event names shall be defined in a single shared module of the codebase to prevent naming drift; the same module shall be the source of documentation in the intranet developer handbook. |
| **NFR-MNT-03** | Engage upgrades shall be assessed against this specification at least once per major version. Breaking changes to the extending-analytics extension point shall trigger a review of the identity integration. |

### 5.6 Licensing

| ID | Requirement |
|---|---|
| **NFR-LIC-01** | A valid Umbraco Engage license at the Analytics tier shall be installed and shall cover the production domain plus the development/staging domains required for the project. |
| **NFR-LIC-02** | The license dashboard inside the Engage section shall display a valid status. License renewals shall be tracked by the platform team with at least 30 days' lead time before expiry. |

---

## 6. Acceptance Criteria, Out-of-Scope and Assumptions

### 6.1 High-level Acceptance Criteria

- An authenticated employee navigating the intranet generates pageview events that are attributed to their UPN from the very first request, with no "Unidentified" state ever recorded.
- The same employee using a corporate laptop and a corporate phone appears as a single visitor in the Engage backoffice, with a unified session history.
- Internal communications staff can open the Analytics section and answer, without developer help: "Which pages did the Finance department visit most this month?", "Which intranet search queries returned no results?", and "What is the completion rate of the mandatory compliance training video?".
- Forms tracking is operational on every Umbraco Form on the intranet, with abandonment data visible per form.
- Custom search events appear in the Events report and are filterable by query.
- The privacy/compliance officer can validate, from the Analytics section and configuration files, that: retention periods are set as agreed, no sensitive attributes are loaded into Engage, and individual-level data is accessible only to the authorised user group.
- Visitor profile lookups expose UPN only to the authorised user group; standard editors see a pseudonymous display name only.
- Disabling an employee's Entra ID account triggers anonymisation of their visitor profile after the configured delay.
- The Personalization, A/B Testing, Profiling and Reporting sections of the Engage backoffice are hidden for non-administrator users.

### 6.2 Out-of-Scope

- **Personalization, A/B Testing, Profiling and standalone Reporting modules of Umbraco Engage.** These are explicitly excluded from this deployment.
- Marketing-style campaign and UTM tracking.
- Geographic tracking (disabled by default; may be enabled later only with privacy/compliance owner approval).
- Bot detection as an active capability (the authenticated intranet has no relevant bot traffic).
- Public-website features such as cookie consent banners.
- The Entra ID authentication itself — covered by the intranet's authentication specification, not by this document.
- BI / data-warehouse integration to push analytics out of Umbraco — possible future phase, not covered here.
- Mobile native apps — only the browser-based intranet is in scope.

### 6.3 Assumptions

- The host Umbraco CMS installation is on a supported version per the Engage release notes, healthy and on the project-agreed infrastructure.
- The intranet is exclusively accessible behind Entra ID authentication; no anonymous routes exist on the front-end.
- Entra ID issues the OIDC claims required by §4.1 and §4.3 for every authenticated session.
- The intranet uses Umbraco Forms for internal forms, where automatic form analytics is required.
- The organisation's privacy/compliance owner has reviewed and signed off on the proposed retention periods, enrichment attributes and access model. Where any employees are California residents, CCPA/CPRA-aligned controls are configured per §5.3. Where employees reside in New York, Connecticut, Delaware or any state with an electronic monitoring notice statute, the deploying organisation has published the required written notice on its own surfaces (employee handbook, onboarding portal, or equivalent). Analyzer does not generate or display this notice.
- The Engage Analytics tracker script is included on every front-end page template; pages that opt out of tracking do so explicitly via the `umbraco-engage-no-tracking` attribute, not by omission of the script.
