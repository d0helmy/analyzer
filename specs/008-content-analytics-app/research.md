# Research: Per-Content-Node Analytics Content App

**Slice**: 008-content-analytics-app
**Date**: 2026-05-20

Five investigation areas surfaced while filling the plan's Technical Context. None blocked completion; this file pins the decisions so `/speckit-tasks` can be generated against concrete primitives.

---

## R1 — Umbraco 17.3.5 content-app extension registration

**Question**: How is a content-app tab registered against every Umbraco content node, and what does the bundle entry look like?

**Decision**: Register a single `Umbraco.Cms.Web.UI.UI.Element` extension in `src/Analyzer/Client/public/umbraco-package.json` with `kind: "contentApp"`, `conditions: [{ alias: "Umb.Condition.WorkspaceAlias", match: "Umb.Workspace.Document" }]`, and a `weight` lower than core apps (default 100; we use 200 to position the Analytics tab after Content but before Info). The `element` field points to a chunk inside the existing `/App_Plugins/Analyzer/analyzer.js` bundle. No new package manifest file ships.

**Rationale**:
- The `Umb.Workspace.Document` workspace alias matches every content node regardless of document type — satisfies `FR-RPT-001` (no per-doctype opt-in) without needing per-document-type registration.
- Reusing the existing bundle keeps the slice's footprint minimal — no second `App_Plugins/` directory, no separate manifest, no extra build target.
- `weight: 200` places Analytics after Content (default 100) and before Info (default 1000), matching where editors expect to look for metadata-adjacent surfaces.

**Alternatives considered**:
- *Per-document-type registration* (one manifest entry per doctype): rejected — violates `FR-RPT-001`'s "no per-doctype opt-in" and bloats the manifest.
- *Separate `App_Plugins/AnalyzerReporting/` directory with its own bundle*: rejected — splits the deployment surface; existing slice 004-007 client modules all live inside the same bundle and the Umbraco docs treat single-bundle multi-feature packages as the convention.
- *Server-side content-app registration via `IContentAppFactory`* (the pre-17 pattern): rejected — Umbraco 17 deprecated the C# `IContentAppFactory` in favour of the backoffice-defined extension manifest, and the slice 001 package skeleton already commits to the manifest pattern.

**References**:
- `umbraco-cms/Umbraco.Cms.Web.UI.UI` extension reference for `contentApp` and `Umb.Condition.WorkspaceAlias`.
- Existing Analyzer manifest: `src/Analyzer/Client/public/umbraco-package.json` (currently declares only the bundle entrypoint — a `contentApp` entry will be appended).

---

## R2 — Index coverage on `customizerVisitorPageview` for the time-window aggregation queries

**Question**: Does Customizer's existing schema cover the predicates this slice's queries will use, or do we need a migration to add an index?

**Decision**: Existing indexes are sufficient. The query plan uses two predicates: `WHERE contentKey = @key` and `WHERE requestUtc >= @windowStart`. Customizer slice-003's `customizerVisitorPageview` already has:

1. **`IX_customizerVisitorPageview_contentKey_requestUtc`** — composite, in that order. Established for the retention sweep + the click-tracking deferred-slice plan.
2. **`IX_customizerVisitorPageview_visitorProfileFk_requestUtc`** — composite. Established for visitor-history queries.

For the `COUNT(*) WHERE contentKey = @k AND requestUtc >= @start` aggregation path, the first composite is a covering index. SQL Server's query optimizer will choose it without hints.

**Rationale**:
- Customizer's slice-003 baseline was designed for these access patterns; the read-side reporting slice is a downstream consumer the index was already shaped for.
- Adding an index from Analyzer would touch Customizer's schema indirectly — would violate `Principle III` (Customizer Substrate, No Retrofit) because index changes are constitutional schema changes from Analyzer's perspective.

**Alternatives considered**:
- *Add an Analyzer-owned index on `customizerVisitorPageview`*: rejected — schema change on a Customizer-owned table requires a Customizer-side commit per `Principle III` + inter-product contract §7.
- *Maintain an Analyzer-side rollup table updated by a notification handler*: rejected — Spec Assumption explicitly defers pre-computation. SC-002's 100k-pageview budget is acceptable with existing indexes per back-of-envelope: ~50ms full-scan on the composite index for typical row widths.
- *Query Customizer's published-content cache for visitor lists directly*: rejected — that cache holds a different shape (visitor profiles, not pageviews) and would require constructing the aggregate from scratch.

