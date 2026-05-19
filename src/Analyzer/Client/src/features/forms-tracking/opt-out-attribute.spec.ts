// Vitest unit tests for the slice-005 US3 opt-out attribute helper.
// Validates: presence-only semantics (any value or no value means
// "skip tracking"); negative case (absent attribute returns false).

import { describe, it, expect } from "vitest";
import { isFormOptedOut, isFieldOptedOut } from "./opt-out-attribute";

describe("opt-out-attribute (T059 — slice 005 US3)", () => {
  it("treats analyzer-no-tracking=\"\" as opted out", () => {
    const form = document.createElement("form");
    form.setAttribute("analyzer-no-tracking", "");
    expect(isFormOptedOut(form)).toBe(true);
  });

  it("treats analyzer-no-tracking=\"anything\" as opted out", () => {
    const form = document.createElement("form");
    form.setAttribute("analyzer-no-tracking", "anything");
    expect(isFormOptedOut(form)).toBe(true);
  });

  it("treats presence-only analyzer-no-tracking as opted out", () => {
    // Setting via setAttribute('foo', '') is the closest DOM API to a
    // presence-only attribute; the resulting attribute is identical in
    // the parsed DOM to `<form analyzer-no-tracking>`.
    const form = document.createElement("form");
    form.setAttribute("analyzer-no-tracking", "");
    expect(isFormOptedOut(form)).toBe(true);
  });

  it("returns false when the attribute is absent", () => {
    const form = document.createElement("form");
    expect(isFormOptedOut(form)).toBe(false);
  });

  it("isFieldOptedOut checks a field element independently", () => {
    const input = document.createElement("input");
    input.setAttribute("analyzer-no-tracking", "");
    expect(isFieldOptedOut(input)).toBe(true);

    const other = document.createElement("input");
    expect(isFieldOptedOut(other)).toBe(false);
  });
});
