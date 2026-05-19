# Feature Specification: Forms Tracking

**Feature Branch**: `005-forms-tracking`

**Created**: 2026-05-19

**Status**: Draft

**Input**: User description: "forms tracking — FR-FRM-01..05 from Analytics_Intranet_Requirements.md §3.4. Per-form analytics (impressions, starts, successful submissions, abandonment rate, time-to-start, time-to-submit) and field-level analytics (focus/unfocus events, whether each field contained data at unfocus). Auto-enabled for all Umbraco Forms on the intranet when the analyzer client bundle is loaded; opt-out via an `analyzer-no-tracking` attribute (renamed from the Engage `umbraco-engage-no-tracking` precedent so it lives in Analyzer's namespace). Submitted form entries in Umbraco Forms must be linkable to the corresponding `customizerVisitorProfile.key` via a Visitor ID field type — whether Customizer already provides one, or whether Analyzer needs to add it, is a key question to surface in the spec. No goals / no UTM / no anonymous bucket (Product framing in CLAUDE.md). Capture-only slice — surfacing belongs in a later content-app / reporting slice."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Per-form lifecycle events captured automatically (Priority: P1)

When an authenticated intranet employee loads any page containing an Umbraco Form on the intranet, Analyzer's client bundle automatically attaches lifecycle observers to that form. The system records an **impression** when the form first becomes visible to the visitor, a **start** when the visitor first focuses any field, a **success** when the visitor submits the form successfully, and an **abandon** when the visitor's session expires without a successful submission after a start. Each event is attached to the visitor's current session and to the content node the form was rendered on, with the elapsed-time-from-page-load (impressions → start) and the elapsed-time-from-start (start → success) carried on the relevant events.

**Why this priority**: This is the MVP — without per-form lifecycle events, no aggregate metric in FR-FRM-02 (impressions, starts, successful submissions, abandonment rate, time-to-start, time-to-submit) can be computed downstream. Field-level analytics (US2) and opt-out (US3) are refinements that depend on the lifecycle backbone existing first.

**Independent Test**: Load a page that contains an Umbraco Form as an authenticated employee, focus a field, submit successfully, and query the persistence store. Exactly one impression row, one start row, and one success row exist for that `(visitorKey, formKey, sessionKey)` tuple, in receivedUtc order. Time-to-start (start.elapsedMs − impression.elapsedMs) and time-to-submit (success.elapsedMs − start.elapsedMs) are within the test-harness measurement tolerance of the observed wall-clock durations.

**Acceptance Scenarios**:

1. **Given** a content page with a single Umbraco Form rendered to an authenticated employee, **When** the page loads, **Then** an `Impression` event row exists for that `(visitorKey, formKey, contentKey, sessionKey)` with `receivedUtc` ≈ page-load time.
2. **Given** an impression has been recorded, **When** the visitor focuses any field on the form for the first time within the same session, **Then** a `Start` event row exists with `elapsedMsFromImpression` populated and `receivedUtc` after the impression's.
3. **Given** a start has been recorded, **When** the visitor submits the form and Umbraco Forms reports a successful submission, **Then** a `Success` event row exists with `elapsedMsFromStart` populated.
4. **Given** a start was recorded but no submission occurs, **When** the visitor's session reaches its inactivity timeout, **Then** an `Abandon` event row is materialised at logical session-close time for that `(visitorKey, formKey, sessionKey)`.
5. **Given** the same visitor loads the same page twice in the same session, **When** each page-load completes, **Then** two distinct `Impression` rows exist (one per page-load), each with its own elapsed-time origin.

---

### User Story 2 - Field-level focus / unfocus events with value-presence flag (Priority: P2)

When a visitor focuses and then unfocuses (blurs) a field on a tracked form, Analyzer records the focus event, the unfocus event, and a **boolean** `hadValue` flag captured at unfocus time. The flag indicates whether the field contained any input at unfocus — it MUST NOT capture the field's actual value. This lets later reports identify which fields are causing abandonment by being focused-then-blurred-empty repeatedly.

**Why this priority**: Field-level analytics are explicitly listed in FR-FRM-03 and are the key differentiator over generic event tracking. They depend on US1's session + form identity backbone existing, so they layer on after the lifecycle backbone is in place.

