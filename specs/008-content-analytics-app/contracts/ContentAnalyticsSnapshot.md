# Contract: ContentAnalyticsSnapshot

**Slice**: 008-content-analytics-app
**Surface**: Public DTO returned by `AnalyzerContentAnalyticsManagementController`
**Namespace**: `Analyzer.Reporting.ContentAnalytics`
**Stability**: PUBLIC — additive baseline diff to `PublicSurfacePinningTests`

## C# shape

```csharp
namespace Analyzer.Reporting.ContentAnalytics;

/// <summary>
/// Aggregate read-side view of one content node's usage. Returned by the
/// per-content-node Analytics management endpoint. Computed on demand
/// from existing capture tables (slices 002-007 + Customizer slice-003);
/// not persisted.
/// </summary>
public sealed record ContentAnalyticsSnapshot(
    Guid ContentKey,
    DateTimeOffset WindowEndUtc,
    int Pageviews24h,
    int Pageviews7d,
    int Pageviews30d,
    int UniqueVisitors30d,
    long? AvgTimeOnPageSeconds30d,
    bool IsContentCurrentlyTombstoned,
    IReadOnlyList<string> TopReferrers30d);
```

## JSON shape (wire format)

```json
{
  "contentKey": "ac716910-a82e-4280-bdf1-3b752e04b5b3",
  "windowEndUtc": "2026-05-20T19:12:34.567+00:00",
  "pageviews24h": 12,
  "pageviews7d": 84,
  "pageviews30d": 318,
  "uniqueVisitors30d": 47,
  "avgTimeOnPageSeconds30d": 92,
  "isContentCurrentlyTombstoned": false,
  "topReferrers30d": []
}
```

## Invariants

1. `Pageviews24h ≤ Pageviews7d ≤ Pageviews30d` — enforced by construction (same query, narrower predicate).
2. `UniqueVisitors30d ≤ Pageviews30d` — a visitor can produce ≥ 1 pageview but each pageview has ≤ 1 visitor.
3. `AvgTimeOnPageSeconds30d` is `null` exactly when the 30d window contains zero sessions with ≥ 2 pageviews on `ContentKey`. Otherwise non-negative.
4. `TopReferrers30d` is `[]` in this slice. Field is reserved for the future click-through slice. Length cap (when populated) will be set by that slice's spec.
5. `IsContentCurrentlyTombstoned` is `true` iff `IPublishedContentCache.GetById(ContentKey)` returns null AND at least one pageview row exists for `ContentKey` in the 30d window. (If no pageview rows exist AND cache is null, the endpoint returns 404 — the DTO is never constructed.)
6. `WindowEndUtc` is the same `TimeProvider.GetUtcNow()` value used as the upper bound for all three windows in this snapshot — consistency invariant: a follower of `windowEndUtc - 24h` MUST be the same instant the controller used to bound `Pageviews24h`.

## Privacy invariants

- The DTO contains **no** field carrying personally-identifying information. No `identityRef`, `upn`, `oid`, `userEmail`, `displayName`, or similar.
- Test `PublicSurfacePinningTests.ContentAnalyticsSnapshot_ContainsNoIdentityFields()` asserts the type's properties don't include any of the reserved identity field names (case-insensitive substring check against `upn`, `oid`, `email`, `identityref`, `displayname`).

## Pinning entry

The DTO is added to `PublicSurfacePinningTests`'s baseline manifest as a new public type. Baseline diff is **additive** (Principle X: no removed or renamed members). Members added:

```
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot..ctor(Guid, DateTimeOffset, int, int, int, int, long?, bool, IReadOnlyList<string>)
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.ContentKey
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.WindowEndUtc
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.Pageviews24h
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.Pageviews7d
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.Pageviews30d
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.UniqueVisitors30d
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.AvgTimeOnPageSeconds30d
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.IsContentCurrentlyTombstoned
Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot.TopReferrers30d
```

## Forward compatibility

When the per-visitor drill-down slice arrives, it MAY add new fields to this DTO (e.g. `recentVisitors[]`, a list of visitor profiles filtered by the role-gate check). Such additions MUST:

- Be nullable or default-empty (non-breaking for existing consumers).
- Be gated on `IIndividualDataAccessCheck.IsAuthorised(principal) == true` — omitted entirely from the JSON when the requesting user is not in the configured group, to avoid leaking even the field's presence.
- Update this contract document and add a new pinning baseline entry.
