# Phase 0 Research: Forms Tracking

**Slice**: 005-forms-tracking
**Date**: 2026-05-19
**Status**: Complete — all `[NEEDS CLARIFICATION]` items resolved in spec (Q1 + Q2); this document records the supporting design decisions

## R1 — Umbraco.Forms 17.x integration pattern

**Decision**: Pin Umbraco.Forms to `[17.0.0,18.0.0)` (CMS-major-aligned) via central package management. Register the Visitor ID field type by deriving from `Umbraco.Forms.Core.Providers.FieldTypes.FieldType` and letting Umbraco's auto-discovery pick it up at host boot. Hook server-side submission via `INotificationHandler<FormSubmittingNotification>` to write the resolved visitor key into the entry before persistence.

**Rationale**:
- `FieldType` is the documented base class for custom Forms field providers; the package supports unlimited custom field types via this surface (no Umbraco.Forms-side change required).
- `FormSubmittingNotification` fires after client-side validation passes and before Forms persists the entry. The notification handler can mutate `RecordField.Values` for the Visitor ID field before the entry is saved.
- `FormSubmittingNotification` is the safer hook than `FormValidateNotification` (which precedes any record assembly) and `FormSubmittedNotification` (which fires after save — too late to populate the field).
- Auto-discovery means no `IComposer.UpdateCollections` call is needed; the `FieldType` subclass is registered just by being public + derived. This matches Umbraco's "convention over configuration" stance.

**Alternatives considered**:
- **Manual registration via `IComposer`** — explicit but redundant with auto-discovery; introduces a registration step that's easy to forget.
- **`FormValidateNotification`** — fires too early (before `RecordField` instances are built); would require a parallel write pathway.
- **`FormSubmittedNotification`** — fires after persistence; mutating values here is a no-op for the entry.

## R2 — Form + field identifier resolution from the DOM

**Decision**: Umbraco Forms 17.x renders forms with a stable `data-umbraco-form` attribute carrying the form's `Guid` (page-instance), and individual fields render with a `data-umbraco-form-field` attribute (or the wrapping `<div>` carries the field Guid). The client bundle reads these attributes from the DOM to populate `formKey` / `fieldKey` in the event payload.

**Rationale**:
- The Forms-rendered HTML is the public boundary between Umbraco Forms' server template and the client-side analytics observer. Reading documented `data-*` attributes is the cleanest contract.
- No JS-side dependency on Umbraco Forms internals — only on the rendered HTML shape.
- Forms 17.x's default renderer adds these attributes; a host with a heavily-customised renderer would need to preserve them. Documented as an assumption in `quickstart.md`.

**Alternatives considered**:
- **Read form/field names from the `name` attribute** — names are stable per form but not unique across forms (two forms can both have a "name" field). Guids are unique.
- **Hash the DOM structure** — fragile under template edits; useless if a form moves between pages.
- **Server round-trip** — query the server for the form's Guid given the DOM element. Adds latency to capture; defeats the fire-and-forget posture.

## R3 — Client-side observation strategy

