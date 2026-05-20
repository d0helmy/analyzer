# Feature Specification: Per-Content-Node Analytics Content App

**Feature Branch**: `008-content-analytics-app`

**Created**: 2026-05-20

**Status**: Draft

**Input**: User description: "Slice 008 — per-content-node Analytics content app (FR-RPT-*, contract D9). The first non-capture slice. Every Umbraco content node gets an 'Analytics' content app tab in the backoffice that shows the node's own usage metrics, role-gated so individual-level UPN data is visible only to an authorised user group (NFR-SEC-*). Pure read-side: aggregates from existing capture tables shipped in slices 002-007, no new capture surface."

## Clarifications

### Session 2026-05-20

- Q: Who can see the Analytics tab? → A: Any backoffice user with section access to the content node — no Analyzer-defined gate. Umbraco's existing content/section permissions are the only door. The "Analytics.IndividualData" group remains a separate gate that only applies to future per-visitor drill-down fields, not to the aggregate tab.
- Q: How is "average time on page" computed? → A: Delta between this pageview and the next pageview in the same session (conventional web-analytics approach). The last pageview in a session is excluded from the average because there is no successor pageview to bound its duration. Scroll-sample data (slice 006) is NOT used in this computation in MVP.
- Q: What does the "tombstoned content" field reflect? → A: The content node's *current* Umbraco state — i.e. "is this node currently in the recycle bin / unpublished?", determined by an `IPublishedContent` lookup at query time. NOT the historical `customizerVisitorPageview.wasContentTombstoned` flag (which captures the state at pageview-capture time and remains available for future slices that want historical semantics). Consequence: the response field is renamed from `wasContentTombstoned` to `isContentCurrentlyTombstoned` to reflect the present-tense semantic.
- Q: What does the "Analytics.IndividualData" role gate do in MVP? → A: Gate is wired but invisible in MVP. A check-function (e.g. `IIndividualDataAccessCheck.IsAuthorised(ClaimsPrincipal)`) is shipped and unit-tested for user-group membership behaviour. No integration test against the endpoint payload because the MVP response shape carries no per-visitor fields to filter. The first slice to introduce per-visitor fields owns the integration-test coverage for the gate's filtering behaviour against the wire response.
- Q: What does the tab show while the aggregation query runs? → A: Skeleton placeholders. The metric layout renders with grey placeholder rectangles while loading, and the container element carries `aria-busy="true"`. When the request resolves, the skeleton swaps to real numbers in a single re-render. No centred spinner, no transient zeros (zeros collide with the empty-state semantic), no blank tab. Vitest tests assert skeleton presence and the `aria-busy` attribute deterministically.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Editor reviews a content node's usage at a glance (Priority: P1)

A backoffice editor opens any published content node in the Umbraco backoffice, switches to the new "Analytics" tab, and immediately sees aggregate usage numbers for that specific node: pageviews in the last 24 hours, last 7 days, and last 30 days; unique visitors in the last 30 days; and average time on page in the last 30 days. The view is read-only — there are no inputs or actions, just numbers.

**Why this priority**: This is the entire MVP of the slice. Seven capture slices (002-007) have shipped without a single surface where operators can see what was captured. P1 closes that gap with the most-asked-for view: "how much traffic does this page get?"

**Independent Test**: Sign in to the backoffice as any user with default "Editor" group permissions. Open a published content node that has historical pageviews. Click the "Analytics" tab. The tab renders inside ~2 seconds with five labelled metric blocks showing real numbers (not zeros) joined from the capture tables shipped in prior slices. Refreshing the page returns the same numbers; navigating to a different node returns that node's numbers.

**Acceptance Scenarios**:

1. **Given** a published content node with at least 5 historical pageviews in the last 30 days, **When** the editor opens the Analytics tab, **Then** the 30-day pageview metric shows the correct count, the 7-day count is less-or-equal to the 30-day count, and the 24-hour count is less-or-equal to the 7-day count.
2. **Given** a published content node that has been viewed by 3 distinct visitors in the last 30 days, **When** the editor opens the Analytics tab, **Then** the unique-visitors-30d metric shows 3.
3. **Given** a published content node with session-recorded engagement, **When** the editor opens the Analytics tab, **Then** the average-time-on-page-30d metric shows a positive integer number of seconds with the correct unit label.
4. **Given** the editor switches between three different content nodes in sequence, **When** each Analytics tab loads, **Then** each shows numbers specific to that node — no cross-node bleed.

