// Analyzer slice-006 US1 — per-pageview scroll-depth observer.
//
// Attaches one passive scroll listener at `DOMContentLoaded` and
// coalesces scroll bursts via `requestAnimationFrame` (research
// §R1). On each rAF tick, measures `window.scrollY` against
// `documentElement.scrollHeight - innerHeight`, derives the depth
// percentage, and dispatches one milestone POST per newly-crossed
// bucket.
//
// State is closure-scoped to a single pageview — the bundle is
// re-initialised on hard navigations. SPA route changes that re-
// render content without a fresh pageview are out of scope for v1
// (matches slice-005 "Forms via Ajax" exclusion).

import {
  dispatchMilestone,
  ScrollBucket,
} from "./scroll-event-dispatcher";
import { detectNewlyCrossed, allBuckets } from "./milestone-tracker";
import { isShortPage } from "./short-page-detector";

export interface ScrollObserverOptions {
  /** Customizer pageview key — carried in every milestone POST. */
  pageviewKey: string;
  /** Umbraco content-node key for the pageview. */
  contentKey: string;
}

/**
 * Start observing scroll depth for this pageview. Should be called
 * once at `DOMContentLoaded`. Idempotent guard not enforced here —
 * the caller (`index.ts`) gates re-invocation via a flag on the
 * document.
 */
export function startScrollObserver(opts: ScrollObserverOptions): void {
  if (typeof window === "undefined" || typeof document === "undefined") {
    return;
  }
  if (opts.pageviewKey.length === 0 || opts.contentKey.length === 0) {
    // By-design skip when mounted in contexts (e.g. Umbraco backoffice)
    // that don't emit a pageview — debug-level so it doesn't surface in
    // the default browser console.
    console.debug(
      "[analyzer] scroll-tracking init skipped — missing pageviewKey or contentKey",
    );
    return;
  }

  const crossed = new Set<ScrollBucket>();

  // Short-page path — emit Full on page-ready, mark every bucket as
  // crossed so any subsequent rAF measurement no-ops, do NOT install
  // the scroll listener (no scrollable distance to observe).
  if (isShortPage()) {
    for (const bucket of allBuckets()) {
      crossed.add(bucket);
    }
    void dispatchMilestone({
      pageviewKey: opts.pageviewKey,
      contentKey: opts.contentKey,
      bucket: ScrollBucket.Full,
    });
    return;
  }

  let rafQueued = false;

  const measure = (): void => {
    rafQueued = false;
    const scrollableHeight =
      document.documentElement.scrollHeight - window.innerHeight;
    if (scrollableHeight <= 0) {
      return;
    }
    const percent = Math.floor((window.scrollY / scrollableHeight) * 100);
    const newly = detectNewlyCrossed(percent, crossed);
    for (const bucket of newly) {
      void dispatchMilestone({
        pageviewKey: opts.pageviewKey,
        contentKey: opts.contentKey,
        bucket,
      });
    }
  };

  const onScroll = (): void => {
    if (rafQueued) {
      return;
    }
    rafQueued = true;
    window.requestAnimationFrame(measure);
  };

  window.addEventListener("scroll", onScroll, { passive: true });

  // Eager measurement once at attach — covers the "page-load
  // scrollY != 0" anchor-link landing case (visitor lands on an
  // in-page anchor halfway down the article).
  window.requestAnimationFrame(measure);
}
