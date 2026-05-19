// Analyzer slice-005 US2 — per-field focus / unfocus observer.
//
// Walks every field within the form and listens for `focus` / `blur`
// in the capture phase. `hadValue` is derived client-side from
// `element.value.length > 0` (privacy invariant: field VALUES are
// never sent — only the boolean derived signal).
//
// Each form's field listener attaches at observe time AND on any
// `focusin` event that lands on a not-yet-instrumented descendant
// (covers dynamically-added inputs without resorting to
// `MutationObserver` — keeps the bundle lean).

import {
  FieldEventType,
  dispatchField,
} from "./form-event-dispatcher";

const FORM_KEY_ATTRIBUTE = "data-umbraco-form";
const FIELD_KEY_ATTRIBUTE = "data-umbraco-field";

/**
 * Attach field-event listeners to every Umbraco-Forms-rendered field
 * within the supplied form. Idempotent per element via a
 * `data-analyzer-field-observed` stamp.
 */
export function observeFieldsWithin(form: HTMLFormElement): void {
  const formKey = form.getAttribute(FORM_KEY_ATTRIBUTE);
  if (formKey === null) {
    return;
  }

  // Capture-phase focus/blur on the form itself catches every
  // descendant input/textarea/select including ones added after
  // DOMContentLoaded.
  form.addEventListener(
    "focus",
    (event) => {
      const target = event.target as HTMLElement | null;
      if (target === null) return;
      const fieldKey = resolveFieldKey(target);
      if (fieldKey === null) return;
      dispatchField({
        formKey,
        fieldKey,
        eventType: FieldEventType.FieldFocus,
        hadValue: null,
      });
    },
    { capture: true },
  );

  form.addEventListener(
    "blur",
    (event) => {
      const target = event.target as HTMLElement | null;
      if (target === null) return;
      const fieldKey = resolveFieldKey(target);
      if (fieldKey === null) return;
      const hadValue = readHasValue(target);
      dispatchField({
        formKey,
        fieldKey,
        eventType: FieldEventType.FieldUnfocus,
        hadValue,
      });
    },
    { capture: true },
  );
}

function resolveFieldKey(target: HTMLElement): string | null {
  // The Umbraco-Forms-rendered control may be the input itself or
  // a wrapper element (e.g. a fieldset containing radios). Walk up
  // until we hit the field attribute or the form root.
  let cursor: HTMLElement | null = target;
  while (cursor !== null && cursor.hasAttribute(FORM_KEY_ATTRIBUTE) === false) {
    const key = cursor.getAttribute(FIELD_KEY_ATTRIBUTE);
    if (key !== null) {
      return key;
    }
    cursor = cursor.parentElement;
  }
  return null;
}

function readHasValue(target: HTMLElement): boolean {
  // INPUT / TEXTAREA / SELECT all expose .value; checkboxes /
  // radios are special-cased via .checked.
  if (target instanceof HTMLInputElement) {
    if (target.type === "checkbox" || target.type === "radio") {
      return target.checked;
    }
    return target.value.length > 0;
  }
  if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement) {
    return target.value.length > 0;
  }
  return false;
}
