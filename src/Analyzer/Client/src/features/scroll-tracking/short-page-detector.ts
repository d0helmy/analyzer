// Analyzer slice-006 — short-page detection.
//
// Research §R3 — when the rendered page is no taller than the
// viewport (`scrollableHeight <= 0`), the visitor cannot scroll to
// trigger the 25 / 50 / 75 milestones. Emit the 100 % milestone on
// page-ready (the visitor saw the whole content because the whole
// content IS the viewport) and pre-mark every bucket as crossed so
// any subsequent rAF measurement no-ops. The 25 / 50 / 75 rows are
// deliberately skipped — the heatmap denominator equals pageviews,
// and emitting all four would over-count short-page engagement vs
// tall-page where the visitor only reached 50 %.

/**
 * `true` when there is no scrollable distance — the rendered body is
 * shorter than (or equal to) the viewport height. The caller should
 * dispatch the 100 % milestone and mark every bucket as crossed
 * without installing a scroll listener.
 */
export function isShortPage(doc: Document = document, win: Window = window): boolean {
  const scrollableHeight =
    doc.documentElement.scrollHeight - win.innerHeight;
  return scrollableHeight <= 0;
}
