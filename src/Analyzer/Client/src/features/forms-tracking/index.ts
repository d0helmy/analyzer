// Slice-005 forms-tracking module entrypoint. Wires the per-form
// observer at `DOMContentLoaded`. US2 layers `field-observer.ts` on
// top; US3 layers `opt-out-attribute.ts` short-circuits on top of
// both.

import { initialiseFormObserver } from "./form-observer";

export function initialiseFormsTracking(): void {
  if (typeof document === "undefined") {
    return;
  }
  if (document.readyState === "loading") {
    document.addEventListener(
      "DOMContentLoaded",
      () => initialiseFormObserver(),
      { once: true },
    );
  } else {
    initialiseFormObserver();
  }
}
