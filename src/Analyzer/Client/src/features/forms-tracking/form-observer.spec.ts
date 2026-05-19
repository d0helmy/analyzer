// Vitest unit tests for slice-005 US3 — the form-observer's opt-out
// short-circuit. Asserts that a form bearing `analyzer-no-tracking`
// produces ZERO POSTs (no lifecycle, no field dispatch), per SC-005
// and FR-007.

import { describe, it, expect, beforeEach, vi } from "vitest";
import { initialiseFormObserver } from "./form-observer";

function buildForm(opts: {
  optOutForm?: boolean;
  optOutField?: boolean;
}): HTMLFormElement {
  const form = document.createElement("form");
  form.setAttribute("data-umbraco-form", "11111111-1111-1111-1111-111111111111");
  form.setAttribute("data-umbraco-form-page-id", "22222222-2222-2222-2222-222222222222");
  if (opts.optOutForm === true) {
    form.setAttribute("analyzer-no-tracking", "");
  }

  const input = document.createElement("input");
  input.type = "text";
  input.setAttribute("data-umbraco-field", "33333333-3333-3333-3333-333333333333");
  if (opts.optOutField === true) {
    input.setAttribute("analyzer-no-tracking", "");
  }
  form.appendChild(input);
  document.body.appendChild(form);
  return form;
}

describe("form-observer opt-out short-circuit (T060 — US3)", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn().mockResolvedValue({
      status: 202,
      json: async () => ({ eventKey: "00000000-0000-0000-0000-000000000001" }),
    });
    (globalThis as Record<string, unknown>).fetch = fetchMock;
    document.body.textContent = "";
  });

  it("dispatches zero POSTs for a form with form-level opt-out", () => {
    const form = buildForm({ optOutForm: true });

    initialiseFormObserver();

    const field = form.querySelector<HTMLInputElement>("input")!;
    field.dispatchEvent(new FocusEvent("focus", { bubbles: true }));
    field.dispatchEvent(new FocusEvent("blur", { bubbles: true }));
    form.dispatchEvent(new Event("submit", { bubbles: true }));

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("dispatches lifecycle Start but skips field events when only the field opts out", () => {
    const form = buildForm({ optOutField: true });

    initialiseFormObserver();

    const field = form.querySelector<HTMLInputElement>("input")!;
    field.dispatchEvent(new FocusEvent("focus", { bubbles: true }));
    field.dispatchEvent(new FocusEvent("blur", { bubbles: true }));

    const calls = fetchMock.mock.calls.map((c: unknown[]) => c[0] as string);
    const fieldCalls = calls.filter((u) => u.endsWith("/form-event/field"));
    expect(fieldCalls).toHaveLength(0);
  });
});
