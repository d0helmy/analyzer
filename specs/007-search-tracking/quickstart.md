# Quickstart: Internal Search-Tracking Capture

**Slice**: 007-search-tracking
**Audience**: developers verifying slice 007 end-to-end against a running Umbraco host with the Analyzer bundle loaded.

This document is the manual-verification companion to `/speckit-implement` — it walks the two user stories from the spec against a real intranet page so the green tests don't paper over an end-to-end gap.

---

## 0. Prerequisites

- An Umbraco 17.3.5 host that project-references Analyzer + Customizer.
- The Aspire AppHost is running (`dotnet run --project aspire/Analyzer.AppHost`) so the SQL container is warm and migrations have applied (in particular, `M0007_AddAnalyzerSearchEventTable`).
- An authenticated EntraID employee account (or a `TestEntraIdProvider`-stubbed one if the host is mocked).
- A Razor or headless intranet page that emits `window.analyzer.pageviewKey` (the same script tag slice 004 / 005 / 006 host pages already render). The host page does NOT need to render a real search box — the quickstart drives the helper directly from the DevTools console.

Sanity check before walking the stories:

```bash
sqlcmd -Q "SELECT TOP 1 * FROM analyzerSearchEvent" -d Analyzer    # empty rowset, no error
sqlcmd -Q "SELECT name FROM sys.indexes WHERE name = 'IDX_analyzerSearchEvent_normalisedQuery'" -d Analyzer
# Expected: one row — the normalised-query aggregation index is present
sqlcmd -Q "SELECT name FROM sys.foreign_keys WHERE name IN ('FK_analyzerSearchEvent_VisitorProfile','FK_analyzerSearchEvent_Session')" -d Analyzer
# Expected: two rows — both FKs are present
```

---

## 1. US1 — Search submissions captured with normalised query + result count (P1)

**Setup**: navigate to any intranet content page. No `analyzer-no-tracking` attribute. Confirm via DevTools that `window.analyzer.pageviewKey` is set after page-load. Confirm `typeof window.analyzer.sendSearch === 'function'`.

**Steps**:

1. Open the page as an authenticated employee. Open DevTools Network panel; filter to `/search-event`.
2. In the Console, run:

   ```js
   await window.analyzer.sendSearch("design system", 12);
   // → { eventKey: "<guid>" }
   ```

   Observe one POST fires with body `{ pageviewKey: "<guid>", query: "design system", resultCount: 12 }`, response `202 { eventKey: '<guid>' }`.

3. Submit a normalisation-variant of the same query:

   ```js
   await window.analyzer.sendSearch("  Ｄｅｓｉｇｎ  SYSTEM  ", 12);
   // → { eventKey: "<different guid>" }
   ```

   Observe one more POST; response 202 with a *different* `eventKey`.

4. Submit a zero-result query:

   ```js
   await window.analyzer.sendSearch("xyzzy", 0);
   // → { eventKey: "<guid>" }
   ```

5. Submit two consecutive same-query calls (intentional re-search):

   ```js
   await window.analyzer.sendSearch("annual review", 7);
   await window.analyzer.sendSearch("annual review", 7);
   // → two distinct { eventKey } values
   ```

**Verify in DB**:

```sql
SELECT eventKey, rawQuery, normalisedQuery, resultCount, receivedUtc, pageviewKey, sessionKey
FROM analyzerSearchEvent
WHERE visitorProfileKey = @your_visitor_key
ORDER BY receivedUtc;
```

Expected: exactly 5 rows.
- Rows 1 + 2: different `rawQuery` (`"design system"` vs `"  Ｄｅｓｉｇｎ  SYSTEM  "`), same `normalisedQuery = "design system"`, same `resultCount = 12`.
- Row 3: `rawQuery = normalisedQuery = "xyzzy"`, `resultCount = 0`.
- Rows 4 + 5: both `rawQuery = normalisedQuery = "annual review"`, `resultCount = 7`, distinct `eventKey` values.
- All five rows share the same `pageviewKey`, `sessionKey`, and `visitorProfileKey`.

