// Analyzer slice-005 US1 — per-form lifecycle observer.
//
// Attaches at `DOMContentLoaded` to every `<form data-umbraco-form>`
// rendered by Umbraco Forms. Three signals:
//
//   - Impression: IntersectionObserver fires when ≥50% of the form
//     is visible. Disconnects after first fire (one Impression per
//     page lifecycle, per (visitor, form, session)).
//   - Start:      First `focus` on any field within the form.
//   - Success:    `submit` event (client-side; not gated on server
//                 acceptance — server-side rejection still records
//                 Success at the slice-005 granularity and the
//                 lifecycle stays in Start → Abandon if the server
//                 rejects; v1 limitation, see spec Assumptions).
//
// v1 scope: forms present at `DOMContentLoaded` only. SPA / Ajax-
// inserted forms are out of scope (deferred to v1.1; would require
// `MutationObserver` wiring).
//
// Each form observed remembers its impression / start timestamps so
// the dispatcher can populate elapsedMsFromImpression on Start +
// elapsedMsFromStart on Success.

import {
  FormEventType,
  dispatchLifecycle,
  type LifecyclePayload,
} from "./form-event-dispatcher";
import { observeFieldsWithin } from "./field-observer";
import { isFormOptedOut } from "./opt-out-attribute";

const FORM_KEY_ATTRIBUTE = "data-umbraco-form";
const CONTENT_KEY_ATTRIBUTE = "data-umbraco-form-page-id";

interface FormState {
  formKey: string;
  contentKey: string;
  impressionAt: number | null;
  startAt: number | null;
  startDispatched: boolean;
  successDispatched: boolean;
}

/**
 * Initialise per-form observers for every form already in the DOM.
 * Idempotent: a second invocation walks the same DOM and re-observes
 * any not-yet-observed forms (safe — observers stamp the element).
 */
export function initialiseFormObserver(root: ParentNode = document): void {
  if (typeof window === "undefined") {
    return;
  }
  const forms = root.querySelectorAll<HTMLFormElement>(
    `form[${FORM_KEY_ATTRIBUTE}]`,
  );
  forms.forEach((form) => observeForm(form));
}

function observeForm(form: HTMLFormElement): void {
  if (form.dataset.analyzerObserved === "1") {
    return;
  }
  // US3 — `analyzer-no-tracking` on the <form> element short-circuits
  // every observer attach. Defence-in-depth: zero rows, zero POSTs.
  if (isFormOptedOut(form)) {
    form.dataset.analyzerObserved = "1";
    return;
  }
  const formKey = form.getAttribute(FORM_KEY_ATTRIBUTE);
  const contentKey = form.getAttribute(CONTENT_KEY_ATTRIBUTE);
  if (formKey === null || contentKey === null) {
    return;
  }
  form.dataset.analyzerObserved = "1";

  const state: FormState = {
    formKey,
    contentKey,
    impressionAt: null,
    startAt: null,
    startDispatched: false,
    successDispatched: false,
  };

  // Impression — IntersectionObserver, 50% threshold.
  if (typeof IntersectionObserver !== "undefined") {
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting && state.impressionAt === null) {
            state.impressionAt = performance.now();
            dispatchLifecycle({
              formKey: state.formKey,
              contentKey: state.contentKey,
              eventType: FormEventType.Impression,
              elapsedMsFromImpression: null,
              elapsedMsFromStart: null,
            });
            io.disconnect();
            return;
          }
        }
      },
      { threshold: 0.5 },
    );
    io.observe(form);
  }

  // Start — first focus on any descendant field. Use capture phase
  // so dynamically-added inputs are still seen.
  form.addEventListener(
    "focus",
    () => {
      if (state.startDispatched) {
        return;
      }
      state.startDispatched = true;
      state.startAt = performance.now();
      const impressionAt = state.impressionAt;
      const elapsed = impressionAt === null
        ? 0
        : Math.max(0, Math.round(state.startAt - impressionAt));
      const payload: LifecyclePayload = {
        formKey: state.formKey,
        contentKey: state.contentKey,
        eventType: FormEventType.Start,
        elapsedMsFromImpression: elapsed,
      };
      dispatchLifecycle(payload);
    },
    { capture: true },
  );

  // US2 — per-field focus / unfocus listeners (capture phase on
  // the form so dynamically-added inputs are still seen).
  observeFieldsWithin(form);

  // Success — submit. Client-side only; server-side rejection is
  // not propagated back to flip the row to Abandon (v1 limitation).
  form.addEventListener("submit", () => {
    if (state.successDispatched) {
      return;
    }
    state.successDispatched = true;
    const startAt = state.startAt;
    const elapsed = startAt === null
      ? 0
      : Math.max(0, Math.round(performance.now() - startAt));
    const payload: LifecyclePayload = {
      formKey: state.formKey,
      contentKey: state.contentKey,
      eventType: FormEventType.Success,
      elapsedMsFromStart: elapsed,
    };
    dispatchLifecycle(payload);
  });
}
