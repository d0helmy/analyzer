// Analyzer slice-006 — pure-function milestone-crossing detector.
//
// Research §R2 — "crossing semantics, not delta semantics". For a
// given scroll percentage, return every milestone bucket that the
// visitor has reached BUT not yet been recorded as crossing. Adds
// the newly-crossed buckets to the supplied `crossed` Set as a
// side-effect so the caller can pass the same Set on subsequent
// calls.
//
// Tolerant of:
//  - jumpy scrolls (Home/End keys, link-anchor jumps) that skip past
//    intermediate buckets — every bucket ≤ percent fires on the
//    first measurement above it
//  - back-and-forth scrolling — buckets already in the Set never
//    re-fire
//  - dropped measurements (rAF coalescing) — crossing semantics
//    means we don't need every single percent value
//
// This is the only place the bucket boundary values are pinned in the
// client bundle; the server enforces them at the database CHECK
// constraint level too.

import { ScrollBucket } from "./scroll-event-dispatcher";

const ALL_BUCKETS: readonly ScrollBucket[] = [
  ScrollBucket.Quarter,
  ScrollBucket.Half,
  ScrollBucket.ThreeQuarters,
  ScrollBucket.Full,
];

/**
 * Return every milestone bucket reached at the given depth that has
 * not yet been recorded in `crossed`. Mutates `crossed` to add each
 * returned bucket — callers MUST pass the same Set across calls for
 * the per-pageview idempotency invariant.
 */
export function detectNewlyCrossed(
  percent: number,
  crossed: Set<ScrollBucket>,
): ScrollBucket[] {
  const newly: ScrollBucket[] = [];
  for (const bucket of ALL_BUCKETS) {
    if (percent >= bucket && !crossed.has(bucket)) {
      crossed.add(bucket);
      newly.push(bucket);
    }
  }
  return newly;
}

/**
 * The complete set of milestone buckets — exposed so the short-page
 * detector can pre-mark every bucket as crossed before emitting the
 * 100 % row only (research §R3).
 */
export function allBuckets(): readonly ScrollBucket[] {
  return ALL_BUCKETS;
}
