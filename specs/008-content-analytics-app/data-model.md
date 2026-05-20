# Data Model: Per-Content-Node Analytics Content App

**Slice**: 008-content-analytics-app
**Date**: 2026-05-20

## No new persisted entities

This slice introduces **zero new tables**, **zero new migrations**, and **zero new cascade-step registrations**. All shape below is either:

- A read-side projection materialised per-request (`ContentAnalyticsSnapshot`)
- A domain enum that exists only in code (`TimeWindow`)
- An internal application-layer DTO (`ContentAnalyticsProjection`)
- An internal authorisation primitive (`IIndividualDataAccessCheck`)

Existing tables consumed read-only:

- `customizerVisitorPageview` (owned by Customizer, slice-003) — joined by `contentKey` and `requestUtc`
- `analyzerSession` (owned by Analyzer, slice 002) — joined by `visitorProfileKey` for the time-on-page derivation
- `customizerVisitorProfile` (owned by Customizer, slice-003) — referenced only via integer FK (`visitorProfileFk`) for `COUNT(DISTINCT)` — the `identityRef` column is NEVER read

## ContentAnalyticsSnapshot (public DTO)

Returned by the management endpoint. C# record, serialised as JSON. Lives at `Analyzer.Reporting.ContentAnalytics.ContentAnalyticsSnapshot`.

```text
ContentAnalyticsSnapshot
├── contentKey                       Guid              The node this snapshot is keyed by.
├── windowEndUtc                     DateTimeOffset    The "now" moment used to anchor the three time windows.
├── pageviews24h                     int (≥ 0)         Count of pageview rows where requestUtc ≥ now − 24h.
├── pageviews7d                      int (≥ 0)         Count where requestUtc ≥ now − 7d.    Invariant: pageviews24h ≤ pageviews7d.
├── pageviews30d                     int (≥ 0)         Count where requestUtc ≥ now − 30d.   Invariant: pageviews7d ≤ pageviews30d.
├── uniqueVisitors30d                int (≥ 0)         COUNT(DISTINCT visitorProfileFk) in the 30d window.
├── avgTimeOnPageSeconds30d          long? (nullable)  AVG(DATEDIFF(SECOND, prevRequestUtc, requestUtc)) in 30d.
│                                                     Null when no session has ≥ 2 pageviews on this node within 30d.
├── isContentCurrentlyTombstoned     bool              Result of IPublishedContentCache.GetById(contentKey) == null.
└── topReferrers30d                  string[]          Always empty in MVP. Placeholder for future click-through slice.
```

### Field-level rules

| Field | Validation / invariants | Source |
|---|---|---|
| `contentKey` | Echo of the route parameter. MUST equal the requested GUID. | Route binding |
| `windowEndUtc` | UTC, set server-side once per request at handler entry. Used by all three window calculations to keep them consistent. | `TimeProvider.GetUtcNow()` |
| `pageviews24h` | `≥ 0`. `≤ pageviews7d`. | Computed in single SQL query (see research.md R3) |
| `pageviews7d` | `≥ pageviews24h`. `≤ pageviews30d`. | Same |
| `pageviews30d` | `≥ pageviews7d`. Soft ceiling 100k per SC-002. | Same |
| `uniqueVisitors30d` | `≥ 0`. `≤ pageviews30d`. Counts anonymised visitors per FR-RPT-009 (their `customizerVisitorProfile.key` survives anonymisation; their `identityRef` is re-keyed, but the `key`-based `visitorProfileFk` reference does not change). | `COUNT(DISTINCT visitorProfileFk)` |
| `avgTimeOnPageSeconds30d` | Null when the 30d window contains zero sessions with ≥ 2 pageviews on this node. Otherwise positive long. Last pageview in each session excluded (no successor). | `AVG(DATEDIFF(SECOND, LAG(requestUtc), requestUtc))` |
| `isContentCurrentlyTombstoned` | `true` when `IPublishedContentCache.GetById(contentKey)` returns null AND at least one capture row exists for the GUID. (When the GUID is unknown to both cache and capture, the endpoint returns 404 instead.) | Cache lookup, post-aggregation |
| `topReferrers30d` | Always `[]` in MVP. Field exists for forward compatibility. | Hard-coded |

### Anonymisation invariant (`FR-RPT-009` / `SC-004`)

The projection's unique-visitor count joins on `customizerVisitorPageview.visitorProfileFk` (an integer FK to `customizerVisitorProfile.id`). Customizer's anonymisation cascade:

1. Re-keys `customizerVisitorProfile.identityRef` from `oid:…` / `upn:…` to `anonymized:…`.
2. Does **not** delete the row or change `customizerVisitorProfile.id` / `customizerVisitorProfile.key`.

Therefore: every anonymised visitor's pageview rows still reference an alive `visitorProfileFk`. `COUNT(DISTINCT visitorProfileFk)` returns the correct count both before and after anonymisation. The `identityRef` column is **never** selected by this slice's queries, so the anonymised value is never exposed to a backoffice user.

---

## TimeWindow (internal domain enum)

```text
public enum TimeWindow
{
    TwentyFourHours,
    SevenDays,
    ThirtyDays
}
```

Used only to label the metric mapping at the application boundary. The repository receives `windowEndUtc` + the three `windowStart*` `DateTimeOffset` values directly; the enum doesn't ride into the SQL layer.

---

## ContentAnalyticsProjection (internal application-layer DTO)

