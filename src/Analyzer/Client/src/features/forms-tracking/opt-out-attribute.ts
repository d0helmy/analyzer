// Analyzer slice-005 US3 — `analyzer-no-tracking` opt-out attribute,
// per-form / per-field variants. Slice 006 extracted the attribute
// name + element predicate into `../../shared/opt-out-attribute` so
// the scroll-tracking module can share the same vocabulary; this
// file remains the public API for slice-005's element-scoped checks
// (`isFormOptedOut`, `isFieldOptedOut`) so existing import sites
// continue to work without churn.

import { isElementOptedOut } from "../../shared/opt-out-attribute";

export function isFormOptedOut(form: HTMLFormElement): boolean {
  return isElementOptedOut(form);
}

export function isFieldOptedOut(field: HTMLElement): boolean {
  return isElementOptedOut(field);
}