**Aggregation key-stability spot-check** (proves SC-007 at the row level):

```sql
SELECT normalisedQuery, COUNT(*) AS hits
FROM analyzerSearchEvent
WHERE visitorProfileKey = @your_visitor_key
GROUP BY normalisedQuery
ORDER BY hits DESC;
```

Expected three groups: `design system` (2 hits), `annual review` (2 hits), `xyzzy` (1 hit).

**Zero-result derived view** (FR-SRC-02):

```sql
SELECT normalisedQuery, receivedUtc
FROM analyzerSearchEvent
WHERE resultCount = 0;
```

Expected: one row (`xyzzy`).

**Identity gate spot-check** (manual replay):

In an Incognito window with no backoffice cookie:

```bash
curl -X POST https://your-host/umbraco/management/api/v1/analyzer/search-event \
  -H "Content-Type: application/json" \
  -d '{"pageviewKey":"<any guid>","query":"unauthorized","resultCount":1}'
# Expected: 401 Unauthorized, zero rows persisted.
```

**Visitor-bound `pageviewKey` spot-check** (defends against forged correlations — R3 + `PageviewVisitorBindingTests`):

As visitor A, attempt to POST with `pageviewKey` belonging to visitor B (look one up from `customizerPageview` for a different `visitorProfileKey`):

```bash
curl -X POST https://your-host/umbraco/management/api/v1/analyzer/search-event \
  -H "Cookie: <visitor A's backoffice auth cookie>" \
  -H "X-Anti-Forgery-Token: <visitor A's token>" \
  -H "Content-Type: application/json" \
  -d '{"pageviewKey":"<visitor B's pageviewKey>","query":"spoofed","resultCount":1}'
# Expected: 400 with structured error mentioning pageviewKey; zero rows persisted.
```

**Audit-log PII redaction spot-check** (SC-006):

Grep the host's structured-log substrate (Serilog console, App Insights query, etc.) for the `AnalyzerSearchEventCaptured` event name. Expected log line shape:

```text
AnalyzerSearchEventCaptured EventKey=<guid> PageviewKey=<guid> ResultCount=12 ActorUpn=alice@contoso.com ActorOid=<guid> ReceivedUtc=2026-05-19T14:32:01.234Z
```

**Assert**: the log line contains **no** `Query=`, `RawQuery=`, `NormalisedQuery=`, or any text fragment of the actual search term. The DB row is the canonical, role-gated record of the literal query.

---

## 2. US2 — Opt-out via `analyzer-no-tracking` attribute (P2)

**Setup**: render the same content node with the `analyzer-no-tracking` attribute on `<body>` (or `<html>`, or the document scroll root). For example, set the `analyzer-no-tracking` macro flag on the page's Document Type's controller / view.

**Steps**:

1. Open the page as an authenticated employee. Open DevTools Network panel; filter to `/search-event`.
2. In the Console, run:

   ```js
   await window.analyzer.sendSearch("anything", 3);
   // → { skipped: true }
   ```

   Observe **zero** POSTs fired. The Promise resolves with the sentinel `{ skipped: true }` instead of throwing.

3. Submit ten consecutive helper calls:

   ```js
   for (let i = 0; i < 10; i++) {
     await window.analyzer.sendSearch(`query ${i}`, i);
   }
   ```

   Observe still **zero** POSTs.

4. **Per-call evaluation spot-check** (US2 acceptance scenario 2): with the attribute still present, dynamically remove it:

   ```js
   document.body.removeAttribute('analyzer-no-tracking');
   await window.analyzer.sendSearch("after-removal", 1);
   // → { eventKey: "<guid>" }  ← one POST fires, opt-out lifted on the very next call
   ```

   This is the key behavioural difference from slice 006's scroll observer (which reads the attribute at handler-init only).

**Verify in DB** (during the opt-out portion, before step 4):

```sql
SELECT COUNT(*) FROM analyzerSearchEvent WHERE rawQuery LIKE 'query %';
-- Expected: 0
```

