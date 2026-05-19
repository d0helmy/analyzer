# Phase 0 Research: Scroll Tracking

**Slice**: 006-scroll-tracking
**Date**: 2026-05-19
**Status**: Complete — both `[NEEDS CLARIFICATION]` candidates (bucket strategy, short-page handling) resolved inline in spec.md Assumptions. This document records the supporting design decisions for the Phase 1 design surface.

## R1 — Client-side scroll-position observation strategy

**Decision**: A single passive `window.addEventListener('scroll', ..., { passive: true })` listener installed at `DOMContentLoaded`, with the actual measurement deferred to `requestAnimationFrame` (rAF). Each scroll burst schedules at most one rAF callback (re-entrant scroll fires while a callback is queued no-op); on rAF fire, we read `window.scrollY` + `document.documentElement.scrollHeight - innerHeight` once, derive the percentage, and let the milestone tracker (R2) decide whether to dispatch.

**Rationale**:
- `passive: true` is mandatory on a scroll listener attached to `window` — otherwise the browser's scroll thread is blocked and the page can hitch under finger-drag on mobile. Even though we do not call `preventDefault`, marking it passive avoids the browser pessimising the listener.
- The rAF gate gives us implicit throttling: scroll events on modern browsers fire at the input-event rate (~60-120 Hz on desktop, often higher on mobile), and we want at most one measurement per frame. Throttling via `setTimeout(0)` is unreliable (queued behind layout); throttling via `setTimeout(N)` adds visible delay.
- Reading `scrollHeight` inside rAF avoids forced sync layout — by rAF time the browser has already laid out for the frame.
- The FR-004 debounce ("≤ 1 POST per 100 ms scroll window") is *automatically* satisfied by the per-bucket fire-once flag (R2) — the moment a milestone is crossed it can never re-fire, so back-and-forth scrolling produces zero additional POSTs.

**Alternatives considered**:
- **`IntersectionObserver` on sentinel elements at 25/50/75/100 % positions** — clean conceptually but requires injecting four DOM elements into the host's content; either invasive (modifies host markup) or fragile (positions need recalculation on every layout change). Rejected: cleaner-looking but more brittle.
- **`scroll-snap` + `scrollend` event** — `scrollend` is well-supported in 2026 but only fires when the user stops scrolling; we need *crossing* detection, not *settled-position* detection. Rejected as semantically wrong.
- **`setInterval(measure, 100)`** — would work but wastes CPU on stationary pages and still requires the per-bucket flag to dedupe. Rejected: rAF is strictly better.

## R2 — Bucket-crossing detection algorithm

**Decision**: Per-page state shape is `{ crossedBuckets: Set<25|50|75|100>, maxObservedPercent: number }`. On each measurement, compute `percent = Math.floor((scrollY / scrollableHeight) * 100)`; for each bucket `b ∈ [25, 50, 75, 100]`, if `percent >= b && !crossedBuckets.has(b)` then add to set, dispatch event. This guarantees:

1. **Idempotency** — once a bucket is in the set, it never fires again for this pageview (FR-003).
2. **Monotonic** — we do not require `percent` to monotonically increase; if the user scrolls top → 80 % → top → 80 %, buckets 25/50/75 fire on the first pass (in a single rAF if the user jumps via Home/End keys), and the second 80 % is a no-op.
3. **Crossing semantics, not threshold semantics** — "crossing" means `percent >= b`, not `previous < b && current >= b`. This is simpler and tolerant of a missed measurement (e.g. a jumpy scroll skipping past 50 % straight to 60 % still fires the 50 % milestone on the first measurement above 50 %).

**Rationale**:
- A `Set<number>` is O(1) for membership; max-four-bucket scan is constant-time.
- "Crossing semantics" rather than "delta semantics" tolerates measurement gaps without false negatives — a rAF gate can drop measurements, and we still capture every milestone correctly.
- Per-pageview state lives in a closure scoped to the scroll module's init; on SPA route change (out of scope v1) the state would leak — handled by the SPA-out-of-scope assumption.

