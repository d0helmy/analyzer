# Quickstart: Scroll Tracking

**Slice**: 006-scroll-tracking
**Audience**: developers verifying slice 006 end-to-end against a running Umbraco host with the Analyzer bundle loaded.

This document is the manual-verification companion to `/speckit-implement` — it walks the two user stories from the spec against a real intranet page so the green tests don't paper over an end-to-end gap.

---

## 0. Prerequisites

- An Umbraco 17.3.5 host that project-references Analyzer + Customizer.
- The Aspire AppHost is running (`dotnet run --project aspire/Analyzer.AppHost`) so the SQL container is warm and migrations have applied (in particular, `M0006_AddAnalyzerScrollSampleTable`).
- An authenticated EntraID employee account (or a `TestEntraIdProvider`-stubbed one if the host is mocked).
- A Razor or headless intranet page that emits `window.analyzer.pageviewKey` + `window.analyzer.contentKey` (the same script tag slice 004 / 005 host pages already render).

Sanity check before walking the stories:

```bash
sqlcmd -Q "SELECT TOP 1 * FROM analyzerScrollSample" -d Analyzer    # empty rowset, no error
sqlcmd -Q "SELECT name FROM sys.indexes WHERE name = 'UX_analyzerScrollSample_pageviewBucket'" -d Analyzer
# Expected: one row — the unique index is present
```

---

## 1. US1 — Scroll-depth milestones (P1)

**Setup**: create a content page `/scroll-demo` whose rendered body is taller than the viewport (≥ 5× the viewport height is ideal so milestone boundaries are well-separated). No `analyzer-no-tracking` attribute. Confirm via DevTools that `window.analyzer.pageviewKey` is set after page-load.

**Steps**:

1. Open the page as an authenticated employee. Open DevTools Network panel; filter to `/scroll-event/milestone`.
2. Slowly scroll from top to bottom. As each milestone is crossed, observe one POST fires with the corresponding `bucket` value:
   - At ~25 % scroll → POST with `bucket: 25`, response `202 { eventKey: '...' }`.
   - At ~50 % → POST with `bucket: 50`.
   - At ~75 % → POST with `bucket: 75`.
   - At ~100 % (bottom of page) → POST with `bucket: 100`.
3. Scroll back to the top, then back down. Verify: **zero** additional POSTs (the closure-scoped `crossed` set already contains all four buckets).

**Verify in DB**:

```sql
SELECT bucket, receivedUtc, pageviewKey
FROM analyzerScrollSample
WHERE visitorProfileKey = @your_visitor_key
ORDER BY receivedUtc;
```

Expected: exactly 4 rows in ascending `bucket` order (25, 50, 75, 100). All share the same `pageviewKey` and `contentKey`; `sessionKey` is the same Guid (single session). `receivedUtc` strictly increasing.

**Idempotency spot-check** (manual replay):

Using curl, replay the bucket-25 POST against the same `pageviewKey`:

```bash
curl -X POST https://your-host/umbraco/management/api/v1/analyzer/scroll-event/milestone \
  -H "Cookie: <your backoffice auth cookie>" \
  -H "X-Anti-Forgery-Token: <your token>" \
  -H "Content-Type: application/json" \
  -d '{"pageviewKey":"<the same guid>","contentKey":"<the same guid>","bucket":25}'
```

Expected: HTTP 409 with `{ "code": "duplicate" }`. The DB row count is still exactly 4; the structured log shows one `AnalyzerScrollEventCaptured … Duplicate` entry.

---

## 2. US2 — Opt-out (`analyzer-no-tracking`)

**Setup**: edit the demo page's layout to add `analyzer-no-tracking` to the `<body>` element:

```html
<body analyzer-no-tracking>
  ...
</body>
```

1. Reload `/scroll-demo`. Open DevTools Network panel.
2. Scroll top to bottom slowly, crossing all four milestone positions.
3. Verify: **ZERO** POSTs to `/umbraco/management/api/v1/analyzer/scroll-event/milestone`.
4. Inspect DB:

