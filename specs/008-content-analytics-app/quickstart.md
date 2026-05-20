# Quickstart: Per-Content-Node Analytics Content App

**Slice**: 008-content-analytics-app
**Audience**: developers verifying slice 008 end-to-end against a running Umbraco host with the Analyzer bundle loaded.

> ⚠️ **MANUAL QUICKSTART DEFERRED** — manual verification against a live browser is currently blocked by [#34](https://github.com/d0helmy/analyzer/issues/34) (no EntraID claims shim for local dev) and [#33](https://github.com/d0helmy/analyzer/issues/33) (content-save scope race). Without #34, `Customizer.PageviewCaptureMiddleware` refuses to capture pageviews because `VisitorIdentity.FromClaims` returns `Unresolved` for any local-dev request. Without seeded pageviews, the Analytics tab will always show the empty-state and US1 cannot be exercised.
>
> The slice ships with full automated coverage (server-side unit + Vitest jsdom + Testcontainers integration with faked EntraID claims, per the slice 007 pattern). This document is the runbook to use **once #34 lands**. Until then, treat US2 (empty state on a fresh node) as the only story manually verifiable, and use the integration tests for US1 + US3.

---

## 0. Prerequisites

- An Umbraco 17.3.5 host that project-references Analyzer + Customizer (existing sample host at `samples/Analyzer.Host` works).
- The Aspire AppHost is running (`dotnet run --project aspire/Analyzer.AppHost`) so the SQL container is warm and migrations have applied.
- An authenticated EntraID employee account (or a `TestEntraIdProvider`-stubbed one once #34 lands).
- At least one published content node with historical pageviews (per US1 + US3). For US2, a freshly-published node with no traffic.

Sanity check before walking the stories:

```bash
sqlcmd -Q "SELECT COUNT(*) FROM customizerVisitorPageview" -d Analyzer
# Expected: > 0 if visitors have browsed since the last DB reset
sqlcmd -Q "SELECT name FROM sys.indexes WHERE name = 'IX_customizerVisitorPageview_contentKey_requestUtc'" -d Analyzer
# Expected: one row — the index this slice's query relies on
```

---

## 1. US1 — Editor reviews a content node's usage at a glance (P1)

**Setup**: pick a published content node that has at least 5 historical pageviews from at least 3 distinct visitors. Note its content GUID (`umbracoNode.uniqueId` for the row whose `text = '<node name>'`).

**Steps**:

1. Sign in to `/umbraco/login` as a backoffice user (any group — per Spec Clarifications §1, no Analyzer-defined gate).
2. Navigate to **Content** → click the content node.
3. Click the **Analytics** tab in the content-app strip (top of the workspace, between Content and Info).
4. Watch for the skeleton state (5 grey placeholder rectangles, `aria-busy="true"`).
5. Skeleton swaps to real numbers within ~2 seconds (SC-001 budget).

**Verify in the UI**:

- Pageviews block shows three numbers: `24h`, `7d`, `30d`. The 24h count ≤ 7d ≤ 30d.
- Unique visitors shows a 30d count.
- Avg time on page shows a duration (e.g. `1m 32s`) or `—` if no session has ≥ 2 pageviews on this node.
- No banner. No error state.

**Verify in DevTools**:

- Network tab → filter to `/content-analytics/` → one `GET` request to `https://<host>/umbraco/management/api/v1/analyzer/content-analytics/<contentKey>` returning **200**.
- Response payload matches `ContentAnalyticsSnapshot` shape (see contract). Confirm:
  - `windowEndUtc` is within a second of "now".
  - `topReferrers30d: []` (always empty in this slice).
  - `isContentCurrentlyTombstoned: false`.
  - **No** `identityRef`, `upn`, `oid`, or `userEmail` field anywhere in the JSON (privacy invariant SC-005).

**Cross-node bleed spot-check**:

1. Open node A's Analytics tab, note the numbers.
2. Navigate to node B (different content node).
3. Open node B's Analytics tab.
4. Numbers differ from A. Tab refresh shows B's numbers consistently.

---

## 2. US2 — Empty / never-viewed content shows zero gracefully (P2)

**Setup**: create and publish a fresh content node. Do **not** browse to it from the front-end.

**Steps**:

1. Open the new node in the backoffice.
2. Click **Analytics** tab.
3. Skeleton appears briefly, then resolves.

**Verify in the UI**:

- Five metric blocks render with `0` for counts and `—` for avg-time-on-page.
- A small "No activity in the last 30 days" headline appears above the blocks (or in their grid area, per UI designer's choice).
- No spinner-stuck-forever. No red error banner.

**Verify in DevTools**:

- Network tab → the `GET .../content-analytics/<contentKey>` returns **200 OK**, **not 404** (per `FR-RPT-010`).
- Response body has all metric fields at 0 / null and `isContentCurrentlyTombstoned: false`.

---

## 3. US3 — Anonymisation-preserved unique visitor count (P3)

**Setup**: pick a node that 10 distinct visitors have viewed in the last 30 days. Trigger Customizer's anonymisation cascade against 3 of those visitors via the backoffice visitor-profiles surface (see `customizer/specs/.../quickstart.md` for the operator action; tooling exists but interactive coverage is not the point of this slice — assume the cascade is triggered through the backoffice).

**Steps**:

1. Verify the 10-visitor baseline: open Analytics tab; note `uniqueVisitors30d` = 10.
2. Trigger anonymisation for visitors V1, V2, V3.
3. Hard-refresh the Analytics tab.

**Verify in the UI**:

- `uniqueVisitors30d` still shows 10 (the 3 anonymised visitors are still counted per `FR-RPT-009`).

**Verify in DevTools**:

- Network response payload: `uniqueVisitors30d: 10`.
- The JSON contains no `identityRef`, no `upn`, no anonymised-visitor identity marker. The privacy invariant holds before and after anonymisation.

**Verify in DB**:

```sql
SELECT identityRef
FROM customizerVisitorProfile
WHERE [key] IN ('<V1.key>', '<V2.key>', '<V3.key>');
-- Expected: identityRef values start with 'anonymized:' (Customizer's cascade re-key)

SELECT COUNT(DISTINCT visitorProfileFk)
FROM customizerVisitorPageview
WHERE contentKey = '<node.contentKey>'
  AND requestUtc >= DATEADD(DAY, -30, SYSUTCDATETIME());
-- Expected: 10 (unchanged by anonymisation — visitorProfileFk doesn't move)
```

---

## 4. Tombstoned content (deleted node) verification

**Setup**: pick a content node that has historical pageviews, then move it to the recycle bin.

**Steps**:

1. Note the contentKey and the 30d pageview count BEFORE delete.
2. Delete the node (sends to recycle bin).
3. Hit `GET /umbraco/management/api/v1/analyzer/content-analytics/<contentKey>` directly with `curl -k --cookie <backoffice-cookie>`.

**Verify**:

- HTTP 200 OK.
- Response has `isContentCurrentlyTombstoned: true`.
- Pageview counts and unique-visitor count are the same as before the delete (historical analytics preserved per `FR-RPT-012`).

---

## 5. Unknown content GUID

**Steps**:

1. `curl -k --cookie <backoffice-cookie> https://<host>/umbraco/management/api/v1/analyzer/content-analytics/00000000-0000-0000-0000-000000000000`

**Verify**:

- HTTP 404 Not Found.
- Body is `application/problem+json` with `title: "Content node not found"`.

---

## 6. Role-gate plumbing (MVP — no observable effect)

The `IIndividualDataAccessCheck` interface ships in this slice but has no observable effect on the response payload in MVP per Spec Clarifications §4. There is no manual verification step for this MVP — coverage is via the unit-test suite `DefaultIndividualDataAccessCheckTests`. The integration verification belongs to the future per-visitor drill-down slice.

---

## 7. Cleanup

```sql
-- If test content nodes were created for the walkthrough, recycle them via the backoffice.
-- No Analyzer-owned tables need cleanup — this slice introduced no new tables.
```

---

## 8. What was NOT verified by this quickstart

- Per-visitor drill-down (out of scope for this slice).
- Top-referrers list population (deferred to the click-through attribution slice).
- Scroll heatmap rendering (deferred to a future heatmap slice).
- Time-window customisation (24h / 7d / 30d are fixed in MVP).
- Manual verification of US1 / US3 against a live browser **without** the EntraID claims shim from [#34](https://github.com/d0helmy/analyzer/issues/34) — relies on integration tests until that shim lands.