---

### User Story 2 - Freshly published or never-viewed content shows zero gracefully (Priority: P2)

An editor creates and publishes a brand-new content node, opens its Analytics tab while no visitors have yet viewed it, and sees an explicit "no activity yet" state — zeros for every metric, a clean skeleton-to-data transition (no spinner-stuck-forever), and no error banner.

**Why this priority**: Editors create and publish content all the time. The Analytics tab needs to handle the empty-data state cleanly or the feature trains them to ignore it.

**Independent Test**: Create a fresh content node, do not browse to it from the front-end, immediately open Analytics tab in the backoffice. All five metrics show 0 (or a neutral placeholder for avg-time-on-page since the divisor would be 0); a brief copy line clarifies the state ("No activity in the last 30 days"). No JavaScript console errors. The request to the management endpoint returns 200 with zeros, not 404.

**Acceptance Scenarios**:

1. **Given** a content node with zero historical pageviews, **When** the editor opens the Analytics tab, **Then** all numeric metrics display 0, the avg-time-on-page metric displays a neutral placeholder, and the UI does not show a generic error state.
2. **Given** a content node that has been published but never viewed by any visitor, **When** the management endpoint is requested, **Then** the response returns HTTP 200 with a populated JSON body where each metric is 0, not HTTP 404.

---

### User Story 3 - Anonymised visitors continue to count toward aggregates without leaking identity (Priority: P3)

An editor reviews a content node where some historical visitors have since been anonymised by a Customizer "right-to-be-forgotten" action. The 30-day unique-visitor count still includes those visitors; their UPN, email, or other identifying fields never appear in any payload returned to the editor, even in network-tab JSON.

**Why this priority**: Required by contract — Customizer's anonymisation cascade preserves visitor profile keys (re-keyed, not deleted) so historical aggregates remain accurate. Without this, every anonymisation action would silently degrade reporting and confuse the editor. P3 because no UI in the MVP exposes per-visitor identity anyway, but the data-shape contract has to be defended from day one for forward compatibility.

**Independent Test**: Seed 10 visitors against a content node, anonymise 3 of them through Customizer's cascade, request the management endpoint as a backoffice user. The unique-visitors-30d metric returns 10. The JSON response contains no field named `identityRef`, `upn`, `oid`, `userEmail`, or any other identifying string.

**Acceptance Scenarios**:

1. **Given** 10 distinct visitors viewed a node and 3 were later anonymised, **When** the analytics endpoint is requested, **Then** unique-visitors-30d returns 10.
2. **Given** any analytics endpoint response, **When** the editor inspects the network payload, **Then** no field carrying personally-identifying information appears in the JSON.

---

### Edge Cases