**Decision**: Forms tracking attaches at `DOMContentLoaded`, iterates `document.querySelectorAll('form[data-umbraco-form]')`, and per form:
- **Impression**: fires immediately if the form is in the viewport at attach time, OR via `IntersectionObserver` (threshold 0.1) when it scrolls into view. One impression per page-load per form instance.
- **Start**: first `focus` event on any tracked field (event capture phase, single dispatch via a flag on the form's tracker state).
- **Success**: `submit` event observed AND the form's eventual response is 2xx (the client awaits the form's `fetch`/native-submit cycle to confirm before dispatching; if the form submits via native form-post and reloads the page, the `Success` is dispatched on the form's beforeunload IF a synchronous flag is set).
- **Abandon**: NOT materialised client-side. Server-side, slice-003's `AnalyzerSessionSweeperService` queries form-events at session-close (one query per closed session, indexed) and emits one `Abandon` row per `(visitorKey, formKey, sessionKey)` tuple with `Start` but no `Success`.
- **FieldFocus** / **FieldUnfocus**: `focus` and `blur` listeners attached per field, capture phase; the blur listener reads `element.value.length > 0` to populate `hadValue`.

**Rationale**:
- Capture phase ensures we observe events before user-code stops propagation (defensive).
- `IntersectionObserver` is the modern impressions-when-visible primitive; fallback to "in viewport at attach time" handles the case where the observer takes a tick to fire.
- Pushing `Abandon` to the server-side sweeper avoids the client needing to detect "user gave up" (impossible reliably).
- One POST per event matches slice 004's `analyzer.send()` pattern — no client-side batching in v1 (Assumption in spec).

**Alternatives considered**:
- **Single ServerSentEvents stream** for all form events — more efficient at high volume but adds a long-lived connection per visitor; not justified for v1's intranet scale.
- **MutationObserver for dynamically-inserted forms** — out of scope for v1 per spec; reserved for a follow-up if hosts need SPA support.
- **`beforeunload`-based abandonment** — unreliable; `beforeunload` is gated by browser heuristics and can't deliver a guaranteed-POST in many configurations.

## R4 — Two-table persistence shape (confirms Q2 = B)

**Decision**: `analyzerFormEvent` (lifecycle) and `analyzerFormFieldEvent` (field events) as two separate tables.

**Rationale**:
- **No nullable columns**: lifecycle table has `elapsedMsFromImpression` (nullable, Start only) and `elapsedMsFromStart` (nullable, Success/Abandon only); field table has `hadValue` (nullable, Unfocus only). Keeping them separate puts each nullable column in the table where it makes sense, instead of forcing rows of one event family to carry the other's columns.
- **Index family isolation**: lifecycle queries are per `(visitorKey, formKey, sessionKey)`; field queries are per `(formKey, fieldKey)`. Distinct primary access patterns map to distinct index choices.
- **Cardinality**: field events are estimated 5-15× the volume of lifecycle events (every focus/blur per field per visitor). Isolating them keeps `analyzerFormEvent` small and fast for the reporting queries that drive most dashboards.

**Indexes** (locked in `data-model.md`):
- `analyzerFormEvent`:
  - PK: `id` (Guid, non-autoincrement)
  - UX: `eventKey` (Guid)
  - IX: `(visitorProfileKey, formKey, sessionKey, eventType)` — supports per-visitor lifecycle queries + cascade DELETE
  - IX: `formKey` — supports per-form aggregate queries
  - IX: `receivedUtc` — supports time-range scans for reports
- `analyzerFormFieldEvent`:
  - PK: `id` (Guid, non-autoincrement)
  - UX: `eventKey` (Guid)
  - IX: `(visitorProfileKey, formKey, sessionKey)` — supports per-visitor cascade DELETE + per-form-per-session aggregates
  - IX: `(formKey, fieldKey, eventType)` — supports per-field aggregate queries
  - IX: `receivedUtc`

**Alternatives considered**: combined table with nullable cols (Q2 option A). Rejected: simpler superficially but mixes two access patterns onto one index family.

## R5 — Abandonment materialisation hook

**Decision**: Extend `AnalyzerSessionSweeperService` (slice 003) with a post-close hook: after a batch of sessions is closed, query `analyzerFormEvent` for `(sessionKey IN @closedSessions, eventType = 'Start')` rows that have no corresponding `Success` row, and INSERT `Abandon` rows in the same NPoco scope.

**Rationale**:
- Slice 003's sweeper already runs on a bounded background queue with batched closes (`SweepBatchSize` configurable). Plugging Forms-side abandonment into the same pass amortises the cost.
- Same outer NPoco scope means rollback safety: if the sweeper's overall scope fails, the abandon INSERTs roll back together with the session-close UPDATEs.
- One query per batch (not per session), using a LEFT JOIN or NOT EXISTS predicate on `eventType IN ('Start', 'Success')`, scales linearly with batch size.

**Alternatives considered**:
- **Separate `AnalyzerFormAbandonmentSweeper` BackgroundService** — additional cron-style daemon. Splits the work across two services that both need to coordinate with session-close timing.
- **Materialise at session-close-write time** — would require slice-003's `AnalyzerSessionRepository.CloseAsync` to know about form events, breaking layering (Sessions feature reaching into Forms feature).
- **Lazy materialisation at read time** — defer abandon detection to reporting queries. Cheap at write, expensive at read; defeats the "abandonment rate" reporting requirement at scale.

## R6 — Audit-log shape

**Decision**: Mirror slice 004's `CustomEventAuditor`: emit one structured log entry per successful form-event capture using `ILogger<AnalyzerFormEventAuditor>` with a fixed log scope carrying `EventKey`, `FormKey`, `EventType`, `ActorUpn`, `ReceivedUtc`. Field events get a parallel auditor with the same shape plus `FieldKey` + `HadValue`.

**Rationale**:
- Same observability story as slice 004; same log-aggregator queries work without change.
- Structured logging via `ILogger.LogInformation` lets the host's log pipeline (Serilog, Application Insights, etc.) consume without bespoke wiring.
- No new audit-log persistence table — slice 004 already established that `ILogger` is the audit substrate.

**Alternatives considered**:
- **Dedicated `analyzerAuditLog` table** — heavier; would need to be back-filled to slices 002/004 for consistency. Out of slice scope.
- **No audit log** — fails Principle VII (state-changing capture must be auditable).

## R7 — Public-surface pinning diff

**Decision**: Slice 005 adds the following to `Analyzer.Tests.PublicSurface.Baselines.Analyzer-public-surface.txt`:

```
TYPE Analyzer.Analytics.AnalyticsFormEvent : record
  CTOR(System.Guid EventKey, System.Guid VisitorProfileKey, System.Guid? SessionKey, System.Guid FormKey, System.Guid ContentKey, Analyzer.Analytics.AnalyzerFormEventType EventType, System.Nullable<System.Int32> ElapsedMsFromImpression, System.Nullable<System.Int32> ElapsedMsFromStart, System.DateTimeOffset ReceivedUtc)
  PROP System.Guid EventKey { get; init; }
  …

TYPE Analyzer.Analytics.AnalyticsFormFieldEvent : record
  CTOR(System.Guid EventKey, System.Guid VisitorProfileKey, System.Guid? SessionKey, System.Guid FormKey, System.Guid FieldKey, Analyzer.Analytics.AnalyzerFormFieldEventType EventType, System.Nullable<System.Boolean> HadValue, System.DateTimeOffset ReceivedUtc)
  …

TYPE Analyzer.Analytics.AnalyzerFormEventType : enum
  Impression
  Start
  Success
  Abandon

TYPE Analyzer.Analytics.AnalyzerFormFieldEventType : enum
  FieldFocus
  FieldUnfocus

ADDED Analyzer.Analytics.IAnalyticsEventStateProvider:
  PROP System.Collections.Generic.IReadOnlyList<Analyzer.Analytics.AnalyticsFormEvent> CurrentRequestFormEvents { get; }
  PROP System.Collections.Generic.IReadOnlyList<Analyzer.Analytics.AnalyticsFormFieldEvent> CurrentRequestFormFieldEvents { get; }
```

All additive per Principle X. Baseline regen task in Polish phase (slice-004 envelope: T048-T050 equivalents).

**Rationale**:
- New public records use init-only props per Principle X + slice 004 precedent.
- Enums in the `Analyzer.Analytics` namespace mirror the slice 003 `AnalyticsSession` placement.
- `IAnalyticsEventStateProvider` additions are MINOR-additive — preserve binary compat with slice 004 consumers.

**Alternatives considered**: keep enums internal + use `string` columns in the public records. Rejected — string-typed event types are weaker contracts and harder for third-party `IAnalyticsEventStateProvider` consumers to switch on.

## R8 — Cascade-step participation pattern

**Decision**: Both `AnalyzerFormEventCascadeStep` and `AnalyzerFormFieldEventCascadeStep` use **hard-delete** (Principle IV's established pattern, matching `AnalyzerEventReceiptCascadeStep` + `AnalyzerCustomEventCascadeStep`).

**Rationale**:
- Form events have no aggregate-preservation requirement (unlike sessions, which preserve aggregates via soft-anonymise to keep slice-003's reports correct after a visitor's identity is erased). Per-form reports compute over `formKey` directly, not via visitor identity, so erasing the rows is correct.
- Same NPoco outer-scope semantics as receipts + custom events — atomic rollback if a later cascade step throws.
- Two separate cascade-step registrations means each table's DELETE is small + parallelisable in theory (though the cascade orchestrator runs them sequentially today).

**Alternatives considered**:
- **Soft-anonymise** (clear `visitorProfileKey` to `Guid.Empty`, set `anonymizedUtc`) — preserves per-form aggregates after anonymisation, but `customizerVisitorProfile.key` is already preserved post-anonymisation by Customizer (only `IdentityRef` flips to `anonymized:…`). So the analyser-side aggregates already retain the right denominator without our soft-anonymise.
- **Single combined cascade step** that deletes both tables — works but couples the two tables' cleanup; if we ever decided to switch one table's pattern, the combined step would be a refactor.

## R9 — Endpoint route, auth, and payload

**Decision**: Endpoint `POST /umbraco/management/api/v1/analyzer/form-event` (mirrors slice 004's `/umbraco/management/api/v1/analyzer/custom-event`). Two payload shapes:
- `AnalyzerFormEventPayload` — `{ formKey, contentKey, eventType, elapsedMsFromImpression?, elapsedMsFromStart? }`
- `AnalyzerFormFieldEventPayload` — `{ formKey, fieldKey, eventType, hadValue? }`

The endpoint dispatches based on the discriminator in the route (use two route subpaths `…/lifecycle` and `…/field` for clean validation) OR via a polymorphic payload (oneOf in OpenAPI). Sub-path option preferred for OpenAPI clarity.

Decision: **two route subpaths**:
- `POST /umbraco/management/api/v1/analyzer/form-event/lifecycle`
- `POST /umbraco/management/api/v1/analyzer/form-event/field`

**Rationale**:
- Clean OpenAPI: each route documents its own payload shape; no oneOf/discriminator complexity.
- Simpler controller methods; simpler validators.
- Principle-VII four-corner gate applied identically to both routes.

**Alternatives considered**: single route + polymorphic payload (rejected — added complexity at the schema level). Single route with eventType discriminator + a single payload covering both (rejected — nullable column proliferation, same anti-pattern as the rejected one-table data model in R4).

## R10 — Identity claim ordering inside the field type

**Decision**: `AnalyzerVisitorIdField` resolves identity via `IVisitorIdentifier.IdentifyAsync(HttpContext)` (oid-first, upn-fallback per Principle I). Falls back to `Guid.Empty` if `IsAvailable=false` (which on the intranet means a configuration error in EntraID projection — log warning + emit `Guid.Empty` so the Forms entry shows an obvious gap rather than silently bypassing).

**Rationale**:
- Reuses Analyzer's existing `IVisitorIdentifier` (slice 002) — single source of truth for identity claims.
- Logging `IsAvailable=false` as a warning + writing `Guid.Empty` makes misconfigurations visible without preventing the submission (the user pressed Submit; their form entry should still save).

**Alternatives considered**:
- **Throw on `IsAvailable=false`** — would reject the form submission entirely. Wrong: the visitor's submission deserves to land even if our identity capture failed (the form is the user's primary task, not analytics).
- **Resolve identity from the auth cookie directly** — bypasses the shared `IVisitorIdentifier` contract; would drift from slice 002's resolution semantics.

---

## Summary

All Phase 0 decisions documented. Zero `[NEEDS CLARIFICATION]` markers remain. Ready for Phase 1 (data-model + contracts + quickstart).