Internal record carrying both the SQL aggregate result and the tombstone probe result before they are merged into the public `ContentAnalyticsSnapshot`. Lives in `Analyzer.Features.Reporting.Domain`. Not part of the public surface; not pinned.

```text
ContentAnalyticsProjection
├── pageviews24h                     int
├── pageviews7d                      int
├── pageviews30d                     int
├── uniqueVisitors30d                int
├── avgTimeOnPageSeconds30d          long?
├── isContentCurrentlyTombstoned     bool
└── hasAnyCaptureRow                 bool          true if at least one row exists in customizerVisitorPageview for the contentKey, used to decide 200-with-zeros vs 404
```

The repository returns this projection; the controller builds the public DTO from it.

---

## IIndividualDataAccessCheck (internal authorisation primitive)

Internal interface plus default implementation. Wired via DI for testability. Lives in `Analyzer.Features.Reporting.Application`. **Not** added to `PublicSurfacePinningTests` in this slice per Spec Clarifications §4 — promotion to public surface is owned by the future per-visitor drill-down slice.

```text
internal interface IIndividualDataAccessCheck
{
    bool IsAuthorised(ClaimsPrincipal principal);
}

internal sealed class DefaultIndividualDataAccessCheck : IIndividualDataAccessCheck
{
    private readonly IOptions<AnalyzerReportingOptions> _options;

    public bool IsAuthorised(ClaimsPrincipal principal)
    {
        var groupName = _options.Value.IndividualDataUserGroupAlias ?? "Analytics.IndividualData";
        return principal.Claims.Any(c =>
            c.Type == Umbraco.Cms.Core.Constants.Security.UserGroupClaimType &&
            string.Equals(c.Value, groupName, StringComparison.Ordinal));
    }
}
```

In MVP, the controller calls `_check.IsAuthorised(User)` but the result has no effect on the response shape — no per-visitor fields exist to filter. The call site is documented as a TODO-marker for the future slice (`// TODO: gate per-visitor fields when slice 008+ adds them`).

Unit tests cover:

1. Principal with a `userGroup` claim matching the configured alias → returns `true`.
2. Principal without any `userGroup` claim → returns `false`.
3. Principal with a `userGroup` claim mismatching the alias → returns `false`.
4. Custom alias via `AnalyzerReportingOptions.IndividualDataUserGroupAlias` config → respected.
5. Empty / whitespace alias config → falls back to the default `"Analytics.IndividualData"`.

(Test 5 covers `NFR-MNT-*` robustness; an empty config value should not silently authorise everyone.)

---

## AnalyzerReportingOptions (configuration POCO)

New `IOptions<>`-shaped configuration object bound from `appsettings.json` under `Analyzer:Reporting`. Lives in `Analyzer.Features.Reporting.Application`. Internal.

```text
AnalyzerReportingOptions
└── IndividualDataUserGroupAlias     string?     Defaults to "Analytics.IndividualData" when null/empty.
```

Default values shipped via `IPostConfigureOptions<AnalyzerReportingOptions>` — no `appsettings.json` change required for the slice's MVP behaviour. Deploying organisations override the alias in their host's config.

---

## Constitution Check — post-design

Re-validation after data-model + contract design (per `plan.md` § GATE):

| # | Principle | Status | Re-validation evidence |
|---|-----------|--------|------------------------|
| I | EntraID-Only Identity | ✅ PASS | The check-function reads `ClaimsPrincipal` (which carries the EntraID claims projected by Umbraco's auth pipeline). No anonymous identity path introduced. |
| II | Spec-Grounded Scope | ✅ PASS | Data model satisfies `FR-RPT-001` through `FR-RPT-013` exactly; no in-scope FR uncovered, no out-of-scope FR cited. |
| III | Customizer Substrate, No Retrofit | ✅ PASS | `customizerVisitorPageview` and `customizerVisitorProfile` accessed via raw SELECT (slice-002 pattern). Customizer's pinned types not imported. No Customizer-side change. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ PASS (vacuous) | No new tables; cascade-step gate not applicable. Anonymisation-invariant respected by SELECTing on `visitorProfileFk` (FK) not `identityRef` (PII). |
| V | Slice-Driven Delivery | ✅ PASS | Standard slice flow followed; tasks generation next. |
| VI | Software Engineering Excellence | ✅ PASS | Vertical-slice layout; test coverage envelope per plan; DI registrations isolated to `AnalyzerReportingComposer`. |
| VII | Security by Design | ✅ PASS | Backoffice auth gate; defence-in-depth via boundary GUID validation; role-gate primitive shipped; no PII in projection; no new credentials. Anonymisation-preserved aggregates honour the constitutional intent. |
| VIII | Performance & Scalability First | ✅ PASS | Single-pass T-SQL window-function query; existing composite indexes (`IX_customizerVisitorPageview_contentKey_requestUtc`) cover the predicates; on-demand per request with SC-001/002 budgets; no global locks, no N+1, no synchronous network I/O. |
| IX | Umbraco-Native & Operator-First | ✅ PASS | Content-app extension via `umbraco-package.json`; `@umbraco-cms/backoffice` primitives; no operator workflow code changes required. |
| X | Extensibility by Design | ✅ PASS | One additive public DTO (`ContentAnalyticsSnapshot`) pinned in `PublicSurfacePinningTests`. `IIndividualDataAccessCheck` internal-only in MVP — promotion deferred to the per-visitor drill-down slice per Spec Clarifications §4. No breaking changes to any existing public extension contract. |

**Verdict (post-design)**: 10 / 10 PASS. Plan and data-model remain aligned. No Complexity Tracking entries.