**Alternatives considered**:
- **Boolean array `[boolean, boolean, boolean, boolean]`** — equally fine; `Set<number>` reads slightly more clearly in TypeScript.
- **`previousPercent` + delta check** — requires keeping previous state correctly across rAF, more error-prone, no benefit.

## R3 — Short-page handling

**Decision** (resolves spec Assumption "Short-page handling"): On `DOMContentLoaded`, compute `scrollableHeight = document.documentElement.scrollHeight - innerHeight`. If `scrollableHeight <= 0` (page shorter than viewport), the milestone tracker immediately dispatches the **100 % milestone** and adds buckets {25, 50, 75, 100} to the crossed set (so any future scroll attempts no-op). Buckets 25/50/75 are explicitly **not** emitted as rows — the heatmap denominator equals pageviews, and the 100 % row signals "this visitor saw the whole content".

**Rationale**:
- Pages shorter than viewport are common on dashboard-style intranet content (e.g. an org-chart panel). If we emitted nothing, the eventual heatmap would understate engagement on short pages; if we emitted all four milestones, the heatmap would overstate engagement vs. tall pages where the visitor scrolled to the bottom.
- The 100 %-only emission is the minimum signal that distinguishes "visitor saw the page" from "visitor opened the page in a background tab" — useful for the eventual hidden-tab edge-case discrimination.
- Adding all four to the `crossedBuckets` set on the short-page path prevents a late layout shift (e.g. an image loaded after `DOMContentLoaded` extends the page) from re-triggering buckets that no longer apply to this visitor's experience.

**Alternatives considered**:
- **Emit nothing on short pages** — denominator inconsistency in the eventual heatmap; rejected.
- **Emit all four buckets on short pages** — overstates engagement vs. tall-page visitors who only reached 50 %; rejected.
- **Re-measure `scrollableHeight` on resize / load events** — necessary, but the short-page-emission decision is made once at first measurement; if a late-loaded image extends the page from "short" to "tall", we accept that bucket 25/50/75 will be missed for that single visit (acceptable corner case; documented as edge case in spec).

## R4 — Opt-out attribute reuse from slice 005

**Decision**: The slice-005 `analyzer-no-tracking` opt-out detector is extracted (during slice-005 polish or beginning of slice 006) into a shared utility — `Client/src/shared/is-opted-out.ts` — that returns `true` if the attribute is present on `<html>`, `<body>`, or `document.documentElement` (the scroll root). The scroll module imports it at handler-init time and short-circuits before installing the scroll listener if it returns `true`.

**Rationale**:
- Slice 005 introduced the attribute in `Client/src/features/forms-tracking/opt-out-attribute.ts`; rather than duplicate the lookup logic, this slice extracts it once and both modules consume the shared predicate.
- Reading at init-time only matches the slice-005 contract; dynamic attribute changes after init do not retroactively stop in-flight capture (documented as US2 acceptance scenario 2 in spec).
- The extraction is mechanical: rename the function, move the file, update slice-005 imports. Zero behaviour change for slice 005's existing tests.

**Alternatives considered**:
- **Per-module opt-out attribute** (e.g. `analyzer-no-scroll-tracking`) — adds operator confusion (which attribute do I use? all of them?). Rejected: one knob is simpler.
- **Re-implement the predicate locally** — code duplication for no benefit. Rejected.
- **Push the predicate into a runtime config object** — over-engineered for one boolean. Rejected.

## R5 — Single-table persistence shape + unique-index idempotency

**Decision**: One table `analyzerScrollSample` with the column shape from spec Key Entities (`id`, `eventKey`, `visitorProfileKey`, `sessionKey`, `pageviewKey`, `contentKey`, `bucket`, `receivedUtc`). The idempotency invariant is enforced by a **unique non-clustered index** on `(pageviewKey, bucket)`:

