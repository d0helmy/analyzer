# Contract — `AnalyzerCustomEventController`

**Feature**: `004-custom-events`
**Date**: 2026-05-18
**Stability**: internal class; **external contract is the HTTP endpoint** at `POST management/api/v1/analyzer/custom-event` (route prefix per spec Assumption — placeholder; may be pinned in slice 005).

The first Analyzer-owned management endpoint. Sets the precedent for slice 005's content app + slice 010+ reports endpoints.

## HTTP contract

### Route

```
POST {analyzer-prefix}/custom-event
```

Slice 004 placeholder: `management/api/v1/analyzer/custom-event`. Slice 005 may pin a canonical Analyzer namespace under the management-API root (NOT `/umbraco/engage/...` per Tech Stack constraint).

### Request

**Method**: `POST`

**Auth**: Umbraco backoffice session cookie (authenticated EntraID identity). Anti-forgery token in standard cookie + header pair (per Umbraco 17 conventions).

**Body**:

```json
{
  "category": "engagement",
  "action": "click",
  "label": "header-cta",
  "value": 42.5
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `category` | string | yes | 1..64 chars; non-whitespace-only |
| `action` | string | yes | 1..64 chars; non-whitespace-only |
| `label` | string | no | <= 256 chars when present |
| `value` | number | no | finite (NaN/Infinity rejected); decimal(18,4) precision |

### Responses

| Status | Body | When |
|---|---|---|
| `202 Accepted` | `{ "eventKey": "<guid>" }` | Happy path. Row persisted; state-store updated; audit emitted. |
| `400 Bad Request` | RFC 7807 ProblemDetails with `errors` naming the offending field(s) | Payload validation failed (FR-007). |
| `400 Bad Request` (or `403`) | Default Umbraco anti-forgery error response | Anti-forgery token missing/invalid. |
| `401 Unauthorized` | Default Umbraco auth response | Anonymous (no authenticated session). |

## Operation (normative)

```
1. [Framework] Authentication filter rejects anonymous → 401
2. [Framework] Anti-forgery filter rejects missing/invalid token → 400/403
3. [Framework] Model binding deserialises CustomEventPayload
4. [Framework] DataAnnotations validation → 400 ProblemDetails on failure
5. [Action body] Additional manual validation:
   - Category.Trim() non-empty
   - Action.Trim() non-empty
   - Value precision check (if needed beyond default JSON deserialiser)
   → 400 ProblemDetails on failure
6. [Action body] Resolve actor identity via IVisitorIdentifier.GetCurrent()
   → 401 if VisitorProfileKey is Guid.Empty (defensive — should not happen post-auth)
7. [Action body] Read UA from HttpContext.Request.Headers.UserAgent
   (controller runs on request thread; HttpContext IS reliable here —
   distinct from slice-002 handler timing)
8. [Action body] Build CustomEventCapture command
9. [Action body] Delegate to CustomEventCaptureHandler.HandleAsync(command)
   - Handler: resolver.ResolveAsync(visitor, UA, now, CustomEvent, ct)
   - Handler: repository.TouchAsync(sessionKey, now, ct)
   - Handler: repository.InsertAsync(customEventDto, ct)
   - Handler: stateStore.AppendCustomEvent(projection)
   - Handler: auditor.Audit(actor, eventKey, category, action, now)
10. [Action body] Return 202 with { eventKey }
```

## Determinism / idempotence

- Two POSTs with identical bodies from the same authenticated visitor produce two distinct `analyzerCustomEvent` rows (no idempotency token mechanism). This is by design — `analyzer.send(...)` is a fire-and-recorded API; the operator's page script controls dedup if needed.
- Cross-instance: identical bodies from two different Umbraco hosts behind a load balancer produce two rows with different `eventKey`s. No coordination needed; events are append-only.

## Thread safety

- Each POST runs on its own ASP.NET request thread; the controller's per-request resolver + repository + state-store calls do not share state across requests.
- The slice-003 resolver's cache + DB-level partial unique index handle the rare concurrent-session-open race; custom-event capture inherits.

## Behaviour-compatible custom implementations

This is a controller; third parties don't typically replace it. If they did need to (e.g., a custom analytics dispatch shim), the binding is the route + payload schema, not the C# class. Replacement controllers MUST preserve the HTTP contract above.

## Tests proving conformance

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `AnalyzerCustomEventControllerTests.Anonymous_returns_401` | Controller invoked without an authenticated principal → 401. | US3 AS1 |
| `AnalyzerCustomEventControllerTests.EmptyCategory_returns_400` | Payload with empty category → 400 with field name in problem-details errors. | US3 AS2 |
| `AnalyzerCustomEventControllerTests.OversizedLabel_returns_400` | Payload with label.Length > 256 → 400. | US3 AS2 |
| `AnalyzerCustomEventControllerTests.HappyPath_returns_202_with_eventKey` | Well-formed payload → 202 with non-empty eventKey in response body. | US1 AS1 |
| Integration: `EndToEndCaptureTests.SinglePost_persists_row_and_updates_state_store` | POST → row in DB + projection in CurrentRequestCustomEvents + audit-log entry. | US1 AS1, AS4 |
| Integration: `ValidationAndAuditTests.AnonymousPost_no_row_no_audit` | Anonymous POST returns 401; zero rows added; zero audit entries. | US3 AS1, SC-005, SC-007 |

## Versioning

The HTTP contract is **public, pinned at the route + payload + response shape level**. Breaking changes (route rename, payload schema change, response shape change) require a versioned endpoint (e.g., `/v2/custom-event`) per the Reporting & Open Surface constraint in the constitution. Slice 005's potential route-prefix rename is the only pre-stabilisation churn allowed; the JS-side client wrapper centralises the URL so any rename is a single-line client change.
