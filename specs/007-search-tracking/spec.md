# Feature Specification: Internal Search-Tracking Capture

**Feature Branch**: `007-search-tracking`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "Search-tracking capture slice. Capture internal intranet search submissions on the authenticated intranet, mirroring the slice-004 / slice-006 capture shape: client-side helper API + management API endpoint + new `analyzerSearchEvent` table + cascade-step registration + a public `IAnalyzerSearchQueryNormaliser` extension point. Targets `FR-SRC-*` in the Analytics reference doc. Visitor identity via the EntraID `oid` (fallback `upn`) projected through `customizerVisitorProfile` per the inter-product contract. Respect the `analyzer-no-tracking` opt-out attribute introduced in slice 005. No Customizer-side change anticipated."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Search submissions captured with normalised query + result count (Priority: P1)

When an authenticated employee submits a search query through the
intranet's search bar, the host page invokes a new client helper
(`analyzer.sendSearch(query, resultCount)`) once the result count is
known. The helper posts the query, the result count, and the current
`pageviewKey` to Analyzer's management endpoint. The endpoint
resolves the visitor + active session, computes a normalised form of
the query via the new `IAnalyzerSearchQueryNormaliser` extension
point, and writes one row in the new `analyzerSearchEvent` table
keyed to the visitor, the session, the pageview, and the content
node — so that the eventual Events / search reports can answer
"most common queries", "queries returning no results" (FR-SRC-02),
and "queries per page".

**Why this priority**: This is the MVP backbone of the slice — no
search capture means none of `FR-SRC-01`/`FR-SRC-02`/`FR-SRC-03`
is satisfied. The opt-out path (US2) is meaningless until capture
exists.

**Independent Test**: From a Razor-rendered intranet page that hosts
a search box, run
`await window.analyzer.sendSearch("  Annual Review  ", 7)` in the
browser console. Read `analyzerSearchEvent` afterwards: exactly one
row exists with `rawQuery = "  Annual Review  "`,
`normalisedQuery = "annual review"`, `resultCount = 7`,
`sessionKey` matching the visitor's active session,
`visitorProfileKey` resolved from EntraID `oid` (or `upn` fallback),
`pageviewKey` matching the rendering page, and `receivedUtc` within
1 second of the call.

**Acceptance Scenarios**:

1. **Given** an authenticated EntraID employee with an active in-progress
   session on an intranet page rendered with `pageviewKey` available
   to the client, **When** their page script invokes
   `analyzer.sendSearch("design system", 12)`, **Then** the endpoint
   resolves the visitor's session, inserts exactly one
   `analyzerSearchEvent` row with `rawQuery = "design system"`,
   `normalisedQuery = "design system"`, `resultCount = 12`,
   `sessionKey` matching the visitor's active session, and returns
   HTTP 202 with the new row's `eventKey` in the response body.
2. **Given** the same employee, **When** they submit a query whose
   raw form differs from its canonical form only in case / trailing
   whitespace / Unicode width
   (e.g. `"  Ｄｅｓｉｇｎ  SYSTEM  "`), **Then** the persisted row's
   `normalisedQuery` is `"design system"` — exactly equal to the
   normalised form of acceptance scenario 1's query — so the two
   submissions group together in any subsequent "top queries"
   aggregation keyed on `normalisedQuery`.
3. **Given** the same employee, **When** their search returns no
   results and they invoke `analyzer.sendSearch("xyzzy", 0)`,
   **Then** exactly one row is persisted with `resultCount = 0`,
   and operator reporting derives the "no-results" view by filtering
   `WHERE resultCount = 0` (no separate `NoResults` event is
   emitted — Clarification §1).
4. **Given** an anonymous or unauthenticated request (no resolvable
   visitor identity), **When** a search-event POST arrives at the
   management endpoint, **Then** the server responds 401/403 and
   persists zero rows.