**Independent Test**: Focus a field, type nothing, blur it. A `FieldUnfocus` event row exists with `hadValue = false`. Focus the same field again, type a character, blur. A second `FieldUnfocus` row exists with `hadValue = true`. In neither row is the field's actual value present anywhere in the persistence store.

**Acceptance Scenarios**:

1. **Given** a tracked form, **When** the visitor focuses field A, **Then** a `FieldFocus` event row exists with `(formKey, fieldKey)` and `receivedUtc` ≈ focus time.
2. **Given** a focused field A with no input, **When** the visitor unfocuses it, **Then** a `FieldUnfocus` event row exists with `hadValue = false`.
3. **Given** a focused field A with one or more characters typed, **When** the visitor unfocuses it, **Then** a `FieldUnfocus` event row exists with `hadValue = true` and the persisted row does NOT contain the typed characters.
4. **Given** field A is focused, **When** field B receives focus (blurring A), **Then** one `FieldUnfocus` for A and one `FieldFocus` for B are recorded in receivedUtc order.

---

### User Story 3 - Opt-out via `analyzer-no-tracking` attribute (Priority: P3)

When a form or an individual field carries the HTML attribute `analyzer-no-tracking`, Analyzer's client bundle MUST emit zero events for that form (if on the `<form>` element) or zero field events for that field (if on the field element). This is the mechanism for excluding sensitive forms (HR grievances, medical disclosures, whistleblowing) without removing them from Umbraco Forms.

**Why this priority**: Opt-out is a privacy / compliance requirement (FR-FRM-04) but does not block per-form analytics from being useful for the 99% of forms that DO want tracking. Layered on after US1/US2 because it requires those code paths to exist before it can short-circuit them.

**Independent Test**: Render a form with `analyzer-no-tracking` on the `<form>` element. Load the page, focus a field, submit. Zero `analyzerFormEvent` rows exist for any of impression/start/success/abandon. Repeat with `analyzer-no-tracking` on a single field within an otherwise-tracked form — the form-level events still exist, but no `FieldFocus` / `FieldUnfocus` rows exist for that specific field.

**Acceptance Scenarios**:

1. **Given** a form with `analyzer-no-tracking` on the `<form>` element, **When** the page is rendered and the visitor interacts with the form, **Then** zero form-level or field-level event rows exist for that form.
2. **Given** a tracked form with `analyzer-no-tracking` on a single field, **When** the visitor focuses and submits, **Then** form-level events exist, but no field events for the opted-out field exist.
3. **Given** an opt-out attribute is present, **When** the network panel is inspected, **Then** no Analyzer event POST is issued for that form's interactions (defence in depth — opt-out must be client-side, not server-side suppression).

---

### User Story 4 - Submitted Umbraco Forms entries link to the visitor profile (Priority: P3)

Each submitted Umbraco Forms entry persisted in Umbraco Forms' own storage MUST carry the submitter's `customizerVisitorProfile.key` (visitor key) in a field that downstream reports can join on. This lets editors viewing a form's entries cross-reference each entry against the analytics events for that visitor.

**Why this priority**: FR-FRM-05 is parity-required and the cross-product mechanism for joining "what was submitted" (Umbraco Forms data) with "who submitted it and how" (Analyzer events). It is P3 rather than P1 because the per-form aggregate analytics in US1 are useful on their own without the per-entry visitor join. The implementation hinges on **Q1** in the clarifications below.

**Independent Test**: Render a form, submit it as a known authenticated employee, inspect the Umbraco Forms entry record. The visitor key field is populated with that employee's `customizerVisitorProfile.key` and exactly matches the value in the corresponding Analyzer `Success` event row.

**Acceptance Scenarios**:

1. **Given** a form with a Visitor ID field type, **When** a known employee submits, **Then** the Umbraco Forms entry record's Visitor ID field contains that employee's `customizerVisitorProfile.key`.
2. **Given** an anonymisation cascade runs for a visitor, **When** the cascade completes, **Then** subsequent reads of that visitor's submitted Umbraco Forms entries see the visitor key field overwritten or null per the cross-product anonymisation contract.

---

### Edge Cases

