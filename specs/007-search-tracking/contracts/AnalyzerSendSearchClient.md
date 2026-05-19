# Contract — `AnalyzerSendSearchClient` (client-side bundle module)

**Feature**: `007-search-tracking`
**Date**: 2026-05-19
**Stability**: public on `window.analyzer` (the helper is exposed for host page-scripts to call). The internal dispatcher is not exported on `window` and is not part of the pinned public surface.

The client-side TypeScript module that exposes `window.analyzer.sendSearch(...)` and POSTs accepted search submissions to the management endpoint. Lives under `src/Analyzer/Client/src/features/search-tracking/`.

## Module entry point

```ts
// src/Analyzer/Client/src/features/search-tracking/index.ts
export function attachSendSearch(opts: {
  endpoint: string;             // POST URL (default: /umbraco/management/api/v1/analyzer/search-event)
  antiForgeryToken: () => string; // backoffice anti-forgery token getter (called per request)
}): void;
```

Called once at module-init time by `analyzer-bundle.ts`. Attaches `sendSearch` onto `window.analyzer`:

```ts
window.analyzer.sendSearch = async function(
  query: string,
  resultCount: number,
  options?: { pageviewKey?: string }
): Promise<{ eventKey: string } | { skipped: true }>;
```

## Per-call behavioural contract

1. **Opt-out check (per call)**: invoke shared `isOptedOut()` predicate (`Client/src/shared/opt-out-attribute.ts`, slice-006 shared). If true → resolve with `{ skipped: true }`; no fetch issued, no error thrown.
2. **Client-side validation**:
   - `query` is a non-empty string after `.trim()`; reject with `{ status: 400, message: "query required" }` otherwise (no fetch).
   - `query.length` ≤ 256 after trim; reject with `{ status: 400, message: "query too long" }` otherwise (no fetch).
   - `resultCount` is a finite non-negative integer; reject with `{ status: 400, message: "resultCount must be a non-negative integer" }` otherwise (no fetch).
3. **PageviewKey resolution**: prefer `options?.pageviewKey`; fall back to `window.analyzer.pageviewKey`; fall back to `<meta name="analyzer-pageview-key" content="...">`'s `content`. If all three are missing or `Guid.Empty`, reject with `{ status: 400, message: "pageviewKey unavailable" }` (no fetch).
4. **Dispatch**: POST to `opts.endpoint` (see "Dispatch contract" below).
5. **Resolution**: on HTTP 202 with `{ eventKey }` body, resolve with `{ eventKey }`. On any non-202 status, reject with `{ status, message }` where `message` is the response's `errors[0].detail` or a default string. **Failures are not retried** — fire-and-forget after the initial attempt.

## Dispatch contract

```ts
fetch(opts.endpoint, {
  method: 'POST',
  credentials: 'same-origin',
  headers: {
    'Content-Type': 'application/json',
    'X-Anti-Forgery-Token': opts.antiForgeryToken(),
  },
  body: JSON.stringify({
    pageviewKey: resolvedPageviewKey,
    query: query.trim(),
    resultCount,
  }),
  keepalive: true, // allow the request to survive page unload (e.g. user hits Enter then clicks first result)
});
```

- **`credentials: 'same-origin'`**: the backoffice authentication cookie + anti-forgery cookie travel with the request.
- **`X-Anti-Forgery-Token` header**: the token sourced via `opts.antiForgeryToken()` (Umbraco's management-API anti-forgery convention).
- **`keepalive: true`**: ensures the POST survives a fast page unload (the visitor hits Enter, sees results, and immediately clicks one — the POST has ~ms to complete).
- **Trimmed query**: client trims before sending. Server re-trims defensively (Principle VII — domain MUST NOT trust upstream validation).

## State shape

The module is stateless across calls. Each `sendSearch(...)` call is independent — no closure-scoped queue, no per-pageview state, no debouncer. Hosts that want per-keystroke debouncing wire their own debouncer around the helper (documented as a Spec assumption).

## Public surface diff

`window.analyzer.sendSearch` is added to the existing `window.analyzer` shape (alongside `window.analyzer.send` from slice 004 and `window.analyzer.pageviewKey` from slice 006). The TypeScript ambient declaration in the bundle's `globals.d.ts` gains:

```ts
declare global {
  interface AnalyzerGlobal {
    // ... existing members from slices 004 / 006 ...
    sendSearch: (
      query: string,
      resultCount: number,
      options?: { pageviewKey?: string }
    ) => Promise<{ eventKey: string } | { skipped: true }>;
  }
}
```

## Conformance tests (Vitest)

Under `src/Analyzer/Client/src/features/search-tracking/*.test.ts`:

- **opt-out**: when `isOptedOut()` returns true, `sendSearch(...)` resolves with `{ skipped: true }` and no `fetch` mock is invoked.
- **valid happy path**: `sendSearch("design system", 12)` triggers exactly one fetch with the expected URL, headers, and body; resolves with the server-returned `eventKey`.
- **trim**: `sendSearch("  hello  ", 3)` posts `query: "hello"` (trimmed before send).
- **empty-query rejection**: `sendSearch("", 5)` rejects with `{ status: 400, message: "query required" }`; no fetch fired.
- **whitespace-only rejection**: `sendSearch("   \n\t  ", 5)` rejects with `{ status: 400, message: "query required" }`; no fetch.
- **oversize rejection**: `sendSearch("x".repeat(257), 5)` rejects with `{ status: 400, message: "query too long" }`; no fetch.
- **negative-count rejection**: `sendSearch("hello", -1)` rejects with `{ status: 400, message: "resultCount must be a non-negative integer" }`; no fetch.
- **non-integer-count rejection**: `sendSearch("hello", 3.14)` rejects similarly; no fetch.
- **NaN/Infinity rejection**: `sendSearch("hello", Number.NaN)` rejects similarly; no fetch.
- **pageviewKey from options**: `sendSearch("hello", 1, { pageviewKey: "<guid>" })` posts that key.
- **pageviewKey from window global**: `window.analyzer.pageviewKey = "<guid>"; sendSearch("hello", 1)` posts that key.
- **pageviewKey from meta tag**: with `<meta name="analyzer-pageview-key" content="<guid>">` in the DOM and no window global, `sendSearch("hello", 1)` posts that key.
- **pageviewKey missing**: all three sources unavailable → rejects with `{ status: 400, message: "pageviewKey unavailable" }`; no fetch.
- **server 400**: mocked server returns 400 with `{ errors: [{ detail: "bad query" }] }`; `sendSearch(...)` rejects with `{ status: 400, message: "bad query" }`.
- **server 202**: mocked server returns 202 with `{ eventKey: "<guid>" }`; `sendSearch(...)` resolves with that body.
- **anti-forgery header**: every fetch carries `X-Anti-Forgery-Token` from `opts.antiForgeryToken()`.

## Bundle impact

Module adds ~2-3 KB minified to the existing `analyzer.js` bundle. Smaller than slice 006's scroll module (~3-4 KB) because there's no listener / rAF / per-pageview state machine — just the `sendSearch` function and validation helpers. No measurable FCP impact (the helper code is loaded but not executed until the host page script calls it).

## Interaction with other `window.analyzer` members

- Coexists with `window.analyzer.send(...)` (slice 004) — both helpers POST to different endpoints; no shared state.
- Reads `window.analyzer.pageviewKey` (slice 006) as a `pageviewKey` source.
- Does **not** depend on slice 006's scroll observer being initialised — the search helper is fully independent of scroll capture.
