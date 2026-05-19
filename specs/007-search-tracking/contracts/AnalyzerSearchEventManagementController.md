# Contract — `AnalyzerSearchEventManagementController`

**Feature**: `007-search-tracking`
**Date**: 2026-05-19
**Stability**: management API — versioned + documented via OpenAPI per Constitution's Reporting & Open Surface section.

The single Umbraco backoffice management controller that accepts search-event POSTs from the client bundle. Mirrors slice-004's `AnalyzerCustomEventManagementController`, slice-005's `AnalyzerFormEventManagementController`, and slice-006's `AnalyzerScrollEventManagementController` four-corner Principle-VII gate.

## Route

| Route | Method | Body | Returns |
|-------|--------|------|---------|
| `/umbraco/management/api/v1/analyzer/search-event` | POST | `AnalyzerSearchEventPayload` | `202 { eventKey }` on success |

One route is sufficient — there is no second event type to split out (in contrast to slice 005 which had both `/lifecycle` and `/field`). Unlike slice 006, the route does not include a `/milestone` segment — search submissions are not bucket-discrete.

## Payload

```csharp
public sealed class AnalyzerSearchEventPayload
{
    public Guid PageviewKey { get; set; }
    public string Query { get; set; } = string.Empty;
    public int ResultCount { get; set; }
}
```

Serialised as JSON (System.Text.Json, Umbraco-default). The controller does NOT accept a client-supplied `ContentKey` — that field is server-set from `customizerPageview.contentKey` of the validated `PageviewKey` (defends against a client forging arbitrary content-key correlations).

**Field discipline**: the payload schema MUST NOT contain any property whose name suggests result snippets, result URLs, click positions, or anything beyond the {query, count, pageview-correlation} signal. Validator runs a name-pattern reject pass on the request body (defence in depth — model binder strips unknown properties, validator confirms).

## Principle-VII gates

1. **Backoffice auth**: `[Authorize(Policy = "BackOffice")]`; anonymous POST → 401. The current Umbraco backoffice cookie identifies the EntraID-projected visitor; the controller passes the cookie context to `IVisitorIdentifier.Resolve()`.
2. **Anti-forgery**: enforced by Umbraco's management-API pipeline by convention; missing anti-forgery → 403, no row written, no audit emitted.
3. **Payload validation**: model-binding + handler-level validation per Spec FR-008:
   - `Query` non-empty after trim; length 1-256 post-trim.
   - Normalised form of `Query` non-empty (defence against a custom normaliser collapsing input to nothing).
   - `ResultCount >= 0` and `<= 1_000_000` (sanity cap).
   - `PageviewKey != Guid.Empty` AND `PageviewKey` belongs to the resolved visitor (one indexed lookup against `customizerPageview`). Failures → 400, no row written, no audit emitted.
4. **Audit**: emitted only on successful persistence (handler invokes auditor after repository insert). **Neither `rawQuery` nor `normalisedQuery` appears in the audit-log entry** — search queries are PII per FR-SRC-04 (Spec FR-009 + SC-006).

## Status code matrix

| Outcome | Status | Body | Side effects |
|---------|--------|------|--------------|
| Successful capture | 202 | `{ "eventKey": "<guid>" }` | One row in `analyzerSearchEvent`. One audit-log entry `AnalyzerSearchEventCaptured` (no query in fields). `lastActivityUtc` advanced for the session via `TouchAsync`. |
| Anonymous request | 401 | empty | None. |
| Identity unavailable / `Guid.Empty` | 403 | empty | None. |
| Payload validation failure (empty query / oversize / negative count / pageviewKey mismatch / pageviewKey not-belongs-to-visitor) | 400 | `{ "errors": [...] }` (Umbraco's standard problem-details shape) | None. |
| Anti-forgery failure | 403 | empty | None. |
| Unexpected exception | 500 | Umbraco's standard problem-details shape | Exception structured-logged. No row inserted. |

**No 409 path**: search events have no DB-level uniqueness invariant. Concurrent same-query submissions land as separate rows by design (R7, Spec Edge Case "Concurrent same-query submissions").

## OpenAPI

The controller participates in Umbraco's management API OpenAPI generation. The published schema MUST include:

- Request schema: `AnalyzerSearchEventPayload`.
- Response schemas: `{ eventKey: string<uuid> }` for 202; standard problem-details for 4xx/5xx.
- Description string referencing slice 007 (FR-SRC-01, FR-SRC-02, FR-SRC-04) and a pointer to the read-side search-report doc once that ships.
- **A `x-pii-fields` extension marker** on the `query` request property — surfaces to API consumers that the field is treated as PII server-side. The marker is informational; the controller's actual PII handling does not depend on it. (Convention introduced this slice; matches the kind of out-of-band metadata Customizer's outbox webhook signing-key field uses.)

## Conformance tests

Integration tests (under `src/Analyzer.Tests/Integration/Search/`) MUST cover:

- **`EndToEndCaptureTests`**: authenticated POST with valid payload → 202, row exists, state provider reflects the row, audit entry emitted **without `rawQuery` or `normalisedQuery` in the structured log fields**.
- **`NormalisationAggregationTests`** (SC-007): seed 3 000 variants of 1 000 queries; group by `normalisedQuery`; assert 1 000 distinct groups.
- **`OptOutComplianceTests`**: client-side opt-out is exercised via the Vitest module test (`send-search.test.ts`); server-side OptOut compliance is verified by absence-of-POST monitoring — there is no server-side opt-out path to test.
- **`CascadeHardDeleteTests`**: insert 1 000 rows for a visitor, run the cascade step, verify all 1 000 are removed; assert latency < 200 ms (SC-004).
- **`CascadeRollbackTests`**: simulate a downstream cascade-step throw → outer scope rolls back; search rows remain present.
- **`PageviewVisitorBindingTests`**: POST with `PageviewKey` belonging to a different visitor → 400; zero rows written. POST with non-existent `PageviewKey` → 400.

The HTTP boundary integration tests remain gated on issue #23 (mgmt-API 404 in test host) — the same gap slices 004/005/006 left. Per slice 006's Phase-5 polish entry, they're tracked as deferred items in `tasks.md` rather than blocking the slice.
