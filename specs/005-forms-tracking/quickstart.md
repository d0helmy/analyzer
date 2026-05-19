# Quickstart: Forms Tracking

**Slice**: 005-forms-tracking
**Audience**: developers verifying slice 005 end-to-end against a running Umbraco host with Umbraco Forms installed and the Analyzer bundle loaded.

This document is the manual-verification companion to `/speckit-implement` — it walks the four user stories from the spec against a real intranet page so the green tests don't paper over an end-to-end gap.

---

## 0. Prerequisites

- An Umbraco 17.3.5 host that project-references Analyzer + Customizer.
- Umbraco Forms 17.x installed and licensed on the host (`Umbraco.Forms` package added to `samples/Analyzer.Host/Analyzer.Host.csproj`).
- The Aspire AppHost is running (`dotnet run --project aspire/Analyzer.AppHost`) so the SQL container is warm and migrations have applied.
- An authenticated EntraID employee account (or a TestEntraIdProvider-stubbed one if the host is mocked).

Sanity check before walking the stories:

```bash
# Schema applied
sqlcmd -Q "SELECT TOP 1 * FROM analyzerFormEvent" -d Analyzer    # empty rowset, no error
sqlcmd -Q "SELECT TOP 1 * FROM analyzerFormFieldEvent" -d Analyzer
```

---

## 1. US1 — Per-form lifecycle events (P1)

**Setup**: create a content page `/forms-demo` containing one Umbraco Form with a single text field. No `analyzer-no-tracking` attribute.

**Steps**:

1. Open the page as an authenticated employee. Open DevTools Network panel.
2. Verify: a POST to `/umbraco/management/api/v1/analyzer/form-event/lifecycle` fires with `eventType=0` (Impression). Response 202 with an `eventKey`.
3. Focus the text field. Verify: a POST to `…/lifecycle` with `eventType=1` (Start) and `elapsedMsFromImpression` populated.
4. Fill the field, submit. Verify: a POST with `eventType=2` (Success) and `elapsedMsFromStart` populated.

**Verify in DB**:

```sql
SELECT eventType, elapsedMsFromImpression, elapsedMsFromStart, receivedUtc
FROM analyzerFormEvent
WHERE visitorProfileKey = @your_visitor_key
ORDER BY receivedUtc;
```

Expected: 3 rows in order `Impression(0), Start(1), Success(2)`. `elapsedMsFromImpression` set on Start; `elapsedMsFromStart` set on Success. Each row's `sessionKey` is the same Guid (single session).

**Abandonment**:

5. Reload the page (new Impression + Start). DO NOT submit.
6. Wait past the slice-003 inactivity timeout (default 30 min, or shorten via `Analyzer:Session:InactivityTimeoutMinutes=1` for testing).
7. Wait one sweeper pass (default 60 s, configurable via `Analyzer:Session:SweepIntervalSeconds=10`).
8. Verify: a row with `eventType=3` (Abandon) exists for that session+form, with `elapsedMsFromStart` ≈ `inactivityTimeoutMinutes * 60_000`.

---

## 2. US2 — Field-level focus / unfocus (P2)

Same form as US1. Reload the page.

1. Click the field. Verify: a POST to `/umbraco/management/api/v1/analyzer/form-event/field` with `eventType=0` (FieldFocus). Body has no `hadValue` slot.
2. Click outside the field WITHOUT typing. Verify: a POST with `eventType=1` (FieldUnfocus) and `hadValue=false`.
3. Click the field again, type "x", click outside. Verify: a FieldFocus + a FieldUnfocus with `hadValue=true`.

**Verify in DB**:

```sql
SELECT eventType, hadValue, receivedUtc
FROM analyzerFormFieldEvent
WHERE visitorProfileKey = @your_visitor_key
ORDER BY receivedUtc;
```

Expected: 4 rows alternating Focus(0) / Unfocus(1, hadValue=0) / Focus(0) / Unfocus(1, hadValue=1). The persisted row has NO column for "x" — only the boolean.

**Privacy spot-check**:

```sql
-- Confirm no column intended to hold field content
EXEC sp_columns 'analyzerFormFieldEvent';
```

Expected: no `value`, `content`, `text`, `payload`, or similar column. Only the listed columns in data-model §1.2.

---

## 3. US3 — Opt-out (`analyzer-no-tracking`)

