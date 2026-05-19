// Shared `analyzer-no-tracking` opt-out predicate. Introduced by
// slice 005 (forms-tracking US3, scoped to form / field elements);
// extracted to `shared/` by slice 006 so the scroll-tracking module
// can consume the document-level variant without duplicating the
// attribute name.
//
// Two predicates:
//
//   - `isElementOptedOut(el)` — checks a specific element. Used by
//     slice-005 forms-tracking for per-form / per-field opt-out
//     (presence on `<form analyzer-no-tracking>` excludes the entire
//     form; presence on `<input analyzer-no-tracking>` excludes one
//     field while leaving form-level lifecycle events untouched).
//
//   - `isDocumentOptedOut(doc)` — checks `<html>` and `<body>` for
//     the attribute. Used by slice-006 scroll-tracking where the
//     opt-out semantic is pageview-wide (the scroll observer attaches
//     once per pageview; no per-element analogue).
//
// Both predicates are presence-only: any value (including empty
// string) means "skip tracking". Read at handler-init time; dynamic
// attribute changes after the observer is attached do NOT
// retroactively short-circuit (documented assumption — slice 005
// US3 AS2 and slice 006 US2 AS2).

export const ANALYZER_NO_TRACKING_ATTRIBUTE = "analyzer-no-tracking";

export function isElementOptedOut(el: Element): boolean {
  return el.hasAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE);
}

export function isDocumentOptedOut(doc: Document = document): boolean {
  const root = doc.documentElement;
  if (root !== null && root.hasAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE)) {
    return true;
  }
  const body = doc.body;
  if (body !== null && body.hasAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE)) {
    return true;
  }
  return false;
}
