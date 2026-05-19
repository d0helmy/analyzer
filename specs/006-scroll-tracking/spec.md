# Feature Specification: Scroll-Tracking Capture

**Feature Branch**: `006-scroll-tracking`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Scroll-tracking capture slice. Capture scroll depth interactions per pageview on the authenticated intranet, mirroring the slice-005 forms shape: client-side capture handler + management API endpoint + new `analyzerScrollSample` table + cascade-step registration. Targets `FR-COL-*` / scroll-related capture requirements in the Analytics reference doc. Visitor identity via the EntraID `oid` (fallback `upn`) projected through `customizerVisitorProfile` per the inter-product contract. Respect the `analyzer-no-tracking` opt-out attribute introduced in slice 005. No Customizer-side change anticipated."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scroll-depth milestones captured per pageview (Priority: P1)

While an authenticated employee browses an intranet content node, the
front-end records each scroll-depth milestone (25 / 50 / 75 / 100 %) they
cross during that pageview. Each crossing produces one row in the
`analyzerScrollSample` table keyed to the visitor, the pageview, and the
content node, so that the eventual per-content-node scroll heatmap can
report "what proportion of visitors reached this depth on this page".

**Why this priority**: This is the MVP backbone of the slice — without it
there is no scroll data to drive `FR-HMP-01`. The opt-out path (US2) is
meaningless until capture exists.

**Independent Test**: Load an intranet page, scroll to 60 %, navigate
away, then query `analyzerScrollSample` filtered to that pageview — two
rows (bucket=25, bucket=50) must exist with correct
`visitorProfileKey`, `pageviewKey`, `contentKey`, `receivedUtc`.

**Acceptance Scenarios**:

1. **Given** an authenticated employee on a page taller than the
   viewport, **When** they scroll smoothly from top to bottom,
   **Then** exactly four rows are persisted for that pageview
   (`bucket` ∈ {25, 50, 75, 100}), each tagged with the visitor's
   `customizerVisitorProfile.key` resolved from EntraID `oid` (or
   `upn` fallback).
2. **Given** an authenticated employee on a page, **When** they scroll
   to 60 % and then scroll back to the top and back down to 60 %
   again, **Then** the second crossing produces no new rows for
   buckets 25 or 50 — only one row per `(pageviewKey, bucket)` tuple.
3. **Given** an anonymous or unauthenticated request (no resolvable
   visitor identity), **When** a scroll-milestone POST arrives at the
   management endpoint, **Then** the server responds 401/403 and
   persists zero rows.
4. **Given** the visitor anonymisation orchestrator runs for a visitor,
   **When** the scroll cascade step executes, **Then** every
   `analyzerScrollSample` row carrying that visitor's
   `visitorProfileKey` is hard-deleted inside the outer
   anonymisation scope (atomic rollback parity with slice 005).

---

### User Story 2 - Opt-out via `analyzer-no-tracking` attribute (Priority: P2)

