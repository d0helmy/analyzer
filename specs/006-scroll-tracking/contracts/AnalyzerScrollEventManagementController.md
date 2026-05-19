# Contract — `AnalyzerScrollEventManagementController`

**Feature**: `006-scroll-tracking`
**Date**: 2026-05-19
**Stability**: management API — versioned + documented via OpenAPI per Constitution's Reporting & Open Surface section.

The single Umbraco backoffice management controller that accepts scroll-milestone POSTs from the client bundle. Mirrors slice-004's `AnalyzerCustomEventManagementController` and slice-005's `AnalyzerFormEventManagementController` four-corner Principle-VII gate.

## Route

| Route | Method | Body | Returns |
|-------|--------|------|---------|
| `/umbraco/management/api/v1/analyzer/scroll-event/milestone` | POST | `AnalyzerScrollEventPayload` | `202 { eventKey }` on success |

One route is sufficient for v1 — there is no second event type to split out (in contrast to slice 005 which had both `/lifecycle` and `/field`).

## Payload

```csharp
public sealed class AnalyzerScrollEventPayload
{
    public Guid PageviewKey { get; set; }
    public Guid ContentKey { get; set; }
    public AnalyzerScrollBucket Bucket { get; set; }
}
```

Serialised as JSON (System.Text.Json, Umbraco-default). The `Bucket` enum is emitted as its integer underlying value (`25`, `50`, `75`, `100`) — matches the `byte`-backed wire format.

**Field discipline**: the payload schema MUST NOT contain any property whose name suggests page content, raw scroll position, or anything beyond the milestone-crossed signal (`*Position`, `*Pixels`, `*Selector`, etc.). The system captures *crossed-a-bucket*, not *scrolled-to-pixel-X*. Validator runs a name-pattern reject pass on the request body (defence in depth — model binder strips unknown properties, validator confirms).

## Principle-VII gates

1. **Backoffice auth**: `[Authorize(Policy = "BackOffice")]`; anonymous POST → 401. The current Umbraco backoffice cookie identifies the EntraID-projected visitor; the controller passes the cookie context to `IVisitorIdentifier.Resolve()`.
2. **Anti-forgery**: enforced by Umbraco's management-API pipeline by convention; missing anti-forgery → 403, no row written, no audit emitted.
3. **Payload validation**: model-binding + handler-level validation (see `IAnalyzerScrollEventCaptureHandler` contract). Failures → 400, no row written, no audit emitted.
4. **Audit**: emitted only on successful persistence (handler invokes auditor after repository insert).

## Status code matrix

| Outcome | Status | Body | Side effects |
|---------|--------|------|--------------|
| Successful capture | 202 | `{ "eventKey": "<guid>" }` | One row in `analyzerScrollSample`. One audit-log entry `AnalyzerScrollEventCaptured`. `lastActivityUtc` advanced for the session. |
| Anonymous request | 401 | empty | None. |
| Identity unavailable / `Guid.Empty` | 403 | empty | None. |
| Payload validation failure | 400 | `{ "errors": [...] }` (Umbraco's standard problem-details shape) | None. |
| Duplicate `(pageviewKey, bucket)` | 409 | `{ "code": "duplicate" }` | One audit-log entry tagged `Duplicate`. No row inserted (UX rejected). |
| Anti-forgery failure | 403 | empty | None. |
| Unexpected exception | 500 | Umbraco's standard problem-details shape | Exception structured-logged. No row inserted. |

## OpenAPI

The controller participates in Umbraco's management API OpenAPI generation. The published schema MUST include:

- Request schema: `AnalyzerScrollEventPayload`.
- Response schemas: `{ eventKey: string<uuid> }` for 202; standard problem-details for 4xx/5xx; `{ code: 'duplicate' }` for 409.
- Description string referencing slice 006 (FR-COL-02 capture) and a pointer to the read-side heatmap doc once that ships.

## Conformance tests

Integration tests (under `src/Analyzer.Tests/Integration/Scroll/`) MUST cover:

- **`EndToEndCaptureTests`**: authenticated POST with valid payload → 202, row exists, state provider reflects the row, audit entry emitted.
- **`IdempotencyTests`**: two POSTs for the same `(pageviewKey, bucket)` → first 202, second 409; only one row exists; audit shows one success + one `Duplicate`.
- **`OptOutComplianceTests`**: client-side opt-out is exercised via the Vitest module test (`scroll-observer.test.ts`); server-side OptOut compliance is verified by absence-of-POST monitoring — there is no server-side opt-out path to test.
- **`CascadeHardDeleteTests`**: insert 1 000 rows for a visitor, run the cascade step, verify all 1 000 are removed; assert latency < 200 ms (SC-004).
- **`CascadeRollbackTests`**: simulate a downstream cascade-step throw → outer scope rolls back; scroll rows remain present.