**Setup**: edit the form's underlying Razor template (or the page that hosts it) and add `analyzer-no-tracking` to the `<form>` element:

```html
<form data-umbraco-form="@Model.Id" analyzer-no-tracking>
  ...
</form>
```

1. Reload `/forms-demo`. Open DevTools.
2. Focus the field, submit. Verify: ZERO POSTs to `/umbraco/management/api/v1/analyzer/form-event/*`.
3. Inspect DB:
   ```sql
   SELECT COUNT(*) FROM analyzerFormEvent WHERE visitorProfileKey = @your_visitor_key AND receivedUtc > @page_load_time;
   SELECT COUNT(*) FROM analyzerFormFieldEvent WHERE visitorProfileKey = @your_visitor_key AND receivedUtc > @page_load_time;
   ```
   Both expected to return 0.

**Per-field opt-out**: revert the form-level attribute; add `analyzer-no-tracking` to a single `<input>`:

```html
<input data-umbraco-form-field="@field.Id" analyzer-no-tracking ... />
```

4. Reload, focus + submit. Verify: form-level events (Impression, Start, Success) ARE recorded; field events for the opted-out field are NOT.

---

## 4. US4 — Visitor ID field type populates entry (P3)

**Setup**: in Umbraco Forms designer, edit the demo form. Drag the "Analyzer Visitor ID" field onto the form. Save.

1. Reload the page as the authenticated employee. Submit the form.
2. In Umbraco Forms backoffice, open the form's entries view.
3. Verify: the submitted entry's "Analyzer Visitor ID" column contains a Guid that exactly matches the visitor's `customizerVisitorProfile.key`:
   ```sql
   SELECT vp.[key]
   FROM customizerVisitorProfile vp
   WHERE vp.identityRef = 'oid:' + LOWER(REPLACE(CAST(@your_oid AS varchar(36)), '-', ''));
   ```
4. Cross-reference with the corresponding `analyzerFormEvent` Success row's `visitorProfileKey` — should be identical Guid.

**Misconfig fallback**: temporarily stub `IVisitorIdentifier.IdentifyAsync` to return `IsAvailable=false`. Submit. Verify: the entry's Visitor ID column holds `00000000-0000-0000-0000-000000000000`; the host log shows one `Warning` from `AnalyzerVisitorIdFieldSubmissionHandler`.

---

## 5. Cascade hard-delete

1. Trigger Customizer's anonymisation operator action on the test visitor (Customizer backoffice → visitor profile → "Anonymise").
2. Verify:
   ```sql
   SELECT COUNT(*) FROM analyzerFormEvent WHERE visitorProfileKey = @your_visitor_key;
   SELECT COUNT(*) FROM analyzerFormFieldEvent WHERE visitorProfileKey = @your_visitor_key;
   ```
   Both expected to return 0.
3. Verify the slice-004 + slice-002 cascade steps also ran (their tables also empty for the visitor).
4. Verify the visitor profile row itself still exists with `identityRef = 'anonymized:…'` (Customizer-side soft-anonymise of identity).

---

## 6. Performance + audit spot-check

- **Audit log**: each successful POST produces one structured log entry with `EventKey`, `FormKey`, `EventType`, `ActorUpn`, `ReceivedUtc`. For field events, additionally `FieldKey` + `HadValue`. Inspect via the host's log sink (Serilog console for dev).
- **Latency**: open DevTools Network, sort by Time, filter to `form-event`. p95 should be well under the 1 s SC-001 budget on a warm system.
- **Bundle overhead**: compare first-contentful-paint between `/forms-demo` (with 1 form) and a non-form content page. Δ should be ≤ 10 ms (SC-008).

---

## 7. What to do if a step fails

- **POST returns 404**: route not registered in test host — see issue #23 (gates the integration suite's HTTP-boundary coverage).
- **POST returns 401 for an authenticated user**: anti-forgery cookie missing. Verify the host emits the backoffice anti-forgery cookie on the auth bootstrap.
- **No Abandon row materialised**: confirm the sweeper is running (slice 003's `AnalyzerSessionSweeperService` log line "sweeper closed N sessions"). Confirm the visitor's session reached the inactivity timeout.
- **Field event has a "value" column**: privacy invariant violated. Open a P0 issue, halt the slice. The schema MUST NOT have such a column (SC-003).
- **Visitor ID field empty after submit**: `IVisitorIdentifier` likely returned `IsAvailable=false`. Check the EntraID external login projection.