```sql
CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerScrollSample_pageviewBucket]
  ON [analyzerScrollSample] ([pageviewKey] ASC, [bucket] ASC);
```

When a duplicate insert hits the unique index, NPoco raises `SqlException` with `Number IN (2601, 2627)`; the slice-003 `UniqueConstraintViolationDetector` already discriminates this case from generic SQL failures. The handler maps to HTTP 409 + an audit-log entry tagged `Duplicate`. Zero application-level locking required.

Supporting indexes:
- PK: `id` (uniqueidentifier, non-autoincrement) — matches slices 002/004/005.
- UX: `eventKey` (uniqueidentifier) — public-surface identity collision detector.
- UX: `(pageviewKey, bucket)` — idempotency invariant.
- IX: `visitorProfileKey` — powers the cascade-step delete (SC-004 200 ms / 1 000 rows budget).
- IX: `receivedUtc` — supports time-range reports in the eventual read-side slice.

**Rationale**:
- Two-table split (slice-005's pattern) is not justified here: there is only one event type, no nullable-column-by-event-type pressure, no index-family contention.
- Unique-index enforcement at the database layer is the correct place for an invariant that must hold even if a buggy client emits twice; FR-003 explicitly requires this defence-in-depth.
- Client-supplied `id` lets the same row land safely on retry (the second POST hits the unique index, returns 409, no duplicate created).

**Alternatives considered**:
- **`INSERT ... WHERE NOT EXISTS`** — works but trades a unique-index for a row-scan-with-lock; rejected.
- **Application-level lock per pageview** — multi-instance hosts cannot rely on in-process locks; rejected.
- **Stored procedure with `MERGE`** — adds DB-side complexity for no observable benefit; rejected.

## R6 — Pageview-key resolution flow

**Decision**: The client payload carries the `pageviewKey` resolved server-side from the same request that delivered the page (via Customizer's `IAnalyticsStateProvider.CurrentRequest.PageviewKey`). Two paths:

1. **Razor-rendered page**: a `<meta name="analyzer-pageview-key" content="..." />` tag emitted by a tiny Analyzer view-component (or — if simpler — a `window.analyzer.pageviewKey = '...'` inline script) carries the key into the client bundle.
2. **Headless-rendered page**: the host frontend obtains the key from a deterministic Customizer endpoint (already exposed in slice-003), then injects it into the same global the bundle reads from.

If the bundle cannot resolve a `pageviewKey` at init, it skips capture for this pageview (Edge Case: "Pageview unresolvable at capture time"). The server endpoint additionally rejects POSTs with `pageviewKey == Guid.Empty` (401/403 if also unauthenticated, 400 otherwise; FR-008).

**Rationale**:
- Carrying the key client-side avoids a round-trip on every milestone (which would itself defeat fire-and-forget capture).
- Headless-compat requires a single shared shape — the bundle reads from `window.analyzer.pageviewKey`; the host populates it. Razor-render emits the script; headless host code emits the script.
- The meta-tag option is fully compatible with strict CSP that blocks inline scripts; the inline-script option is simpler when CSP is permissive. Plan suggests inline; sample host renders both for migration ease.

**Alternatives considered**:
- **Server-side cookie containing the key** — adds cookie surface, defeats the cookie-less posture; rejected.
- **Per-POST round-trip to resolve the key** — defeats fire-and-forget; rejected.

## R7 — Management endpoint route + Principle-VII gate

**Decision**: One endpoint, mirroring the slice-005 lifecycle controller exactly:

- Route: `POST /umbraco/management/api/v1/analyzer/scroll-event/milestone`
- Auth: `[Authorize(AuthenticationSchemes = "UmbracoBackofficeAuth")]` (same scheme as slices 004/005).
- Anti-forgery: `[ValidateAntiForgeryToken]` (or the management API's anti-forgery filter — confirm at impl time).
- Payload validation: `AnalyzerScrollEventPayload` record with `[Required]` annotations + range checks on `bucket` ∈ {25, 50, 75, 100}.
- Audit: every accepted call writes one structured log entry tagged `AnalyzerScrollEventCaptured` carrying `EventKey`, `PageviewKey`, `Bucket`, `ActorUpn`, `ReceivedUtc` (SC-007).
- Response: HTTP 202 with `{ eventKey }` body on success; 401/403 on identity gate fail; 400 on payload validation fail; 409 on unique-index duplicate; 500 on unexpected.

**Rationale**:
- Mirrors the slice-005 `/form-event/lifecycle` route convention exactly; operators only learn one URL pattern.
- The four-corner gate is the project's house style for any state-changing management endpoint; deviating would be a Principle-VII regression.

**Alternatives considered**:
- **Batch-POST endpoint** (multiple milestones per request) — needless v1 complexity at 200 events/min throughput. Rejected.

## R8 — Audit-log payload shape

**Decision**: One `AnalyzerScrollEventAuditor` (parallel to slice 005's `AnalyzerFormEventAuditor`) emitted via Microsoft.Extensions.Logging structured logging:

```text
LogInformation(
  "AnalyzerScrollEventCaptured EventKey={EventKey} PageviewKey={PageviewKey} Bucket={Bucket} ActorUpn={ActorUpn} ReceivedUtc={ReceivedUtc:o}",
  eventKey, pageviewKey, bucket, actorUpn, receivedUtc);
```

The auditor is invoked from the management controller only on a successful insert (after the repo returns the inserted DTO). Failures (401/403/400/409/500) are NOT audit-logged at this level — the auth/auth-z layer and the unique-index detector already log; double-logging is noise.

**Rationale**: parity with slice-005's auditor + Customizer's audit conventions. Structured fields are queryable in any log-shipper.

## R9 — Public-surface pinning diff

**Decision**: Additive only. Pinning baseline regenerated to include:

- New record `Analyzer.Analytics.AnalyticsScrollSample` (immutable, `init`-only props, `byte`-backed `bucket` enum).
- New enum `Analyzer.Analytics.AnalyzerScrollBucket` (byte-backed; `Quarter=25`, `Half=50`, `ThreeQuarters=75`, `Full=100`).
- New member `IAnalyticsEventStateProvider.CurrentRequestScrollEvents : IReadOnlyList<AnalyticsScrollSample>`.

No existing members removed or renamed. The pinning test compares against the regenerated baseline; the diff is auto-approved by the `Analyzer.Tests.PublicSurfacePinningTests` baseline-regeneration helper (same pattern as slices 002-005).

**Rationale**: Principle X requires that breaking changes only happen on MAJOR releases; this slice is MINOR (additive), so the diff is fine.

## R10 — No Customizer-side change verification

**Verified**:
1. **Cascade-step discovery**: Customizer's `AnonymizationOrchestrator` (slice 002 onward) discovers `IAnonymizationCascadeStep` implementations via DI scan — no central registry, no Customizer source change. ✅
2. **Pageview FK semantics**: Inter-product contract §3 D9 explicitly names `analyzerScrollSample` as an Analyzer-owned table that FKs to `customizerPageview.Key`. The FK is declared via raw SQL in the migration (per slice-002 precedent — Principle III: do not import `Customizer.Features.Pageview.Persistence.CustomerPageviewDto`). ✅
3. **PageviewKey reading**: Customizer's `IAnalyticsStateProvider.CurrentRequest.PageviewKey` is the pinned read contract; no Customizer-side change needed to expose it. ✅
4. **Visitor identity**: `IVisitorIdentifier` (Analyzer-owned, slice 002) handles `oid`/`upn` resolution; no Customizer-side change. ✅

Zero Customizer-side change required. Spec assertion confirmed.
