# Contract: AnalyzerContentAnalyticsManagementController

**Slice**: 008-content-analytics-app
**Surface**: Backoffice management API endpoint
**Type**: HTTP route + response shape

## Route

```
GET /umbraco/management/api/v1/analyzer/content-analytics/{contentKey:guid}
```

Mounted under the Analyzer management-API namespace per `Principle IX` + the Tech Stack route-prefix constraint. Slice-007's `POST .../analyzer/search-event` is the prior precedent for the namespace shape.

## Authorization

Decorated with:

```csharp
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[Route("umbraco/management/api/v1/analyzer/content-analytics")]
[ApiController]
[ApiVersion("1.0")]
public sealed class AnalyzerContentAnalyticsManagementController : ManagementApiControllerBase
{
    [HttpGet("{contentKey:guid}")]
    [MapToApiVersion("1.0")]
    public async Task<IActionResult> GetAsync([FromRoute] Guid contentKey, CancellationToken cancellationToken) { … }
}
```

Same `BackOfficeAccess` policy used by slices 004-007. No anti-forgery requirement on GET (Umbraco convention).

## Request

- **Method**: `GET`
- **Route parameter**: `contentKey` — must parse as a `Guid` (route constraint `:guid` enforces). Invalid GUID format returns 400 at the routing layer before the action method is invoked.
- **Headers**: standard backoffice cookie auth + `UMB-XSRF-TOKEN` (browser-attached; not used by GET but present from the backoffice session).
- **Body**: none.

## Response (200 OK)

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

Body matches `ContentAnalyticsSnapshot` (see sibling contract). Returned when:

- The `contentKey` parses as a GUID AND
- At least one pageview row exists in `customizerVisitorPageview` for that key in the last 30 days, OR
- `IPublishedContentCache.GetById(contentKey)` returns a non-null `IPublishedContent`.

When neither condition holds → 404 (see below).

When the first condition holds with zero counts: still 200 with all metric fields set to 0 / null (per `FR-RPT-010`). This includes the case "content node exists in the cache but has never been viewed" — explicit 200 not 404.

## Response (404 Not Found)

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
```

```json
{
  "type": "https://docs.umbraco.com/problem-details/analyzer/content-analytics/not-found",
  "title": "Content node not found",
  "status": 404,
  "detail": "No content node or capture data found for the supplied contentKey.",
  "contentKey": "<echoed GUID>"
}
```

Returned when both:
- `IPublishedContentCache.GetById(contentKey)` returns null, AND
- No row exists in `customizerVisitorPageview` for `contentKey` in the last 30 days.

(The 30-day window is consistent with the slice's overall window scope. A content node deleted longer ago than 30 days returns 404 with no historical analytics; the data may exist beyond 30d but the slice's scope is the 30d window.)

## Response (401 Unauthorized)

Anonymous requests (no backoffice cookie or invalid token) are rejected by the `BackOfficeAccess` policy before the action runs. Standard Umbraco 401 response shape.

## Response (403 Forbidden)

Not used in MVP. The future per-visitor drill-down endpoint variant may use 403 for users not in the `Analytics.IndividualData` group; this slice's aggregate endpoint is visible to anyone with `BackOfficeAccess` per Spec Clarifications §1.

## Idempotency

GET requests are idempotent and safe. The endpoint performs no writes. Two consecutive identical requests return identical responses (modulo `windowEndUtc` advancing by the request-interval).

## Audit logging

**No audit log entry is emitted on this read endpoint** per `Principle VII` (audit is for state-changing actions). Operators who need access auditing can rely on Umbraco's section-access logs and standard web server logs.

## Caching

- **Server-side**: none. Aggregation runs on each request per Spec Assumption.
- **HTTP**: `Cache-Control: no-store` (the response includes `windowEndUtc` which is request-time-bound; caching would surface stale numbers).
- **ETag**: not used (the response is not designed for conditional refresh in MVP).

## Performance contract

- SC-001: For a `contentKey` with ≤ 10,000 pageviews in the 30d window, p95 server-side processing latency MUST be < 500 ms (allowing 1.5 s for network + bundle parse + render to hit the user-facing 2 s budget).
- SC-002: For ≤ 100,000 pageviews, p95 server-side latency MUST be < 3 s (allowing 2 s for network + render to hit the 5 s user-facing budget).

Integration test `ContentAnalyticsRepositoryIntegrationTests` seeds the 10k case + the 100k case and asserts the timing budget via `Stopwatch.ElapsedMilliseconds`.

## Failure modes

| Condition | Status | Body | Side-effects |
|---|---|---|---|
| GUID parses, content+capture both unknown | 404 | Problem details | None |
| GUID parses, content known, no captures yet | 200 | Snapshot with all zeros / `avgTime…=null` | None |
| GUID parses, captures exist, content removed | 200 | Snapshot with `isContentCurrentlyTombstoned=true` | None |
| GUID malformed | 400 | Routing-layer default | None (action not invoked) |
| Authorization fails | 401 | Umbraco default | None |
| DB unreachable / query throws | 500 | Umbraco default | Exception logged at server (no PII in exception data) |
| `IPublishedContentCache` injection fails | startup failure | n/a | Composer-level error caught at host boot |

## Test plan

- **Unit**: `AnalyzerContentAnalyticsManagementControllerTests` — mock `IContentAnalyticsQueryService` + `IIndividualDataAccessCheck` + `IPublishedContentCache`, assert routing, 404 vs 200 branch, snapshot construction.
- **Integration**: `ContentAnalyticsEndToEndTests` — Testcontainers MSSQL + `WebApplicationFactory<Program>` from slice-002 base; seeds visitors + sessions + pageviews via slice-002/003 helpers with faked EntraID claims (per the slice-007 + `EndToEndCaptureTests` pattern). Asserts:
  1. 200 with correct counts after seeding 10 pageviews / 3 visitors / 1 session.
  2. 404 for unknown GUID.
  3. 200 with all zeros for known-content-key with no captures.
  4. 401 without backoffice cookie.
  5. `windowEndUtc` field matches the test's `FakeTimeProvider`.
  6. JSON response contains no field matching the reserved identity field names (privacy invariant).
- **Performance**: `ContentAnalyticsRepositoryIntegrationTests` time-budget assertions per SC-001/002.