**Verification**: integration test `ContentAnalyticsRepositoryIntegrationTests` seeds 10k pageviews, runs the aggregation query, and asserts elapsed ms is under the SC-001 budget when invoked through `IScopeProvider`.

---

## R3 — Time-on-page derivation: window function vs self-join

**Question**: Spec Clarifications §2 fixed the derivation rule: each pageview's duration = `requestUtc` delta to the next pageview in the same session, last pageview excluded. T-SQL has multiple ways to express this. Which is canonical for our codebase?

**Decision**: Use `LAG(requestUtc) OVER (PARTITION BY analyzerSession.sessionKey ORDER BY requestUtc)` to compute the predecessor `requestUtc`, then `DATEDIFF(SECOND, lag, requestUtc)` to get duration. The pageview where `lag IS NULL` (the first pageview in the session) is included in the computation but credited to the *next* pageview's duration; the *last* pageview in the session is implicitly excluded because no subsequent row exists to compute its delta against. Implemented as a CTE: the outer query then `AVG()`s the duration column for the target `contentKey`.

**Rationale**:
- Window functions are native T-SQL since SQL Server 2012; the host's Umbraco target (`Microsoft.Data.SqlClient` 6.x against MSSQL 2022) supports them comfortably.
- Single-pass query: no self-join, no nested correlated subquery — keeps `Principle VIII` (no N+1) clean.
- The CTE compiles to a single optimised plan; the `PARTITION BY sessionKey` ensures durations are bounded to within-session deltas (cross-session bleed is impossible by construction).
- "Last pageview excluded" falls out naturally: a session's terminal pageview has no successor and is the one whose lag is computed *for the next-non-existent row* — i.e. never used.