5. **Given** the visitor anonymisation orchestrator runs for a
   visitor with N persisted `analyzerSearchEvent` rows, **When** the
   search cascade step executes, **Then** every
   `analyzerSearchEvent` row carrying that visitor's
   `visitorProfileKey` is hard-deleted inside the outer
   anonymisation scope (atomic rollback parity with slice 004 / 006).
6. **Given** two consecutive `analyzer.sendSearch(...)` calls from
   the same page within the session's inactivity window, **When** the
   second call lands, **Then** both events attribute to the same
   session row, `pageviewCount` on the session DOES NOT increment
   (search events are not pageviews), and `lastActivityUtc` advances
   to the second event's `receivedUtc` via the slice-004
   `TouchAsync` repository path — search events keep the session
   warm, matching the slice-004 / slice-006 precedent.

---

### User Story 2 — Opt-out via `analyzer-no-tracking` attribute (Priority: P2)

A content editor (or platform owner) marks a sensitive content node
hosting a search interface with the `analyzer-no-tracking` HTML
attribute introduced in slice 005 (applied to `<html>`, `<body>`, or
the document's scroll root). On those pages the
`analyzer.sendSearch(...)` helper short-circuits entirely: zero POST
requests issued, zero rows persisted — independent of how the
visitor uses the search box.

**Why this priority**: Privacy/compliance opt-out parity with the
forms (slice 005) and scroll (slice 006) slices. Lower priority
than US1 because capture must exist first to be opted out of, but
still ship-required for the slice given the heightened privacy
posture of search queries (FR-SRC-04 = potential PII).

**Independent Test**: Render an intranet content node with
`<body analyzer-no-tracking>`, invoke
`await window.analyzer.sendSearch("anything", 3)` from the console,
then assert: (a) browser DevTools network panel shows zero requests
to `/search-event`; (b) `analyzerSearchEvent` contains zero rows
for that pageview; (c) the Promise resolves to a sentinel value
(`{ skipped: true }`) so callers can branch without fetch errors.

**Acceptance Scenarios**:

1. **Given** a page rendered with `analyzer-no-tracking` on `<body>`,
   **When** the authenticated visitor invokes
   `analyzer.sendSearch(...)` ten times in succession, **Then** zero
   search-event POSTs are issued and zero rows are persisted.
2. **Given** a page where the attribute is added *dynamically* after
   page-load (edge case), **When** the visitor invokes the helper
   after the attribute was added, **Then** the helper short-circuits
   on that call — the opt-out predicate is evaluated **per call**
   (cheap DOM read; differs from slice-006 scroll which reads at
   handler-init only, because here there is no long-lived listener
   to mute).
3. **Given** the attribute is absent, **When** the visitor invokes
   the helper, **Then** the search event captures normally (parity
   with US1 acceptance scenario 1).

---

### Edge Cases

- **Empty / whitespace-only `query`** — client helper rejects locally
  (Promise rejects with `{ status: 400, message: "query required" }`,
  no POST issued); server also validates and returns 400 if the POST
  still lands.
- **Query whose normalised form is empty** (e.g. punctuation-only
  input like `"???"` collapses to `""` after NFKC + lowering +
  whitespace trim) — server rejects with HTTP 400 and a structured
  error naming the offending field; zero rows persisted. The default
  normaliser MUST NOT silently fall back to the raw query.
- **Oversized query** — `rawQuery.Length > 256` rejected with HTTP
  400; client helper does NOT truncate (calling code decides).
- **Negative or non-finite `resultCount`** — rejected with HTTP 400;
  `resultCount` MUST be a non-negative finite integer.
- **Search-as-you-type / autocomplete keystrokes** — each helper
  invocation produces one row; the slice does NOT prescribe
  debouncing. Hosts that wire the helper to keystrokes will see
  high row counts; that is the host's design choice. Recommended
  integration: invoke the helper once per **submitted** query
  (Enter key / search button), not per keystroke. Documented as an
  assumption.
- **`pageviewKey` unresolvable at capture time** — payload
  validation rejects with HTTP 400 if `pageviewKey` is missing,
  empty `Guid`, or not a recognised pageview belonging to the
  current visitor.
- **Visitor anonymised while search request is in flight** — the
  identity gate at the management endpoint catches this because
  the anonymised visitor's profile is purged; the POST is rejected
  401/403 and no row is created.
- **Concurrent same-query submissions** — each is a separate row;
  no uniqueness index on `(pageviewKey, normalisedQuery)` because
  legitimately re-running the same query (e.g. after switching
  filters) IS a distinct engagement signal.
- **Pagination clicks / page-2 navigation** — out of scope; only the
  initial submission with the result count is captured. A
  subsequent page navigation on the results page produces a normal
  pageview row (Customizer-owned), not another search event.
- **Click-through attribution** ("queries followed by no
  click-through" — FR-SRC-03) — out of scope for v1; this is a
  derived read-side metric that joins search events to subsequent
  pageviews and is deferred to the reporting slice that lights up
  the Analytics Events report.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** *(Client helper)*: Analyzer MUST extend the existing
  `window.analyzer` client surface (from slice 004) with a new method
  `sendSearch(query: string, resultCount: number, options?: {
  pageviewKey?: string }): Promise<{ eventKey: string } | { skipped:
  true }>`. The helper MUST source `pageviewKey` from `options`,
  falling back to the standard
  `<meta name="analyzer-pageview-key" content="...">` /
  `window.analyzer.pageviewKey` plumbing established in slice 006.
  The helper MUST include the standard browser-managed authentication
  cookie and the host's anti-forgery token (sourced from the standard
  Umbraco anti-forgery cookie + header pair). On HTTP 202 it resolves
  with `{ eventKey }`; on any other status it rejects with
  `{ status: number, message: string }` (failure logged client-side
  at `console.warn`; not retried).
- **FR-002** *(Opt-out short-circuit)*: When the `analyzer-no-tracking`
  attribute is present at *call time* on `<html>`, `<body>`, or the
  document scroll root, `analyzer.sendSearch(...)` MUST short-circuit:
  no POST issued, no fetch failure surfaced, Promise resolves with
  `{ skipped: true }`. The opt-out predicate is the shared
  `shared/opt-out-attribute.ts` introduced in slice 006.
- **FR-003** *(Management endpoint)*: Analyzer MUST expose a
  backoffice-authenticated POST endpoint at
  `POST /umbraco/management/api/v1/analyzer/search-event` that
  accepts the JSON payload `{ query: string, resultCount: number,
  pageviewKey: Guid }`. The endpoint is gated by (a) authenticated
  EntraID session (anonymous rejected per FR-007), (b) anti-forgery
  token validation (rejected per FR-007), (c) payload validation
  (per FR-008). On success, it (1) resolves the active session via
  `IAnalyzerSessionResolver` (synchronously on the request thread;
  slice-003 contract), (2) normalises the query via
  `IAnalyzerSearchQueryNormaliser` (per FR-005), (3) persists a
  single `analyzerSearchEvent` row, (4) advances the session's
  `lastActivityUtc` via the slice-004 `TouchAsync` repository path
  (no `pageviewCount` increment), (5) updates the request-scoped
  state surface with the in-flight event (per FR-006), (6) emits an
  audit-log entry (per FR-009), (7) returns HTTP 202 with the new
  row's `eventKey` in the response body.
- **FR-004** *(Persistence shape)*: Analyzer MUST introduce the
  `analyzerSearchEvent` table with columns:
  - `id` (PK, Guid, opaque internal)
  - `eventKey` (Guid, externally visible stable identifier — the
    value returned in the response body; unique non-clustered index)
  - `visitorProfileKey` (Guid, hard FK to
    `customizerVisitorProfile.key`)
  - `sessionKey` (Guid, hard FK to `analyzerSession.sessionKey`)
  - `pageviewKey` (Guid, hard FK to `customizerPageview.Key`)
  - `contentKey` (Guid, the Umbraco node hosting the pageview;
    denormalised at write time for fast per-page-of-content lookup)
  - `rawQuery` (NVARCHAR(256), NOT NULL — pre-normalisation, retained
    for forensic display under role-gated access; PII-sensitive per
    FR-SRC-04)
  - `normalisedQuery` (NVARCHAR(256), NOT NULL — output of
    `IAnalyzerSearchQueryNormaliser`; the grouping key for "top
    queries" aggregations)
  - `resultCount` (int, NOT NULL, ≥ 0 — `0` is the
    "no-results" derived view per Clarification §1)
  - `receivedUtc` (DateTimeOffset(7), NOT NULL)
  Indexes:
  - unique non-clustered on `eventKey`
  - non-clustered on `visitorProfileKey` (cascade-step delete
    predicate)
  - non-clustered on `(normalisedQuery)` (top-queries aggregation)
  - non-clustered on `pageviewKey` (per-pageview lookup; supports
    eventual click-through join)
- **FR-005** *(Query normalisation extension point)*: Analyzer MUST
  ship a public extension point
  `IAnalyzerSearchQueryNormaliser { string Normalise(string rawQuery); }`
  plus a default implementation
  `DefaultAnalyzerSearchQueryNormaliser`. The default MUST apply, in
  order: (a) trim leading/trailing whitespace, (b) Unicode
  normalisation form NFKC, (c) `ToLower(CultureInfo.InvariantCulture)`,
  (d) collapse internal whitespace runs to a single space character.
  The default MUST be culture-stable across hosts (no
  `CurrentCulture` dependency). Hosts with multilingual search MAY
  replace the default via a single composer registration (last
  registration wins, per Umbraco DI conventions).
- **FR-006** *(In-request state surface)*: `IAnalyticsEventStateProvider`
  MUST expose a new read-only member `CurrentRequestSearchEvents`
  that yields the search events accepted during the current request,
  parity with the slice-004 `CurrentRequestCustomEvents` /
  slice-005 `CurrentRequestFormEvents` /
  slice-006 `CurrentRequestScrollEvents` surfaces. The backing
  `AnalyticsEventStateStore` gains a parallel `_currentSearchEvents`
  list field + `AppendSearchEvent(AnalyticsSearchEvent)` mutator.
- **FR-007** *(Identity + anti-forgery gates)*: The identity gate
  MUST resolve the visitor via `IVisitorIdentifier` using EntraID
  `oid` (with `upn` fallback for hosts whose external-login provider
  omits `oid`). Requests where `IsAvailable=false` or the resolved
  key is `Guid.Empty` MUST be rejected with 401/403 and zero rows
  persisted. Anti-forgery rejection MUST return HTTP 400 with a
  structured error response.
- **FR-008** *(Payload validation)*: Payload validation MUST reject
  (HTTP 400, structured error naming the offending field):
  - `query` empty or whitespace-only
  - `query.Length > 256`
  - normalised form of `query` empty after the default normaliser
    runs (defensive — FR-005 (d) does not guarantee a non-empty
    output)
  - `resultCount < 0` or non-finite or non-integer
  - `resultCount > 1_000_000` (sanity cap; intranet search corpus is
    small)
  - `pageviewKey == Guid.Empty` or `pageviewKey` not found in
    `customizerPageview` for the resolved visitor (the pageview must
    belong to the same visitor making the search call — defends
    against a misbehaving page script forging arbitrary pageview
    keys).
  Validation runs at the request boundary (controller filter or
  model-state validation per ASP.NET conventions); the domain layer
  MUST NOT trust upstream validation.
- **FR-009** *(Audit log)*: Every successful event-recording action
  MUST emit a structured audit-log entry capturing: actor (`UPN` for
  display + `oid` for canonical key per Principle I), action name
  `"search-event-capture"`, target = the persisted row's `eventKey`,
  `pageviewKey`, `resultCount`, `receivedUtc`. The audit-log entry
  MUST NOT include either `rawQuery` or `normalisedQuery` — search
  queries are PII per FR-SRC-04 and the access-controlled DB row is
  their canonical store; replicating them into the log substrate
  would broaden the privacy surface inappropriately. Failed
  validations DO NOT emit audit entries (they are noise; structured
  logs at warn-level capture them for operator visibility).
- **FR-010** *(Cascade hard-delete on anonymisation)*: Analyzer MUST
  register a new `IAnonymizationCascadeStep` (number seven on the
  stack, after slice-002 receipts, slice-003 sessions, slice-004
  custom events, slice-005's two form-event tables, and slice-006
  scroll samples). The new step MUST execute inside Customizer's
  outer NPoco scope and MUST **hard-delete** every
  `analyzerSearchEvent` row carrying the anonymised visitor's
  `visitorProfileKey`. Hard-delete (not re-key) is the right choice
  because search queries are PII per FR-SRC-04 — right-to-delete
  obligations under CCPA/CPRA cannot be satisfied by re-keying a row
  that still contains the literal personal data of the subject.
  This diverges from the contract D8 row, which prescribes re-key;
  the divergence is intentional and documented in Clarifications §2.
- **FR-011** *(Reference-doc parity)*: Cited reference-doc items:
  `FR-SRC-01` (search query capture as an event with the conceptual
  category `Search`, action `Query`; this slice implements the
  capture via a dedicated table + helper rather than the reference
  doc's "use the custom-events table with literal category/action
  strings" mechanism — Clarification §1), `FR-SRC-02` (zero-result
  queries capturable; this slice implements as `resultCount = 0`
  filter rather than a separate event type), `FR-SRC-04` (queries
  are PII; informs FR-009 audit-log redaction and FR-010 hard-delete
  cascade), `FR-COL-*` (server-side capture continues — inherited
  from slice 002 / 003 with no regression). `FR-SRC-03` ("queries
  followed by no click-through") is **deferred** to the read-side
  reporting slice; only the row shape that enables the eventual
  click-through join is shipped here. None of the dropped prefixes
  (`FR-DEP-*`, `FR-DIM-04`, `FR-DIM-03`, §3.3 bot detection, §6.2
  public-website features) are cited.

### Key Entities

- **`analyzerSearchEvent`** (Analyzer-owned, new this slice) — one
  row per accepted intranet search submission. Carries the operator-
  defined dimensions (`rawQuery`, `normalisedQuery`, `resultCount`),
  the identity correlation (`visitorProfileKey`), the temporal
  correlation (`sessionKey`, `receivedUtc`), and the content-node
  correlation (`pageviewKey`, `contentKey`). Hard FKs to
  `customizerVisitorProfile.key`, `analyzerSession.sessionKey`, and
  `customizerPageview.Key`.
- **`IAnalyzerSearchQueryNormaliser`** (Analyzer-owned, new this
  slice) — public extension point converting a raw user-typed
  search query into a canonical grouping key. Default implementation
  applies trim + NFKC + invariant-culture lower + whitespace-run
  collapse. Replaceable per FR-005.
- **Search-event state surface** (Analyzer-owned, modified this
  slice) — `IAnalyticsEventStateProvider` gains
  `CurrentRequestSearchEvents`. The slice-002 / slice-003 /
  slice-004 / slice-005 / slice-006 surfaces are unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** *(Capture latency)*: 99 % of accepted search-event
  POSTs are persisted within 1 second of the helper call, measured
  under a sustained load of 200 search-events/minute.
- **SC-002** *(Normalisation correctness)*: Given a fixed table of
  100 input/expected-normalised pairs covering trim, case-folding,
  NFKC fullwidth/halfwidth, and internal-whitespace-run collapse,
  the default `IAnalyzerSearchQueryNormaliser` MUST produce the
  expected output for 100 % of entries. The table is checked into
  the slice's tests.
- **SC-003** *(Opt-out compliance)*: 100 invocations of
  `analyzer.sendSearch(...)` on a content node marked with
  `analyzer-no-tracking` produce zero search-event POSTs and zero
  rows persisted; each Promise resolves with `{ skipped: true }`.
- **SC-004** *(Cascade hard-delete latency)*: Anonymising a visitor
  with 1 000 `analyzerSearchEvent` rows completes the cascade step
  within 200 ms via the indexed `visitorProfileKey` predicate,
  mirroring slice-004 / 005 / 006 SC-004.
- **SC-005** *(Identity gate)*: 100 % of POSTs originating from
  unauthenticated requests or requests where the resolved visitor
  key is `Guid.Empty` receive 401/403 and persist zero rows.
- **SC-006** *(Audit-log fidelity + PII redaction)*: Every successful
  capture produces exactly one structured log entry carrying
  `EventKey`, `ActorUpn`, `ActorOid`, `PageviewKey`, `ResultCount`,
  `ReceivedUtc`, and **does NOT** carry `rawQuery` or
  `normalisedQuery`. Verified by grepping the structured log
  against the row count and by asserting absence of any query-string
  fields in the log entry.
- **SC-007** *(Aggregation key stability)*: For a corpus of 1 000
  synthetic search submissions where each query has been written in
  three case/whitespace/Unicode-width variants (3 000 total
  submissions), grouping `analyzerSearchEvent` by `normalisedQuery`
  produces exactly 1 000 distinct groups — i.e. the normaliser
  collapses the variants together with 100 % fidelity. (This is the
  user-facing equivalent of SC-002, measured at the table level.)

## Assumptions

- **The host site is responsible for calling the helper at the right
  moment.** The slice does not prescribe whether to fire on
  Enter-key submit, on search-button click, on stable-result render,
  or on every keystroke. Hosts firing on every keystroke will see
  large row counts; that is the host's design choice. The
  recommended integration is *one helper call per submitted query*
  (Enter / button click), invoked after the result count is known.
- **`pageviewKey` is propagated to the client by the same mechanism
  slice 006 established** — `<meta name="analyzer-pageview-key">`
  or the inline `window.analyzer.pageviewKey` global populated
  during Razor render. No new injection plumbing is added by this
  slice. Headless front-ends already injecting this for slice 006
  reuse it unchanged.
- **The session is already open from slice 003.** Search-event
  acceptance advances `lastActivityUtc` via the same `TouchAsync`
  path slice-004 / slice-005 / slice-006 use — search keeps the
  session warm, parity with custom events.
- **Search events are NOT a `FR-EVT-04` custom event under the
  hood.** Although the reference doc phrases FR-SRC-01 as "raise a
  custom event with category `Search`", we ship a dedicated table.
  Rationale captured in Clarifications §1; the reference-doc parity
  table cites `FR-SRC-01` directly, not `FR-EVT-04`.
- **Pagination of search results, per-result click-through tracking,
  and "queries followed by no click-through" attribution
  (`FR-SRC-03`) are out of scope for v1.** Only the initial query
  submission is captured; click-through attribution becomes a
  derived read-side metric in the eventual Events-report slice that
  joins `analyzerSearchEvent` to subsequent `customizerPageview`
  rows in the same session.
- **The default normaliser is locale-blind**
  (`InvariantCultureIgnoreCase` lowering). Multilingual hosts that
  need locale-aware folding (e.g. Turkish dotted-i, ICU collation)
  replace `IAnalyzerSearchQueryNormaliser` via DI per FR-005. This
  is a deliberate v1 simplification — the contract is the
  extension point, not the algorithm.
- **A new `IAnonymizationCascadeStep` registration is added to the
  existing Customizer-side orchestrator; no Customizer source
  change is required.** The orchestrator already discovers
  registered steps through DI. This will be the seventh registered
  step (matching the slice-006 precedent of the sixth).
- **Retention follows precedent** — rows are purged exclusively on
  visitor anonymisation (FR-010); there is no time-based retention
  sweep in this slice. Operator-side retention configuration per
  `NFR-SEC-06` lives at the Customizer storage layer and applies
  to the whole DB.
- **No new package dependency.** The slice reuses existing Analyzer
  + Customizer infrastructure (NPoco, Umbraco backoffice auth + AF,
  shared client opt-out predicate, session resolver, identifier).
- **No Customizer-side change.** Capture is fully Analyzer-owned;
  the only Customizer-side surface touched is the existing
  anonymisation cascade DI scan.

## Clarifications resolved

Two scope-significant decisions where the reference doc and the
inter-product contract pointed in different directions were
resolved inline by the spec author. Both are captured here so the
plan / tasks / analyse phases can audit the call.

### §1 — Capture mechanism: dedicated table vs. custom-events ride-along

The reference doc (`FR-SRC-01`, `FR-SRC-02`) describes search as a
custom-events convention: "raise a custom event with category
`Search`, action `Query`, label = the search term"; "a separate
event with category `Search`, action `NoResults` shall be raised
whenever a search returns zero results". The inter-product contract
(§3 D8) explicitly enumerates a dedicated `analyzerSearchEvent`
table among the cascade-step-owning tables.

**Resolved**: dedicated `analyzerSearchEvent` table per the
contract, plus a single event per submission carrying a
`resultCount` column (no second `NoResults` event). Rationale:

- A dedicated table cleanly holds `rawQuery` + `normalisedQuery` +
  `resultCount`, all of which want first-class typed columns rather
  than being shoehorned into the custom-events `label` (string) and
  `value` (decimal) shape.
- `FR-SRC-04` flags search queries as PII; isolating them in their
  own table makes retention, access control, and audit-log
  redaction targetable per-table without affecting custom-events.
- The "queries returning no results" report (`FR-SRC-03`) becomes a
  trivial `WHERE resultCount = 0` filter on a single table; emitting
  two events doubles row volume for no extra signal.
- Slice 006 (scroll) set the same precedent: although scroll could
  conceptually have ridden on `analyzerCustomEvent`, it ships its
  own `analyzerScrollSample` table for the same reasons.

### §2 — Cascade-step disposition: hard-delete vs. re-key

The inter-product contract (§3 D8) lists `analyzerSearchEvent` with
disposition "re-key to anonymised visitor key". Slice-004
(custom events) and slice-006 (scroll samples) chose hard-delete
instead, citing per-row engagement-signal semantics with no
aggregate-load-bearing role.

**Resolved**: hard-delete, diverging from the contract row.
Rationale:

- `FR-SRC-04` flags search queries as PII; right-to-delete
  obligations under CCPA/CPRA cannot be satisfied by re-keying a
  row that still contains the literal `rawQuery` of the subject —
  the row is still a record of "this person searched for $X" once
  the row key is rebound to the operator's pseudonymous identity.
- Hard-delete matches the slice-004 / slice-006 precedent for
  per-row engagement signals.
- Aggregate "top queries" reporting is unaffected by hard-delete:
  the normalised-query histogram across all visitors is the
  reportable signal; per-subject attribution after anonymisation
  is precisely what right-to-delete forbids.
- The contract authors had access only to the v1 sketch of search
  semantics; FR-SRC-04 + the slice-004/006 precedent post-date the
  contract row.

Contract maintenance follow-up (out of scope for this slice's
implementation, in scope for the slice's commit message / PR
description): the contract D8 table SHOULD be amended after this
slice ships to reflect "hard-delete" for `analyzerSearchEvent`.
