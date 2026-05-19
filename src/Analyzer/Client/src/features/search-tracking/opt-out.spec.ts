// Slice-007 / T043 — Vitest test that opt-out short-circuits BEFORE
// the fetch, and resolves the helper with the `{ skipped: true }`
// sentinel rather than throwing. Sets `<body analyzer-no-tracking>`,
// invokes sendSearch ten times, asserts zero fetches and ten
// `{ skipped: true }` resolutions. Then removes the attribute and
// asserts exactly one fetch fires on the next call.

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { sendSearch } from "./send-search";

const PAGEVIEW_KEY = "00000000-0000-0000-0000-000000000001";

beforeEach(() => {
  document.documentElement.removeAttribute("analyzer-no-tracking");
  document.body.removeAttribute("analyzer-no-tracking");
  const globalAny = globalThis as Record<string, unknown>;
  delete globalAny.analyzer;
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("sendSearch — opt-out (US2)", () => {
  it("opted-out body: ten invocations produce zero fetches and ten { skipped: true }", async () => {
    document.body.setAttribute("analyzer-no-tracking", "");
    const mock = vi.fn();
    (globalThis as Record<string, unknown>).fetch = mock;

    const results = await Promise.all(
      Array.from({ length: 10 }, (_, i) =>
        sendSearch(`query-${i}`, i, { pageviewKey: PAGEVIEW_KEY }),
      ),
    );

    expect(mock).not.toHaveBeenCalled();
    expect(results).toHaveLength(10);
    for (const r of results) {
      expect(r).toEqual({ skipped: true });
    }
  });

  it("opted-out via <html> attribute: zero fetches", async () => {
    document.documentElement.setAttribute("analyzer-no-tracking", "");
    const mock = vi.fn();
    (globalThis as Record<string, unknown>).fetch = mock;

    const result = await sendSearch("hi", 1, { pageviewKey: PAGEVIEW_KEY });
    expect(result).toEqual({ skipped: true });
    expect(mock).not.toHaveBeenCalled();
  });

  it("after removing the opt-out attribute, the next call fetches exactly once", async () => {
    document.body.setAttribute("analyzer-no-tracking", "");
    const mock = vi.fn().mockResolvedValue({
      status: 202,
      json: async () => ({ eventKey: "evt-1" }),
    });
    (globalThis as Record<string, unknown>).fetch = mock;

    const before = await sendSearch("first", 1, { pageviewKey: PAGEVIEW_KEY });
    expect(before).toEqual({ skipped: true });
    expect(mock).not.toHaveBeenCalled();

    document.body.removeAttribute("analyzer-no-tracking");

    const after = await sendSearch("second", 2, { pageviewKey: PAGEVIEW_KEY });
    expect(after).toEqual({ eventKey: "evt-1" });
    expect(mock).toHaveBeenCalledTimes(1);
  });
});
