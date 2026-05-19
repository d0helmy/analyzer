// Analyzer slice-005 US3 — `analyzer-no-tracking` opt-out attribute.
//
// Defence-in-depth: short-circuit before any POST is issued. The
// attribute is presence-only — any value (or no value) means "skip
// tracking" (per FR-007 / FR-008). Use cases:
//
//   - On `<form analyzer-no-tracking>`: the entire form is excluded;
//     no Impression / Start / Success / Field rows persisted.
//   - On a single `<input analyzer-no-tracking>`: only that field
//     is excluded; form-level lifecycle events still fire.

const OPT_OUT_ATTRIBUTE = "analyzer-no-tracking";

export function isFormOptedOut(form: HTMLFormElement): boolean {
  return form.hasAttribute(OPT_OUT_ATTRIBUTE);
}

export function isFieldOptedOut(field: HTMLElement): boolean {
  return field.hasAttribute(OPT_OUT_ATTRIBUTE);
}
