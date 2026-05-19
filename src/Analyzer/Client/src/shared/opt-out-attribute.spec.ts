// Vitest unit tests for the shared opt-out predicates extracted in
// slice 006 T039. Element-level predicate is exercised in slice 005's
// existing opt-out tests; this file adds coverage for the document-
// level predicate that slice 006 introduces.

import { describe, it, expect, beforeEach } from "vitest";
import {
  ANALYZER_NO_TRACKING_ATTRIBUTE,
  isElementOptedOut,
  isDocumentOptedOut,
} from "./opt-out-attribute";

describe("opt-out-attribute (shared — slice 006 T039)", () => {
  beforeEach(() => {
    document.documentElement.removeAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE);
    document.body.removeAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE);
    document.body.textContent = "";
  });

  it("isElementOptedOut returns true when attribute is present", () => {
    const el = document.createElement("div");
    el.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");
    expect(isElementOptedOut(el)).toBe(true);
  });

  it("isElementOptedOut returns false when attribute is absent", () => {
    const el = document.createElement("div");
    expect(isElementOptedOut(el)).toBe(false);
  });

  it("isDocumentOptedOut returns false when no marker present", () => {
    expect(isDocumentOptedOut()).toBe(false);
  });

  it("isDocumentOptedOut returns true when present on <body>", () => {
    document.body.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");
    expect(isDocumentOptedOut()).toBe(true);
  });

  it("isDocumentOptedOut returns true when present on <html>", () => {
    document.documentElement.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");
    expect(isDocumentOptedOut()).toBe(true);
  });

  it("isDocumentOptedOut is presence-only (any value works)", () => {
    document.body.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "any-value");
    expect(isDocumentOptedOut()).toBe(true);
  });
});
