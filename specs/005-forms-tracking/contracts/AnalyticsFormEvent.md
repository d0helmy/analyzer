# Contract — `AnalyticsFormEvent` + `AnalyticsFormFieldEvent`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: public; new in slice 005. Pinned via the existing `PublicSurfacePinningTests` baseline (regenerated as a Polish-phase task).

Immutable projections of `analyzerFormEvent` / `analyzerFormFieldEvent` rows. Returned by `IAnalyticsEventStateProvider.CurrentRequestFormEvents` / `…CurrentRequestFormFieldEvents` for in-process consumers within the request scope.

## Namespace

```
Analyzer.Analytics.AnalyticsFormEvent
Analyzer.Analytics.AnalyticsFormFieldEvent
Analyzer.Analytics.AnalyzerFormEventType
Analyzer.Analytics.AnalyzerFormFieldEventType
```

All four types live in the pinned `Analyzer.Analytics` namespace alongside slice-003 `AnalyticsSession` and slice-004 `AnalyticsCustomEvent`.

## Shapes

```csharp
namespace Analyzer.Analytics;

public sealed record AnalyticsFormEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid ContentKey,
    AnalyzerFormEventType EventType,
    int? ElapsedMsFromImpression,
    int? ElapsedMsFromStart,
    DateTimeOffset ReceivedUtc);

public sealed record AnalyticsFormFieldEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid FieldKey,
    AnalyzerFormFieldEventType EventType,
    bool? HadValue,
    DateTimeOffset ReceivedUtc);

public enum AnalyzerFormEventType : byte
{
    Impression = 0,
    Start = 1,
    Success = 2,
    Abandon = 3,
}

public enum AnalyzerFormFieldEventType : byte
{
    FieldFocus = 0,
    FieldUnfocus = 1,
}
```

## Property semantics — `AnalyticsFormEvent`

| Property | Type | Semantics |
|---|---|---|
| `EventKey` | `Guid` | Stable public handle. Matches DB row's `eventKey`. Returned by HTTP 202 body. |
| `VisitorProfileKey` | `Guid` | Hard FK to `customizerVisitorProfile.key`. Always non-empty. |
| `SessionKey` | `Guid?` | Soft FK to `analyzerSession.sessionKey`. NULL allowed (back-pressure-drop / pre-sessions cohort). |
| `FormKey` | `Guid` | Umbraco Forms `Form.Id`. Non-empty. |
| `ContentKey` | `Guid` | Umbraco content node hosting the form. Non-FK (tombstone tolerance). |
| `EventType` | `AnalyzerFormEventType` | Discriminator. |
| `ElapsedMsFromImpression` | `int?` | Set only when `EventType == Start`. NULL otherwise. |
| `ElapsedMsFromStart` | `int?` | Set only when `EventType ∈ { Success, Abandon }`. NULL otherwise. |
| `ReceivedUtc` | `DateTimeOffset` | When the management endpoint observed the request (or, for Abandon rows, when the sweeper materialised the row). Sourced from injected `TimeProvider`. |

## Property semantics — `AnalyticsFormFieldEvent`

| Property | Type | Semantics |
|---|---|---|
| `EventKey` | `Guid` | Stable public handle. |
| `VisitorProfileKey` | `Guid` | Hard FK. |
| `SessionKey` | `Guid?` | Soft FK. |
| `FormKey` | `Guid` | Compound key with `FieldKey`. |
| `FieldKey` | `Guid` | Umbraco Forms `Field.Id`. |
| `EventType` | `AnalyzerFormFieldEventType` | Discriminator. |
| `HadValue` | `bool?` | Set only when `EventType == FieldUnfocus`. NULL on `FieldFocus`. **The boolean is the ONLY property derived from field content**; field values themselves are never captured (Principle VII privacy invariant; SC-003). |
| `ReceivedUtc` | `DateTimeOffset` | Same as above. |

## Versioning

Slice 005 lands all four types at semver MINOR-additive. No member is breaking; the enums are `byte`-backed for stable wire representation.

## Conformance tests (Phase 1 → Phase 6)

| Conformance | Test class |
|---|---|
| `AnalyticsFormEvent` round-trips through `IAnalyticsEventStateProvider.CurrentRequestFormEvents` | `AnalyticsEventStateStoreTests.RoundTripsFormEvent` |
| `AnalyticsFormFieldEvent` round-trips through `…CurrentRequestFormFieldEvents` | `AnalyticsEventStateStoreTests.RoundTripsFormFieldEvent` |
| `AnalyzerFormEventType` byte values are stable | `EnumStabilityTests` (single-row data-driven, asserts each member's `(byte)` value) |
| `AnalyzerFormFieldEventType` byte values are stable | same |
| Public surface pinning includes the new types | `PublicSurfacePinningTests` baseline diff |