```sql
SELECT COUNT(*) FROM analyzerScrollSample
WHERE pageviewKey = @your_pageview_key;
```

Expected: `0`.

5. Remove the attribute, reload. Verify: capture resumes (4 POSTs, 4 rows).

---

## 3. Edge case — Short page

**Setup**: create a page `/scroll-short` whose rendered content is shorter than the viewport (e.g. a single-line "Welcome" headline). Confirm via DevTools that `document.documentElement.scrollHeight === innerHeight` (or `scrollHeight - innerHeight <= 0`).

1. Open the page as an authenticated employee. Open DevTools Network panel.
2. Observe: exactly **one** POST fires on page-ready with `bucket: 100`. No POST for buckets 25 / 50 / 75.
3. Attempt to scroll (mouse-wheel / arrow keys). Verify: no additional POSTs.

**Verify in DB**:

```sql
SELECT bucket FROM analyzerScrollSample
WHERE pageviewKey = @short_page_pageview_key;
```

Expected: exactly 1 row with `bucket = 100`.

---

## 4. Cascade hard-delete (anonymisation)

**Setup**: capture a few scroll milestones for visitor V (see US1 walkthrough). Confirm rows exist:

```sql
SELECT COUNT(*) FROM analyzerScrollSample WHERE visitorProfileKey = @V;
-- Expected: 4 (or however many milestones you captured)
```

1. Trigger anonymisation for V via Customizer's backoffice "Anonymise visitor" action (or its management endpoint).
2. Re-run the count query.

Expected: `0`. The cascade step hard-deletes all rows in the same NPoco scope as Customizer's visitor-profile re-key. Rows for *other* visitors are untouched.

**Rollback assertion** (only if you have a sentinel-throwing cascade step wired in for testing):

1. Register a sentinel cascade step that throws AFTER `AnalyzerScrollSampleCascadeStep`.
2. Re-run anonymisation.
3. Expected: the outer scope rolls back; `analyzerScrollSample` rows for V are still present (the DELETE did not commit).

---

## 5. Perf-smoke baseline

After implementation, run the perf-smoke test:

```bash
dotnet test --filter "Category=Perf" --logger "console;verbosity=detailed" \
  src/Analyzer.Tests/Analyzer.Tests.csproj
```

Expected outputs (per SC-004 + SC-006 budgets):

- `CascadeHardDeleteThroughputSmoke`: 1 000 rows deleted in ≤ 200 ms (P95 over 5 runs).
- `ScrollEventCaptureLatencySmoke`: 200 events/min sustained for 60 s; P99 server-side persistence < 1 s.
- `ScrollBundleFcpOverheadSmoke` (Vitest with Playwright trace, optional): scroll module init adds ≤ 5 ms to FCP on a synthetic 5 000 px page vs slice-005 baseline.

---

## 6. Audit-log spot-check (SC-007)

For any successful capture, grep the structured log:

```bash
# Assuming Serilog with the default Information sink
grep "AnalyzerScrollEventCaptured" /var/log/umbraco/*.log | head
```

Each line MUST carry: `EventKey`, `PageviewKey`, `Bucket`, `ActorUpn`, `ReceivedUtc`. Verify row count vs log count:

```sql
SELECT COUNT(*) FROM analyzerScrollSample WHERE receivedUtc >= @session_start;
-- Compare with: grep "AnalyzerScrollEventCaptured" ... | wc -l
```

Numbers should match exactly (one log entry per inserted row; one additional `Duplicate`-tagged entry per 409).

---

## 7. Identity-gate spot-check (SC-005)

1. Open an incognito window (unauthenticated). Visit `/scroll-demo`.
2. Manually fire a POST via curl with no backoffice cookie:

```bash
curl -X POST https://your-host/umbraco/management/api/v1/analyzer/scroll-event/milestone \
  -H "Content-Type: application/json" \
  -d '{"pageviewKey":"00000000-0000-0000-0000-000000000001","contentKey":"00000000-0000-0000-0000-000000000002","bucket":25}'
```

Expected: HTTP 401 (no auth cookie) or 403 (cookie present but visitor identity unavailable). DB row count is unchanged.