A content editor (or platform owner) marks a sensitive content node
with the `analyzer-no-tracking` HTML attribute introduced in slice 005
(applied to `<html>`, `<body>`, or the document's scroll root). On
those pages the front-end scroll capture handler short-circuits
entirely: no scroll-position listener installed, no debouncing, no
POST requests issued, no rows persisted — independent of how the
visitor interacts with the page.

**Why this priority**: Privacy/compliance opt-out parity with the
forms slice. Lower priority than US1 because capture must exist first
to be opted out of, but still ship-required for the slice.

**Independent Test**: Render an intranet content node with
`<body analyzer-no-tracking>`, scroll through all four milestones,
then assert: (a) browser DevTools network panel shows zero requests
to `/scroll-event/milestone`; (b) `analyzerScrollSample` contains
zero rows for that pageview.

**Acceptance Scenarios**:

1. **Given** a page rendered with `analyzer-no-tracking` on `<body>`,
   **When** the authenticated visitor scrolls top-to-bottom, **Then**
   zero scroll-milestone POSTs are issued and zero rows are persisted.
2. **Given** a page where the attribute is added *dynamically* after
   page-load (edge case), **When** the visitor scrolls past further
   milestones, **Then** capture continues as if not opted out — v1
   reads the attribute at handler init only and does not re-check
   on each scroll fire (documented assumption).
3. **Given** the attribute is absent, **When** the visitor scrolls,
   **Then** all four milestones capture normally (parity with US1
   acceptance scenario 1).

---

### Edge Cases

- **Pageview unresolvable at capture time** — race between page-load
  and the first scroll event (`IAnalyticsStateProvider.CurrentRequest`
  not yet populated). Client silently drops the event; server rejects
  with logged warning if the POST still lands.
- **Page shorter than the viewport** (nothing to scroll) — the front-end
  detects zero scrollable distance and emits the 100 % milestone once
  on page-ready, leaving buckets 25/50/75 unrecorded for that pageview.
  This keeps the heatmap denominator equal to pageviews.
- **Mobile overscroll / pull-to-refresh past 100 %** — no additional
  event; idempotency from FR-003 holds.
- **Hidden tab / background pageview** — no scroll fires; zero rows;
  not an anomaly.
- **Visitor anonymised while pageview is open** — cascade step
  hard-deletes existing rows; subsequent in-flight milestone POSTs
  must not create new rows referencing the anonymised visitor (the
  identity gate at the management endpoint catches this because the
  anonymised visitor profile is purged).
- **Concurrent milestone POSTs for the same `(pageviewKey, bucket)`** —
  the unique index forces one to fail; server returns 409 and logs
  without raising client-visible noise.
- **Form submission flow on the same page** — slice-005 form events
  and slice-006 scroll events coexist without coupling; both produce
  independent rows tagged to the same pageview.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The client-side capture handler MUST auto-attach on every
  authenticated pageview of a tracked content node, without requiring
  per-page opt-in markup.
- **FR-002**: The capture handler MUST emit one depth event each time
  the visitor crosses one of the four milestone buckets — 25 %, 50 %,
  75 %, 100 % — measured against the scrollable document height.
- **FR-003**: A given milestone bucket MUST fire at most once per
  pageview. Re-scrolling through a previously-crossed bucket does not
  re-emit. This is enforced both client-side (per-bucket flag) and
  server-side (unique index on `(pageviewKey, bucket)`).
- **FR-004**: The capture handler MUST debounce rapid scroll-position
  changes so that no more than one POST is issued per milestone
  crossing per 100 ms scroll window.
- **FR-005**: When the `analyzer-no-tracking` attribute is present at
  handler-init time on `<html>`, `<body>`, or the document scroll root,
  the handler MUST short-circuit: no scroll listener installed, zero
  POSTs issued.
- **FR-006**: The system MUST expose a management API endpoint at
  `POST /umbraco/management/api/v1/analyzer/scroll-event/milestone`
  that accepts scroll-event payloads behind the standard four-corner
  gate (authentication + anti-forgery + payload validation + audit
  log entry per accepted request).
- **FR-007**: The system MUST persist one row per accepted milestone
  crossing in the `analyzerScrollSample` table, capturing
  `visitorProfileKey`, `sessionKey` (nullable), `pageviewKey`,
  `contentKey`, `bucket`, `receivedUtc`. A unique database index on
  `(pageviewKey, bucket)` MUST enforce the per-pageview-per-bucket
  uniqueness invariant.
- **FR-008**: The identity gate MUST resolve the visitor via
  `IVisitorIdentifier` using EntraID `oid` (with `upn` fallback for
  hosts whose external-login provider omits `oid`). Requests where
  `IsAvailable=false` or the resolved key is `Guid.Empty` MUST be
  rejected with 401/403 and zero rows persisted.
- **FR-009**: An `IAnonymizationCascadeStep` registration MUST hard-delete
  every `analyzerScrollSample` row carrying the anonymised visitor's
  `visitorProfileKey`, executing inside the outer NPoco scope so the
  delete rolls back atomically with the other cascade steps.
- **FR-010**: `IAnalyticsEventStateProvider` MUST expose a
  `CurrentRequestScrollEvents` read-only surface that yields the
  scroll-milestone events accepted during the current request, parity
  with the slice-005 `CurrentRequestFormEvents` / `CurrentRequestFormFieldEvents`
  surfaces.

### Key Entities

- **`analyzerScrollSample`** — one row per accepted milestone crossing.
  Columns: `id` (primary key, uniqueidentifier), `eventKey`
  (uniqueidentifier, externally visible event ID), `visitorProfileKey`
  (uniqueidentifier, FK to `customizerVisitorProfile.key`),
  `sessionKey` (uniqueidentifier, nullable), `pageviewKey`
  (uniqueidentifier, FK to `customizerPageview.Key`), `contentKey`
  (uniqueidentifier, the Umbraco node), `bucket` (tinyint, encoded
  depth-percentile enum — 25 / 50 / 75 / 100), `receivedUtc`
  (datetimeoffset(7)). Unique index on `(pageviewKey, bucket)`
  enforces FR-003 server-side; supporting index on
  `(visitorProfileKey)` powers the cascade-step delete.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Milestone-event capture latency — 99 % of accepted
  milestone crossings are persisted within 1 second of the
  client-side fire, measured under a sustained load of 200
  scroll-events/minute.
- **SC-002**: Per-pageview idempotency — across 1 000 simulated
  pageviews with multiple back-and-forth scroll sequences, no
  `(pageviewKey, bucket)` tuple ever has more than one row, verified
  by post-hoc `GROUP BY ... HAVING COUNT(*) > 1` returning zero.
- **SC-003**: Opt-out compliance — 100 page-loads of a content node
  marked with `analyzer-no-tracking`, each scrolling top-to-bottom,
  produce zero scroll-milestone POSTs and zero rows persisted.
- **SC-004**: Cascade hard-delete latency — anonymising a visitor with
  1 000 scroll-milestone rows completes the cascade step in under
  200 ms, via the indexed `visitorProfileKey` predicate.
- **SC-005**: Identity gate — 100 % of POSTs originating from
  unauthenticated requests or requests where the resolved visitor key
  is `Guid.Empty` receive 401/403 and persist zero rows.
- **SC-006**: Client overhead — the scroll listener adds no more than
  5 ms to First Contentful Paint on a 5 000-pixel-tall content node
  compared with the slice-005 baseline (the continuous scroll
  listener is heavier than slice-005's `IntersectionObserver`-based
  form-impression observer, so the budget is explicitly larger).
- **SC-007**: Audit-log fidelity — every successful capture produces
  exactly one structured log entry carrying `EventKey`, `PageviewKey`,
  `Bucket`, `ActorUpn`, `ReceivedUtc`, verified by grepping the
  structured log against the row count.

## Assumptions

- The pageview row has already been written by Customizer's pageview
  middleware before the first scroll milestone fires, so
  `pageviewKey` is resolvable via
  `IAnalyticsStateProvider.CurrentRequest`.
- The session is already open from slice 003; scroll-milestone
  acceptance advances `lastActivityUtc` via the same path slice-005
  uses.
- Scroll measurement is **document-level** for v1 — `window.scrollY`
  against `document.documentElement.scrollHeight - innerHeight`.
  Element-internal scroll containers (e.g. an `overflow-y: scroll`
  panel inside the page) are explicitly out of scope.
- Single-page-application route changes that re-render the content
  area without issuing a fresh pageview are out of scope for v1,
  matching slice-005's "Forms via Ajax" exclusion.
- Heatmap aggregation, the per-content-node Analytics content app
  visualisation, and any read-side reporting against
  `analyzerScrollSample` are out of scope for this slice — this is a
  capture-side-only slice that produces the row shape the eventual
  read-side will consume (`FR-HMP-01` / `FR-RPT-03` deferred).
- The opt-out attribute is read at handler-init time only; attributes
  added dynamically after handler attachment do NOT retroactively
  stop in-flight capture (edge case documented).
- A new `IAnonymizationCascadeStep` registration is added to the
  existing Customizer-side orchestrator; no Customizer source change
  is required — the orchestrator already discovers registered steps
  through DI.
- Retention follows precedent — rows are purged exclusively on
  visitor anonymisation (FR-009); there is no time-based retention
  sweep in this slice.
- Milestone bucket strategy is **fixed quartiles** (25 / 50 / 75 /
  100 %), matching industry-standard engagement-analytics
  convention. The reference doc says "successive depths" without
  pinning a granularity; quartiles balance heatmap signal against
  POST volume.
- **Short-page handling**: when the page is shorter than the viewport
  (no scrollable distance), the handler emits the 100 % milestone
  once on page-ready and emits no rows for buckets 25 / 50 / 75. This
  keeps the heatmap denominator consistent across page lengths.

## Clarifications resolved

Two scope-significant decisions the reference doc left open were
resolved inline by the spec author (see Assumptions for the rationale):

- **Bucket strategy**: fixed quartiles (25 / 50 / 75 / 100), one
  `tinyint` enum value per bucket. Configurable thresholds and
  continuous sampling are out of scope for v1.
- **Short-page behaviour**: emit 100 % once on page-ready when there
  is no scrollable distance; do not emit lower buckets. Matches the
  "denominator equals pageviews" expectation of the eventual
  heatmap.

A third candidate clarification — whether scroll events FK to
`pageviewKey` or `sessionKey` — is resolved by inter-product contract
§3 D9, which prescribes `pageviewKey`; no marker needed.
