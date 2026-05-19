# Contract — `AnalyticsScrollSample` + `AnalyzerScrollBucket`

**Feature**: `006-scroll-tracking`
**Date**: 2026-05-19
**Stability**: public; new in slice 006. Pinned via the existing `PublicSurfacePinningTests` baseline (regenerated as a Polish-phase task).

Immutable projection of an `analyzerScrollSample` row. Returned by `IAnalyticsEventStateProvider.CurrentRequestScrollEvents` for in-process consumers within the request scope, and surfaced through the eventual read-side reporting API.

## Namespace

```
Analyzer.Analytics.AnalyticsScrollSample
Analyzer.Analytics.AnalyzerScrollBucket
```

Both types live in the pinned `Analyzer.Analytics` namespace alongside slice-003 `AnalyticsSession`, slice-004 `AnalyticsCustomEvent`, and slice-005 `AnalyticsFormEvent` / `AnalyticsFormFieldEvent`.

## Shapes

```csharp
namespace Analyzer.Analytics;

public sealed record AnalyticsScrollSample
{
    public required Guid EventKey { get; init; }
    public required Guid VisitorProfileKey { get; init; }
    public Guid? SessionKey { get; init; }
    public required Guid PageviewKey { get; init; }
    public required Guid ContentKey { get; init; }
    public required AnalyzerScrollBucket Bucket { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
}

public enum AnalyzerScrollBucket : byte
{
    Quarter = 25,
    Half = 50,
    ThreeQuarters = 75,
    Full = 100,
}
```

## Field semantics

| Field | Source | Notes |
|-------|--------|-------|
| `EventKey` | server-generated | Public identity for the captured row; UX-indexed in DB. |
| `VisitorProfileKey` | `IVisitorIdentifier.Resolve()` | EntraID `oid`-first, `upn`-fallback (FR-008). |
| `SessionKey` | slice-003 `IAnalyzerSessionResolver` | Nullable: pre-sessions cohort + back-pressure-drop posture. |
| `PageviewKey` | client → server | Carried in the POST payload; resolved client-side from `window.analyzer.pageviewKey` (R6). |
| `ContentKey` | client → server | Carried in the POST payload; the Umbraco content node hosting the page. |
| `Bucket` | client-derived | One of {25, 50, 75, 100}; CHECK constraint enforces at DB layer. |
| `ReceivedUtc` | server-set | `DateTimeOffset.UtcNow` at handler entry. |

## Equality

Record equality is by structural value (all `init` props). Two `AnalyticsScrollSample` instances with identical field values are equal — useful for set semantics in tests.

## Versioning

Slice-006 addition is MINOR (additive); breaking changes require MAJOR. Pinning baseline regenerated in Polish phase; the diff is purely additive (one new record, one new enum, one new `IAnalyticsEventStateProvider` member).

## Consumers

- `IAnalyticsEventStateProvider.CurrentRequestScrollEvents` — in-request enumeration for other Analyzer code (audit emitters, cross-event correlators).
- Eventual read-side slice — the heatmap query layer projects rows into this shape via the reporting API.
- Third-party `IEventDimensionExtractor` implementations — receive `AnalyticsScrollSample` instances as part of dimension-extraction context (parity with slice 004 / 005 events).