- **Visitor identity unavailable at capture time** — Per Product framing, this cannot happen on a properly-configured intranet. Defence in depth: if `IVisitorIdentifier` returns `IsAvailable=false`, the client bundle MUST silently drop the event and the server endpoint MUST reject the POST with a logged warning (no row written).
- **Form with zero fields** — Impressions are recorded; `Start`, `Success`, `Abandon`, and field events are unreachable. No special handling required; absence is correct.
- **Form dynamically inserted into the DOM after initial page load (SPA / AJAX)** — Out of scope for v1. Tracking only applies to forms present at `DOMContentLoaded`. Documented assumption.
- **Multiple instances of the same form on a single page** — Each renders one impression. If two impressions land in the same session for the same `(formKey, contentKey)`, that is correct and reports must handle it.
- **Form submission rejected by Umbraco Forms server validation** — A 4xx response does NOT count as a `Success`. The event remains in `Start` lifecycle state. If the visitor abandons after the rejection, the eventual `Abandon` materialises at session-close.
- **Form submission succeeds but the analytics POST fails** — Analytics events are best-effort (consistent with slice 002's back-pressure-drop posture). The submission still completes; the `Success` event is lost. Documented as accepted loss in line with SC-002 receipt-drop tolerance.
- **Field unfocus fires twice in rapid succession (browser quirk)** — Idempotency: the second event row is allowed; reports MUST tolerate duplicates by deduping on `(visitorKey, formKey, fieldKey, eventType, receivedUtc)` if needed.
- **Visitor anonymisation while session has open form lifecycles** — The cascade step hard-deletes the visitor's form-event rows. Any pending `Abandon` materialisation that would have run at session-close MUST NOT create new rows referencing the anonymised visitor.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** *(Auto-attachment, parity FR-FRM-01)*: When the Analyzer client bundle is loaded on a page and an Umbraco Form is rendered, the client MUST automatically attach lifecycle observers to that form. No per-page or per-form opt-in configuration is required.
- **FR-002** *(Per-form lifecycle events, parity FR-FRM-02)*: The system MUST record four lifecycle event types per form: `Impression`, `Start`, `Success`, and `Abandon`. Each row MUST carry `visitorProfileKey`, `sessionKey`, `formKey`, `contentKey`, `eventType`, and `receivedUtc`.
- **FR-003** *(Timing slots)*: `Start` event rows MUST carry an integer `elapsedMsFromImpression` slot (milliseconds since the matching impression). `Success` event rows MUST carry an integer `elapsedMsFromStart` slot. `Abandon` event rows MUST carry an integer `elapsedMsFromStart` slot equal to `(session.endUtc − start.receivedUtc)` at the moment the abandon row is materialised.
- **FR-004** *(Abandonment materialisation)*: The system MUST materialise one `Abandon` row per `(visitorKey, formKey, sessionKey)` tuple that has a `Start` row but no `Success` row when the session is closed (via slice-003's `AnalyzerSessionSweeperService` logical-close-time). Re-opens (new session) start a fresh form lifecycle.
- **FR-005** *(Field-level events, parity FR-FRM-03)*: The system MUST record `FieldFocus` and `FieldUnfocus` event types per field interaction. `FieldUnfocus` rows MUST carry a `hadValue` boolean indicating whether the field contained any input at unfocus.
- **FR-006** *(Privacy — no field values, parity FR-FRM-04 spirit)*: The system MUST NOT capture, transmit, or persist the actual values entered into fields. The persistence layer MUST have no column intended to hold field content. `hadValue` is the only payload property derived from field content.
- **FR-007** *(Opt-out attribute on form, parity FR-FRM-04)*: When the `<form>` element has the HTML attribute `analyzer-no-tracking` (with any value or empty), the client bundle MUST skip all lifecycle observer attachment and emit zero events for that form. The opt-out check MUST happen client-side before any POST is issued.
- **FR-008** *(Opt-out attribute on field, parity FR-FRM-04)*: When a field element has the HTML attribute `analyzer-no-tracking`, the client bundle MUST emit zero `FieldFocus` / `FieldUnfocus` events for that field. Form-level events remain captured.
- **FR-009** *(Auth + audit, Principle VII)*: The management endpoint that accepts form events MUST enforce the same four-corner gate as slice 004's custom-event endpoint: backoffice auth, anti-forgery, payload validation, and audit-log entry on every successful capture. Anonymous POSTs MUST receive 401 / 403 and persist zero rows.
- **FR-010** *(Cascade hard-delete on anonymisation)*: The system MUST register an `IAnonymizationCascadeStep` that hard-deletes all `analyzerFormEvent` rows for the anonymised `visitorProfileKey` inside Customizer's outer scope. The DELETE MUST participate in the ambient outer NPoco scope (atomic rollback if a later step throws), matching slice 002's receipt and slice 004's custom-event cascade semantics.
- **FR-011** *(State provider exposure)*: `IAnalyticsEventStateProvider` MUST gain an additive `CurrentRequestFormEvents` member returning the `IReadOnlyList` of form events captured during the in-flight request. Empty list, never null.
- **FR-012** *(Visitor ID field-type linkage, parity FR-FRM-05)*: The system MUST link each submitted Umbraco Forms entry to the submitter's `customizerVisitorProfile.key` via a Visitor ID field type **owned by Analyzer**. Analyzer's composer MUST register an Umbraco Forms field type that, at submit time, reads the current visitor's identity through `IVisitorIdentifier` (oid-first, upn-fallback) and writes the resolved `customizerVisitorProfile.key` (Guid) into the entry's Visitor ID column. The field type MUST be selectable in the Umbraco Forms designer and reject configurations that try to bind it to a free-text input from the front-end (it is a server-resolved read-only field). No Customizer-side change is required.
- **FR-013** *(Persistence shape)*: The system MUST persist form events in **two separate tables**: `analyzerFormEvent` for the four lifecycle event types (`Impression`, `Start`, `Success`, `Abandon`) and `analyzerFormFieldEvent` for the two field-level event types (`FieldFocus`, `FieldUnfocus`). This avoids nullable `fieldKey` / `hadValue` columns on the lifecycle rows and isolates the higher-cardinality field-event volume on its own index family. Both tables MUST support efficient per-form aggregate queries (impressions, starts, successes, abandonment rate, average time-to-start, average time-to-submit on `analyzerFormEvent`) AND per-field aggregate queries (per-field focus count, unfocus-empty rate on `analyzerFormFieldEvent`). Each table carries its own cascade-step registration (two `IAnonymizationCascadeStep` instances under FR-010).
- **FR-014** *(Identity gate)*: The capture handler MUST require an authenticated visitor identity (oid-first, upn-fallback) and MUST reject events whose `Actor.IsAvailable=false` or whose visitor key is `Guid.Empty`. Mirrors slice 004's identity gate.

### Key Entities *(include if feature involves data)*

- **`analyzerFormEvent`** — One row per `(visitorKey, formKey, contentKey, sessionKey, eventType ∈ {Impression, Start, Success, Abandon}, receivedUtc)`. Carries optional `elapsedMsFromImpression` (Start only) and `elapsedMsFromStart` (Success / Abandon). Hard FK to `customizerVisitorProfile(key)`; soft FK to `analyzerSession(sessionKey)`; `contentKey` is non-FK (matches receipt + custom event precedent for tombstoned content).
- **`analyzerFormFieldEvent`** — One row per `(visitorKey, formKey, fieldKey, sessionKey, eventType ∈ {FieldFocus, FieldUnfocus}, receivedUtc)`. `FieldUnfocus` rows additionally carry `hadValue` boolean. Hard FK to `customizerVisitorProfile(key)`; soft FK to `analyzerSession(sessionKey)`. Separate table from `analyzerFormEvent` to avoid nullable columns and isolate field-event index family.
- **Analyzer Visitor ID field type** — Umbraco Forms field type registered by Analyzer's composer that resolves the current visitor's `customizerVisitorProfile.key` at submit time (via `IVisitorIdentifier`) and writes it into the Umbraco Forms entry. Server-resolved + read-only from the front-end.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** *(Lifecycle event latency)*: At a sustained capture rate of 100 form interactions per minute across the host (≈ slice-004's per-handler envelope), 99% of lifecycle event rows MUST be persisted within 1 s of the originating client interaction.
- **SC-002** *(Abandonment materialisation)*: At session-close (slice-003 sweeper), 100% of `(visitorKey, formKey, sessionKey)` tuples with a `Start` row and no `Success` row MUST receive exactly one `Abandon` row within the same sweeper pass (no double-materialisation on subsequent passes).
- **SC-003** *(Privacy)*: Zero field values appear anywhere in Analyzer's persistence store, logs, or audit entries during a 1-hour authenticated synthetic load (verified by post-hoc grep + DB column-shape audit). The `hadValue` boolean is the only payload property derived from field content.
- **SC-004** *(Cascade hard-delete latency)*: Anonymising a visitor with 1 000 form-event rows MUST complete the hard-delete within 200 ms via the indexed `visitorProfileKey` predicate, mirroring slice 004's SC-004.
- **SC-005** *(Opt-out compliance)*: For 100 page-loads of a form carrying `analyzer-no-tracking`, zero rows of any kind MUST be persisted for that form and zero POSTs MUST be issued by the client bundle (validated via network-trace fixture in the integration suite).
- **SC-006** *(Audit-log fidelity, Principle VII)*: For every successful form-event capture, exactly one structured audit-log entry MUST be emitted carrying `eventKey`, `formKey`, `eventType`, `actorUpn`, `receivedUtc`. No audit entries MUST be emitted for rejected (401 / 403 / 400) requests.
- **SC-007** *(Identity gate)*: Anonymous or `Actor.IsAvailable=false` POSTs MUST receive a 401 or 403 and persist zero rows; this rate MUST be 100% across 100 synthetic anonymous attempts.
- **SC-008** *(Client overhead)*: The client bundle's form-tracking instrumentation MUST add ≤ 10 ms to the synthetic first-contentful-paint metric on a page containing 5 forms (measured against the slice-004 baseline page bundle).

## Assumptions

- **Umbraco Forms package is installed** on the host. The slice has no fallback for hosts without Umbraco Forms; FR-FRM-* are explicitly Umbraco-Forms-scoped per the reference spec.
- **Forms have stable Umbraco-assigned `Guid` identifiers** that survive form-definition edits. The `formKey` event-row column references this Guid.
- **Field identifiers within a form are stable** — Umbraco Forms exposes a per-field Guid. The `fieldKey` event-row column references this Guid.
- **Capture is exclusively client-side** — the client bundle observes DOM events (focus, blur, submit) and POSTs to a management endpoint. Server-side `OnSubmit` hooks into Umbraco Forms are out of scope for v1 except for the Visitor ID field-type write in FR-FRM-05 (subject to Q1).
- **Session is already open** from slice 003 — the first `Impression` event of a session is preceded by a pageview that opened or extended the session. `Impression` events do NOT advance `lastActivityUtc`; subsequent `Start` / `Success` / field events DO (via slice 003's `TouchAsync`, consistent with slice 004's custom-event behaviour). `pageviewCount` is never incremented by form events.
- **Opt-out attribute name** — `analyzer-no-tracking`. Lives in Analyzer's namespace per the Product framing. The Engage `umbraco-engage-no-tracking` precedent is explicitly NOT adopted (we do not depend on Engage; the attribute would be misleadingly named).
- **Forms dynamically inserted into the DOM after `DOMContentLoaded`** are out of scope for v1. Hosts wanting dynamic-form coverage can re-trigger the analyzer attach in a follow-up slice if needed.
- **Per-field analytics emit one POST per focus/unfocus event** (no client-side batching in v1). Burst-protection (rate-limit + drop) follows slice 002's back-pressure-drop posture — accepted loss at high event rates.
- **Cross-product dependency on Customizer** — visitor identity continues to flow via `IVisitorIdentifier` (slice 002 onward). No new Customizer surface is required by this slice (Q1 resolved to Analyzer-owned). Out-of-scope items §6.2 / §2.4 (no UTM, no anonymous bucket, no geo, no consent banner) continue to apply: form events MUST NOT carry UTM, geo, or anonymous-bucket dimensions.
- **New package dependency** — Umbraco.Forms (managed package; needs central-package-management entry in `src/Analyzer/Directory.Packages.props`). The Analyzer.Host sample MUST also reference it so the integration tests can render real Umbraco Forms.
- **`analyzerFormEvent` retention** follows existing precedent — purged on visitor anonymisation (FR-010), no time-based retention sweep beyond what Customizer's anonymisation orchestrator emits.

## Clarifications resolved

- **Q1 → Analyzer owns the Visitor ID field type** (FR-012). No Customizer-side change required. Adds an Umbraco.Forms package reference + an Analyzer-composer-registered field type that resolves the visitor key server-side at submit time.
- **Q2 → Two separate tables** (FR-013). `analyzerFormEvent` for lifecycle events + `analyzerFormFieldEvent` for field events. Two cascade-step registrations.