- **Content node that doesn't exist**: management endpoint with an unknown `contentKey` GUID returns HTTP 404 with a structured error body.
- **Soft-deleted (tombstoned) content node**: still queryable historically; the Analytics tab in the backoffice (if accessible at all for trashed content) shows the node's last-known aggregates and a small banner indicating the node is no longer published.
- **Time-window boundary**: a pageview captured at exactly `now − 24h00m00s` is included in the 24h count (window is closed at the past edge to keep counts stable across millisecond drift).
- **High-volume node**: a content node with 100k+ pageviews in 30 days still renders the tab within the SC-002 budget; if not, see Assumptions for the pre-computation deferral.
- **Visitor anonymised between two requests**: aggregate count remains identical for the same time window; the anonymisation is invisible to the editor (only the surface-level UPN drill-down — out of scope for this slice — would observe it).
- **Backoffice user without Settings/Content section access**: cannot reach the tab at all; not an analytics gate but the standard Umbraco section permission.
- **Role-gated drill-down field requested for a non-member**: per-visitor field would be omitted from response body, not 401'd (gracefully degrade so the aggregate view still renders).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-RPT-001**: System MUST expose an "Analytics" content app tab on every Umbraco content node in the backoffice (all document types, no per-doctype opt-in). The tab MUST be visible to any backoffice user who can access the content node under Umbraco's standard section/content permissions — no additional Analyzer-defined visibility gate. (The "Analytics.IndividualData" group in `FR-RPT-007` is a *separate* gate that scopes only the per-visitor drill-down fields reserved for future slices; it does not gate visibility of this aggregate tab.)
- **FR-RPT-002**: The Analytics tab MUST display pageviews aggregated for three fixed time windows: last 24 hours, last 7 days, last 30 days — keyed by the node's `IPublishedContent.Key`.
- **FR-RPT-003**: The Analytics tab MUST display the count of distinct visitors who viewed the node in the last 30 days.
- **FR-RPT-004**: The Analytics tab MUST display average time on page for the last 30 days. The duration of each contributing pageview MUST be derived from the delta between that pageview's `requestUtc` and the `requestUtc` of the next pageview in the same `analyzerSession`. The last pageview in each session MUST be excluded from the average (no successor pageview exists to bound its duration). Scroll-sample data from slice 006 is NOT used in this computation; it remains available for a future heatmap/engagement slice.
- **FR-RPT-005**: A read-only management API endpoint MUST exist at the canonical Analyzer management-API prefix, taking the content node's GUID as a route parameter and returning a JSON envelope with the four metric values plus a placeholder array for "top referrers" (always empty in this slice — reserved for a future click-through slice).
- **FR-RPT-006**: The management endpoint MUST be gated by the same authorisation policy used by other Analyzer management endpoints (backoffice-cookie or equivalent backoffice access policy).
- **FR-RPT-007**: A role-gate check-function MUST be implemented and unit-tested in this slice for forward compatibility with future per-visitor drill-down. The check takes a `ClaimsPrincipal` and returns whether the user belongs to the configured "Analytics.IndividualData" user group. The check-function MUST be exercised by direct unit tests (membership / non-membership / absent-group cases). The MVP response shape carries no per-visitor fields and therefore no endpoint integration test for the gate ships in this slice — the first slice that introduces per-visitor drill-down fields owns the integration-test coverage for the gate's effect on response payloads.
- **FR-RPT-008**: All time-window aggregation MUST be computed from UTC datetimeoffset fields populated by prior capture slices; no local-time conversion happens server-side.
- **FR-RPT-009**: Anonymised visitor profile rows (those re-keyed by Customizer's anonymisation cascade) MUST continue to contribute to unique-visitor aggregate counts; the projection MUST exclude any column carrying their original identity reference.
- **FR-RPT-010**: When the requested content node has zero captured events in the queried window, the endpoint MUST return HTTP 200 with all metric fields set to 0 (or a neutral placeholder for the "average" metric where the divisor would be 0), NOT HTTP 404.
- **FR-RPT-011**: When the requested content node GUID does not exist in any prior slice's capture table AND is not present in the Umbraco content cache, the endpoint MUST return HTTP 404 with a structured error body.
- **FR-RPT-012**: Tombstoned (soft-deleted) content nodes MUST remain queryable through the endpoint — historical analytics are not lost on delete. The response shape MUST include a `isContentCurrentlyTombstoned` boolean reflecting the *current* Umbraco state of the node (recycle bin / unpublished), determined by an `IPublishedContent` lookup at request time. The historical `customizerVisitorPageview.wasContentTombstoned` flag captured at each pageview is NOT exposed in this slice; it remains available in the underlying data for future slices that need at-capture-time semantics.
- **FR-RPT-013**: The Analytics tab MUST render a skeleton-placeholder loading state during the aggregation request, with `aria-busy="true"` on the container element. Skeleton blocks MUST occupy the same layout positions as the eventual metric values so the swap is a single re-render with no layout shift. Centred-spinner, transient-zero, and blank-tab loading states are explicitly disallowed.

### Key Entities *(include if feature involves data)*

- **ContentAnalyticsSnapshot**: A read-side projection representing the aggregate view of one content node at one point in time. Attributes: `contentKey` (the Umbraco node), `windowEndUtc` (the moment "now" the snapshot was computed at), `pageviews24h`, `pageviews7d`, `pageviews30d` (positive integers, monotonic 24h ≤ 7d ≤ 30d), `uniqueVisitors30d` (positive integer), `avgTimeOnPageSeconds30d` (positive number or null when no sessions), `isContentCurrentlyTombstoned` (boolean — current Umbraco recycle-bin / unpublished state, NOT capture-time history), `topReferrers30d` (always empty array in this slice). Not persisted — computed on each request.
- **TimeWindow**: An enumeration of the three fixed periods supported by this slice (24h, 7d, 30d). Future slices may add configurable windows; this slice deliberately constrains the surface.
- **AnalyticsIndividualDataPermission**: A backoffice user group membership check that future slices will use to gate per-visitor drill-down. In this slice it has no UI surface but the check-function is implemented and unit-tested.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A backoffice editor opening the Analytics tab on a content node with up to 10,000 historical pageviews sees rendered numbers within 2 seconds of the tab activating, measured from tab click to "interactive numbers visible".
- **SC-002**: A backoffice editor opening the Analytics tab on a content node with up to 100,000 historical pageviews in the 30-day window sees rendered numbers within 5 seconds (degraded budget for high-volume sites; future slices may add pre-computation if this becomes the common case).
- **SC-003**: 100% of published content nodes — across every document type configured in a deploying intranet — expose the Analytics tab without per-doctype opt-in.
- **SC-004**: Anonymisation of a visitor's profile preserves their contribution to historical unique-visitor counts; the test suite confirms the count is identical before and after anonymising any subset of the visitors.
- **SC-005**: Zero personally-identifying fields appear in the management endpoint's response payload in this slice's MVP shape; an automated test asserts the JSON schema does not contain any of the reserved identity field names.
- **SC-006**: When a deploying organisation later wires the "Analytics.IndividualData" user group and adds the first per-visitor drill-down (a separate slice), zero changes to this slice's data-shape will be required — the role gate is forward-compatible.

## Assumptions

- **Validation surface is automated tests, not manual quickstart**: Manual quickstart against a live browser is blocked by slice-007-followup #34 (no EntraID claims shim for local dev) and the unrelated #33 (content-save scope race). The canonical validation surface for this slice is the same as slice 007: server-side unit tests, Vitest jsdom tests for the content-app element, and integration tests using the existing `EndToEndCaptureTests` harness pattern (Testcontainers + WebApplicationFactory + faked EntraID claims).
- **Capture tables already populated**: Slices 002 (sessions), 004 (custom events), 005 (forms), 006 (scroll), and the Customizer-owned `customizerVisitorPageview` already populate the rows this slice aggregates against. No new capture surface ships in slice 008.
- **Aggregation is on-demand per request**: No background pre-computation, materialised view, or cache layer ships in this slice. SC-002's 100k-pageview budget is the soft ceiling beyond which a future slice may introduce a rollup table.
- **"Analytics.IndividualData" user group**: ships as a documented convention with no members configured by default. Deploying organisations create the group and assign membership; the slice's tests verify the gate's behaviour with and without membership but do not ship a seed migration creating the group.
- **No client-side time formatting**: Numbers come back from the endpoint as integers and seconds; the client renders them as-is with simple formatting (thousands separator, "Xm Ys" for seconds). Localised time-zone display is out of scope.
- **Top-referrers list is a placeholder**: Returned as an empty array in every response. A future slice (likely the click-through attribution slice that lights up `FR-SRC-03` from slice 007's deferred scope) will populate it; the field exists in the response shape now so its addition is a non-breaking change.
- **Tombstoned-content UI affordance**: The response signals the tombstoned state, but the exact banner copy in the content-app shell is the UI designer's call during implementation. Tests verify the field's truthfulness, not the banner copy.
- **No webhook delivery**: Per slice 007's contract D7 framing, report-snapshot webhook firing is out of scope for the reporting slice family. Future slices may add it if a real consumer asks.
- **Per-visitor drill-down**: Out of scope for MVP. The role gate ships as plumbing only; no UI surface, no endpoint variant returning per-visitor rows.

## Dependencies and References

- **Contract D9** (`docs/INTER-PRODUCT-CONTRACT.md`) — binds Analyzer to ship the per-content-node Analytics content app surface.
- **`FR-RPT-*` and `NFR-SEC-*`** prefixes from `Analytics_Intranet_Requirements.md` are the parity reference (parity items intentionally dropped per CLAUDE.md still apply).
- **Slices 002-007** populate the read-side data this slice surfaces: `customizerVisitorPageview` (pageviews + visitors), `analyzerSession` (sessions + duration), `analyzerScrollSample` (depth — placeholder for heatmap), `analyzerCustomEvent` / `analyzerFormEvent` / `analyzerFormFieldEvent` / `analyzerSearchEvent` (not consumed in this slice but available for future slices in the same family).
- **`slice-007-followups`** issues #28, #29, #33, #34 are all open in the project board and block manual validation but not automated validation.
