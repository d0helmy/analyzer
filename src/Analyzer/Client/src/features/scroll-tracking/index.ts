// Analyzer slice-006 scroll-tracking module entrypoint.
//
// Wires the per-pageview scroll observer at `DOMContentLoaded`. Reads
// `window.analyzer.pageviewKey` + `window.analyzer.contentKey` which
// the Razor view-component (or headless equivalent) populates from
// `IAnalyticsStateProvider.CurrentRequest` during the original page
// render (research §R6).
//
// US2 will layer the `analyzer-no-tracking` opt-out short-circuit
// on top of this entry point (slice-005 attribute, shared predicate
// extracted in T039-T041).

import { startScrollObserver } from "./scroll-observer";

interface AnalyzerScrollGlobals {
  pageviewKey?: string;
  contentKey?: string;
}

export function initialiseScrollTracking(): void {
  if (typeof document === "undefined" || typeof window === "undefined") {
    return;
  }
  if (document.readyState === "loading") {
    document.addEventListener(
      "DOMContentLoaded",
      () => resolveAndStart(),
      { once: true },
    );
  } else {
    resolveAndStart();
  }
}

function resolveAndStart(): void {
  const globalAny = globalThis as Record<string, unknown>;
  const ns = globalAny.analyzer as AnalyzerScrollGlobals | undefined;
  const pageviewKey = ns?.pageviewKey ?? "";
  const contentKey = ns?.contentKey ?? "";
  startScrollObserver({ pageviewKey, contentKey });
}
