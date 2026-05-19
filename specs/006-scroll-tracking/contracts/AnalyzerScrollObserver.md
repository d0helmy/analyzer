# Contract — `AnalyzerScrollObserver` (client-side bundle module)

**Feature**: `006-scroll-tracking`
**Date**: 2026-05-19
**Stability**: internal to the Analyzer bundle (not exported on `window`; not part of the pinned public surface). Consumed by `analyzer-bundle.ts` only.

The client-side TypeScript module that observes scroll position on a Razor- or headless-rendered intranet page and dispatches scroll-milestone POSTs. Lives under `src/Analyzer/Client/src/features/scroll-tracking/`.

## Module entry point

```ts
// src/Analyzer/Client/src/features/scroll-tracking/index.ts
export function initScrollTracking(opts: {
  pageviewKey: string;          // Guid from window.analyzer.pageviewKey
  contentKey: string;           // Guid from window.analyzer.contentKey
  endpoint: string;             // POST URL (default: /umbraco/management/api/v1/analyzer/scroll-event/milestone)
  antiForgeryToken: string;     // backoffice anti-forgery token
}): void;
```

Called once at `DOMContentLoaded` by the bundle's entrypoint, after the shared opt-out predicate has been consulted. No return value — fire-and-forget setup.

## Init-time short-circuit

1. **Opt-out check**: invoke shared `isOptedOut()` predicate (R4). If true → return immediately; no listener installed, no state allocated.
2. **Page-key check**: if `opts.pageviewKey` is missing or `Guid.Empty` → log a console warning, return (Edge Case: "Pageview unresolvable at capture time").
3. **Short-page check** (R3): compute `scrollableHeight = document.documentElement.scrollHeight - innerHeight`. If `<= 0`, dispatch the `Full` (100 %) milestone immediately, mark all four buckets as crossed, return (no listener installed — the page is fully visible already).

## Listener installation

```ts
let rafQueued = false;
const crossed = new Set<ScrollBucket>();

window.addEventListener('scroll', () => {
  if (rafQueued) return;
  rafQueued = true;
  requestAnimationFrame(measure);
}, { passive: true });
```

The `measure` function reads `window.scrollY` and `document.documentElement.scrollHeight - innerHeight`, derives `percent`, scans the four-bucket array, dispatches any newly-crossed bucket. The `crossed` Set is closure-scoped so each pageview has its own state.

## Dispatch contract

For each newly-crossed bucket:

```ts
fetch(opts.endpoint, {
  method: 'POST',
  credentials: 'same-origin',
  headers: {
    'Content-Type': 'application/json',
    'X-Anti-Forgery-Token': opts.antiForgeryToken,
  },
  body: JSON.stringify({
    pageviewKey: opts.pageviewKey,
    contentKey: opts.contentKey,
    bucket: bucketValue, // 25 | 50 | 75 | 100
  }),
  keepalive: true, // allow the request to survive page unload
});
```

- **Fire-and-forget**: no `await`; the dispatcher ignores the response. Network failures, 4xx, 5xx are silent on the client (FR-002 acceptance: client-side dispatch does not block scrolling).
- **`keepalive: true`**: ensures the POST survives a fast page unload (visitor scrolls + immediately clicks a link).
- **Anti-forgery header**: injected from the cookie populated by the Razor view-component or headless host code (same mechanism used by slices 004 / 005).

## State shape

```ts
type ScrollBucket = 25 | 50 | 75 | 100;

type ObserverState = {
  crossed: Set<ScrollBucket>;
  rafQueued: boolean;
  pageviewKey: string;
  contentKey: string;
};
```

One `ObserverState` per pageview, scoped to the module's `initScrollTracking` closure. SPA route changes that re-render content without a fresh pageview are out of scope (documented assumption); on a hard navigation the bundle re-initialises with a new state.

## Conformance tests (Vitest)

Under `src/Analyzer/Client/src/features/scroll-tracking/*.test.ts`:

- **opt-out**: when `isOptedOut()` returns true, no listener is attached and no fetch is fired even after a simulated scroll-through.
- **short-page**: when `scrollHeight === innerHeight`, exactly one fetch is fired on init with `bucket: 100`; subsequent simulated scroll events fire zero additional fetches.
- **milestone-crossing**: simulate scroll positions [10 %, 30 %, 55 %, 90 %, 50 %, 100 %] → exactly four fetches in order (25, 50, 75, 100), and no fetch on the back-scroll to 50.
- **rAF coalescing**: 20 synthetic scroll events fired before a single `requestAnimationFrame` callback runs → exactly one measurement, one (or zero) fetch.
- **anti-forgery header**: every fetch carries the expected header value.

## Bundle impact

Module adds ~3-4 KB minified to the existing `analyzer.js` bundle (per slice-005 sizing precedent: forms-tracking was ~5 KB; scroll is structurally smaller). Bundle-size budget contribution to SC-006 (≤ 5 ms FCP overhead on a 5 000 px page) is well within envelope.
