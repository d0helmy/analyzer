// Analyzer slice-006 scroll-tracking module entrypoint.
//
// Wires the per-pageview scroll observer at `DOMContentLoaded`. Reads
// `window.analyzer.pageviewKey` + `window.analyzer.contentKey` which
// the Razor view-component (or headless equivalent) populates from
// `IAnalyticsStateProvider.CurrentRequest` during the original page
// render (research §R6).
//
// US2: at init time, checks `analyzer-no-tracking` on `<html>` or
// `<body>` via the shared predicate. Presence short-circuits — no
// scroll listener installed, no fetch fired. Dynamic attribute
// changes after init do NOT retroactively stop in-flight capture
// (spec US2 AS2 — documented v1 behaviour).

import { isDocumentOptedOut } from "../../shared/opt-out-attribute";
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
  // US2 opt-out — read at init time only; documented v1 behaviour.
  if (isDocumentOptedOut()) {
    return;
  }
  const globalAny = globalThis as Record<string, unknown>;
  const ns = globalAny.analyzer as AnalyzerScrollGlobals | undefined;
  const pageviewKey = ns?.pageviewKey ?? "";
  const contentKey = ns?.contentKey ?? "";
  startScrollObserver({ pageviewKey, contentKey });
}