**Verify in DB** (after step 4's dynamic removal):

```sql
SELECT COUNT(*) FROM analyzerSearchEvent WHERE rawQuery = 'after-removal';
-- Expected: 1
```

---

## 3. Cascade hard-delete verification (FR-010 / SC-004)

**Setup**: seed visitor `V` with 1 000 `analyzerSearchEvent` rows (e.g. via `EndToEndCaptureTests` test harness or a SQL `INSERT … SELECT` against a row-multiplying CTE).

```sql
SELECT COUNT(*) FROM analyzerSearchEvent WHERE visitorProfileKey = @V;
-- Expected: 1000
```

**Trigger anonymisation**: from the Umbraco backoffice, navigate to the Customizer "Visitor profiles" surface, find visitor `V`, and invoke the "Anonymise visitor" operator action. The cascade runs synchronously; the action completes in well under a second.

**Verify**:

```sql
SELECT COUNT(*) FROM analyzerSearchEvent WHERE visitorProfileKey = @V;
-- Expected: 0  (all rows hard-deleted)

SELECT COUNT(*) FROM analyzerSearchEvent;
-- Expected: row count of other visitors — V's rows are gone, others untouched.

SELECT COUNT(*) FROM customizerVisitorProfile WHERE [key] = @V;
-- Expected: 1 (the visitor profile row is re-keyed by Customizer's own cascade,
--             not deleted — same convention as slices 002/004/006).
```

**PII-cleanup spot-check** (proves the literal query text is gone, not just the link):

```sql
SELECT TOP 1 *
FROM analyzerSearchEvent
WHERE rawQuery LIKE @uniqueSeedQuerySubstring;
-- Expected: zero rows. The seed query (e.g. "alice's birthday plan") no longer exists.
```

**Latency check** (SC-004 budget — 200 ms for 1 000 rows): the cascade-step's contribution can be measured via the host's structured-log substrate. Find the `AnalyzerSearchEventCascadeStep.ExecuteAsync` span / log entry; assert duration < 200 ms.

---

## 4. Custom-normaliser swap (FR-005)

This step is optional — verify only if the slice's PR description claims the extension point is operational.

**Setup**: in the host's composer, register a custom normaliser that uppercases everything (a deliberately bad implementation, but useful for verification):

```csharp
public sealed class UppercaseNormaliserComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IAnalyzerSearchQueryNormaliser, UppercaseNormaliser>();
    }
}

internal sealed class UppercaseNormaliser : IAnalyzerSearchQueryNormaliser
{
    public string Normalise(string rawQuery) => rawQuery.Trim().ToUpperInvariant();
}
```

**Trigger**: from the Console, run:

```js
await window.analyzer.sendSearch("Hello World", 1);
```

**Verify in DB**:

```sql
SELECT rawQuery, normalisedQuery FROM analyzerSearchEvent ORDER BY receivedUtc DESC;
```

Top row: `rawQuery = "Hello World"`, `normalisedQuery = "HELLO WORLD"`. This proves the host's `AddScoped` override took precedence over Analyzer's default (last-registration-wins).

**Roll back**: remove the host composer; restart the host; confirm subsequent submissions revert to the default normaliser's lower-case + NFKC + whitespace-collapse output.

---

## 5. Cleanup

```sql
DELETE FROM analyzerSearchEvent WHERE rawQuery IN ('design system', '  Ｄｅｓｉｇｎ  SYSTEM  ', 'xyzzy', 'annual review', 'after-removal', 'Hello World');
```

(Or invoke the visitor-anonymisation cascade for the test visitor to remove every row authored by them.)

---

## 6. What was NOT verified by this quickstart

- The Events report aggregation surface (deferred to the read-side reporting slice that lights up `FR-SRC-03`).
- Click-through attribution ("queries followed by no click-through") — also deferred.
- Webhook delivery — search events do not emit through Customizer's outbox (out of scope; the eventual reporting API serves the same operational need per Constitution Reporting & Open Surface).
- Multi-language normalisation against a Turkish-locale host — covered by the `Culture-stability` unit test, not the quickstart.
