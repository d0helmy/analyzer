# Contract — `AnalyticsSearchEvent`

**Feature**: `007-search-tracking`
**Date**: 2026-05-19
**Stability**: public; new in slice 007. Pinned via the existing `PublicSurfacePinningTests` baseline (regenerated as a Polish-phase task).

Immutable projection of an `analyzerSearchEvent` row. Returned by `IAnalyticsEventStateProvider.CurrentRequestSearchEvents` for in-process consumers within the request scope, and surfaced through the eventual read-side reporting API.

## Namespace

```
Analyzer.Analytics.AnalyticsSearchEvent
```

Lives in the pinned `Analyzer.Analytics` namespace alongside slice-003 `AnalyticsSession`, slice-004 `AnalyticsCustomEvent`, slice-005 `AnalyticsFormEvent` / `AnalyticsFormFieldEvent`, and slice-006 `AnalyticsScrollSample`.

## Shape

```csharp
namespace Analyzer.Analytics;

public sealed record AnalyticsSearchEvent
{
    public required Guid EventKey { get; init; }
    public required Guid VisitorProfileKey { get; init; }
    public required Guid SessionKey { get; init; }
    public required Guid PageviewKey { get; init; }
    public required Guid ContentKey { get; init; }
    public required string RawQuery { get; init; }
    public required string NormalisedQuery { get; init; }
    public required int ResultCount { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
}
```

## Field semantics

| Field | Source | Notes |
|-------|--------|-------|
| `EventKey` | server-generated | Public identity for the captured row; UX-indexed in DB. Returned in the POST 202 response body. |
| `VisitorProfileKey` | `IVisitorIdentifier.Resolve()` | EntraID `oid`-first, `upn`-fallback (FR-007). |
| `SessionKey` | slice-003 `IAnalyzerSessionResolver` | NOT nullable — search-event capture resolves a session synchronously (R7). |
| `PageviewKey` | client → server | Carried in the POST payload; visitor-bound at the controller layer (R3) — the pageview must belong to the resolved visitor. |
| `ContentKey` | server-set | Read from `customizerPageview.contentKey` for the validated `PageviewKey` (denormalised at write time for fast per-page-of-content lookup). |
| `RawQuery` | client → server | Pre-normalisation user-typed string. 1-256 chars after trim. **PII-sensitive per FR-SRC-04** — never logged; read-side surfaces exposing it MUST be role-gated. |
| `NormalisedQuery` | server-computed | Output of `IAnalyzerSearchQueryNormaliser.Normalise(RawQuery)` at capture time. Grouping key for "top queries" aggregations. **Also PII-sensitive per FR-SRC-04**. |
| `ResultCount` | client → server | Non-negative integer; `0` is the "no-results" derived view (Spec Clarifications §1). |
| `ReceivedUtc` | server-set | `DateTimeOffset.UtcNow` at handler entry. |

## PII handling notice

`RawQuery` and `NormalisedQuery` are PII per FR-SRC-04. Consumers of this record MUST:

- Honour RBAC gating (NFR-SEC-05) when exposing instances through any backoffice surface, dashboard, or reporting API.
- NOT log either field through structured-logging substrates (the audit-log convention strips both — see `AnalyzerSearchEventManagementController` contract).
- Treat anonymisation cascade behaviour as "row is removed entirely on anonymisation" (hard-delete) — never anticipate a re-keyed `AnalyticsSearchEvent` instance for an anonymised visitor.

## Equality

Record equality is by structural value (all `init` props). Two `AnalyticsSearchEvent` instances with identical field values are equal — useful for set semantics in tests.

## Versioning

Slice-007 addition is MINOR (additive); breaking changes require MAJOR. Pinning baseline regenerated in Polish phase; the diff is purely additive (one new record, one new interface, one new `IAnalyticsEventStateProvider` member).

## Consumers

- `IAnalyticsEventStateProvider.CurrentRequestSearchEvents` — in-request enumeration for other Analyzer code (audit emitters, cross-event correlators).
- Eventual read-side reporting slice — the search-report query layer projects rows into this shape via the reporting API; the `FR-SRC-03` click-through-attribution view joins this record to subsequent `customizerPageview` rows in the same session.
- Third-party `IEventDimensionExtractor` implementations — receive `AnalyticsSearchEvent` instances as part of dimension-extraction context (parity with slice 004 / 005 / 006 events).
