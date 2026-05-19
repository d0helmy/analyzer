// Slice-007 / T044 — proves per-call evaluation of the
// `analyzer-no-tracking` attribute. This is the key behavioural delta
// from slice-006's scroll observer (which reads at init only).
//
// First call: attribute present → `{ skipped: true }`, no fetch.
// Attribute removed dynamically.
// Second call: attribute absent → fetch fires once → `{ eventKey }`.

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { sendSearch } from "./send-search";

const PAGEVIEW_KEY = "00000000-0000-0000-0000-000000000001";

beforeEach(() => {
  document.documentElement.removeAttribute("analyzer-no-tracking");
  document.body.removeAttribute("analyzer-no-tracking");
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("sendSearch — per-call opt-out evaluation (US2)", () => {
  it("toggles between skipped and active on the same page", async () => {
    let fetchCount = 0;
    (globalThis as Record<string, unknown>).fetch = vi.fn().mockImplementation(async () => {
      fetchCount++;
      return { status: 202, json: async () => ({ eventKey: "evt" }) };
    });

    document.body.setAttribute("analyzer-no-tracking", "");
    const r1 = await sendSearch("first", 1, { pageviewKey: PAGEVIEW_KEY });
    expect(r1).toEqual({ skipped: true });
    expect(fetchCount).toBe(0);

    document.body.removeAttribute("analyzer-no-tracking");
    const r2 = await sendSearch("second", 2, { pageviewKey: PAGEVIEW_KEY });
    expect(r2).toEqual({ eventKey: "evt" });
    expect(fetchCount).toBe(1);

    document.body.setAttribute("analyzer-no-tracking", "");
    const r3 = await sendSearch("third", 3, { pageviewKey: PAGEVIEW_KEY });
    expect(r3).toEqual({ skipped: true });
    expect(fetchCount).toBe(1, "still 1 — the third call short-circuits again");
  });
});