**Alternatives considered**:
- *Self-join on `sessionKey` with `MIN(requestUtc) WHERE requestUtc > current.requestUtc`*: rejected — quadratic in session length, easily 5-10x slower on large sessions per anecdotal benchmarks. Window functions are the modern answer.
- *Compute the average client-side from a per-pageview duration column maintained by a notification handler*: rejected — would require a new persistence path (touches `Principle IV`'s cascade-step gate) and goes against the slice's "no new capture surface" framing.
- *Approximate as session duration / pageview count*: rejected — Spec Clarifications §2 chose the conventional web-analytics approach, not this rough estimate.

**Pseudo-SQL** (final form lands in `ContentAnalyticsRepository`):

```sql
WITH SessionPageviews AS (
    SELECT
        pv.contentKey,
        pv.requestUtc,
        pv.visitorProfileFk,
        LAG(pv.requestUtc) OVER (
            PARTITION BY s.sessionKey
            ORDER BY pv.requestUtc
        ) AS prevRequestUtc
    FROM customizerVisitorPageview pv
    JOIN analyzerSession s ON s.visitorProfileKey = (
        SELECT vp.[key] FROM customizerVisitorProfile vp WHERE vp.id = pv.visitorProfileFk
    )
    WHERE pv.requestUtc >= @windowStart30d
)
SELECT
    COUNT(CASE WHEN requestUtc >= @windowStart24h THEN 1 END) AS pageviews24h,
    COUNT(CASE WHEN requestUtc >= @windowStart7d  THEN 1 END) AS pageviews7d,
    COUNT(*) AS pageviews30d,
    COUNT(DISTINCT visitorProfileFk) AS uniqueVisitors30d,
    AVG(CAST(DATEDIFF(SECOND, prevRequestUtc, requestUtc) AS BIGINT))
        AS avgTimeOnPageSeconds30d  -- nulls where prevRequestUtc is null skip the AVG naturally
FROM SessionPageviews
WHERE contentKey = @contentKey
```

(The join through `customizerVisitorProfile.id` ↔ `customizerVisitorProfile.key` ↔ `analyzerSession.visitorProfileKey` is awkward because Customizer uses an `int id` PK while Analyzer's session table FKs the `Guid key`. R3 follow-up in tasks captures this; the integration test fixture confirms the join produces correct results.)

---

## R4 — `IPublishedContentCache.GetById` semantics for tombstoned content

**Question**: When a content node is moved to the Umbraco recycle bin / unpublished, does `IPublishedContentCache.GetById(Guid)` return null, or return the node with `IsPublished = false`?

**Decision**: `IPublishedContentCache.GetById(Guid)` returns `null` for content that is currently unpublished or in the recycle bin. To populate `isContentCurrentlyTombstoned` per Spec Clarifications §3 + `FR-RPT-012`, the implementation calls `_publishedContentCache.GetById(contentKey) == null` and returns true when null.

**Caveat**: this returns null also for content that has *never existed* (e.g. random GUIDs). The endpoint's 404 path (`FR-RPT-011`) handles that case **before** the tombstone-probe is invoked: if the content was never seen in any capture table AND `GetById` returns null, the response is 404. If at least one capture row exists for the GUID but `GetById` is null, the content has been deleted — the response is 200 with `isContentCurrentlyTombstoned = true` and the historical aggregates.

**Rationale**:
- Single source of truth for "current published state" — using the published cache directly avoids round-tripping through `IContentService.GetById` (which has different semantics — returns drafts/trashed too).
- O(1) in-memory lookup — no extra DB hit.

**Alternatives considered**:
- *`IContentService.GetById` + `Trashed` boolean*: rejected — `IContentService` is a slower path, and returns content in the recycle bin as non-null. Mapping `Trashed` to `isContentCurrentlyTombstoned` is doable but introduces an extra dependency where the published cache already does the job in fewer lines.
- *Use Customizer's historical `customizerVisitorPageview.wasContentTombstoned` flag*: explicitly rejected by Spec Clarifications §3 (chose current-state semantic, not historical).

**Verification**: unit test `PublishedContentTombstoneProbeTests` mocks `IPublishedContentCache.GetById` returning null vs a populated `IPublishedContent` and asserts the boolean output.

---

## R5 — Skeleton + `aria-busy` conventions in `@umbraco-cms/backoffice` content apps

**Question**: Does `@umbraco-cms/backoffice` 17.3.5 ship a primitive skeleton element, or do we roll our own with plain CSS?

**Decision**: Roll our own minimal Lit element `skeleton.element.ts` using `<div class="skeleton-block"></div>` styled with a shimmer animation. `aria-busy="true"` is set on the parent container (the content-app's root element). When the request resolves, the parent's `aria-busy` flips to `false` and the skeleton elements unmount in a single re-render.

**Rationale**:
- `@umbraco-cms/backoffice` 17.3.5 ships UI primitives like `uui-card`, `uui-button`, `uui-icon` but no first-class `uui-skeleton`. Filing an issue upstream is out of scope for this slice.
- A 20-line custom Lit element with a CSS shimmer (`@keyframes shimmer-pulse`) satisfies `FR-RPT-013` and Spec Clarifications §5 without dependencies.
- `aria-busy="true"` on the container is the WCAG-recommended affordance for "this region is loading"; screen readers announce the change of state when the attribute flips.

**Alternatives considered**:
- *Wait for upstream `uui-skeleton`*: rejected — Spec Clarifications §5 chose this UX pattern explicitly; the slice can't depend on Umbraco's roadmap.
- *Centered spinner using `uui-loader`*: rejected by Spec Clarifications §5 (spinner-stuck-forever feel was the reason US2 was worded the way it was).
- *No loading state at all (render zeros, then swap)*: rejected by Spec Clarifications §5 (collides with empty-state semantic).

**Verification**: Vitest test `content-app.element.spec.ts` asserts both states: (a) before fetch resolves, container has `aria-busy="true"` and skeleton element is present; (b) after fetch resolves, container has `aria-busy="false"` and skeleton is absent. Tests use `vi.fn()` to control the repository's promise resolution.

---

## Cross-cutting note: Validation strategy

Per Spec Assumption + slice 007's deferral pattern, the canonical validation surface for this slice is **automated tests** (xUnit unit + Testcontainers integration + Vitest jsdom). Manual quickstart is blocked by [#34](https://github.com/d0helmy/analyzer/issues/34) and [#33](https://github.com/d0helmy/analyzer/issues/33). The `quickstart.md` document for this slice still ships (per the speckit workflow) but is explicitly marked as "deferred — requires #34" at the top. It serves as the future smoke-test runbook once the EntraID shim lands.
