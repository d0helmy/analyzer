// Slice-006 / T034 — Vitest unit tests for the scroll observer.
// Validates milestone crossing dispatch, back-scroll idempotency,
// rAF coalescing, short-page emission, anti-forgery header threading,
// missing-pageviewKey short-circuit.

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { startScrollObserver } from "./scroll-observer";

const PAGEVIEW_KEY = "00000000-0000-0000-0000-000000000001";
const CONTENT_KEY = "00000000-0000-0000-0000-000000000002";

interface FetchCall {
  url: string;
  init: RequestInit;
}

function captureFetch(): { mock: ReturnType<typeof vi.fn>; calls: FetchCall[] } {
  const calls: FetchCall[] = [];
  const mock = vi.fn().mockImplementation(async (url: string, init: RequestInit) => {
    calls.push({ url, init });
    return {
      status: 202,
      json: async () => ({ eventKey: "00000000-0000-0000-0000-000000000099" }),
    };
  });
  (globalThis as Record<string, unknown>).fetch = mock;
  return { mock, calls };
}

function setScrollableHeight(documentHeight: number, viewportHeight = 800): void {
  Object.defineProperty(document.documentElement, "scrollHeight", {
    configurable: true,
    value: documentHeight,
  });
  Object.defineProperty(window, "innerHeight", {
    configurable: true,
    value: viewportHeight,
  });
}

function setScrollY(value: number): void {
  Object.defineProperty(window, "scrollY", {
    configurable: true,
    value,
  });
}

async function flushRaf(): Promise<void> {
  await new Promise<void>((resolve) =>
    requestAnimationFrame(() => resolve()),
  );
  // give microtasks (the fetch chain) a turn
  await Promise.resolve();
}

function parsePayload(call: FetchCall): {
  pageviewKey: string;
  contentKey: string;
  bucket: number;
} {
  return JSON.parse(call.init.body as string);
}

describe("scroll-observer (slice 006 T034)", () => {
  beforeEach(() => {
    document.body.textContent = "";
    document.cookie = "UMB-XSRF-TOKEN=test-token";
    setScrollableHeight(5000);
    setScrollY(0);
  });

  afterEach(() => {
    delete (globalThis as Record<string, unknown>).fetch;
    // Clear the cookie best-effort.
    document.cookie =
      "UMB-XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT";
  });

  it("dispatches buckets in order on a smooth scroll-through", async () => {
    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: PAGEVIEW_KEY, contentKey: CONTENT_KEY });

    // Eager rAF on attach — scrollY=0 → no buckets crossed.
    await flushRaf();
    expect(calls.length).toBe(0);

    const scrollable = 5000 - 800; // 4200
    setScrollY(Math.floor(scrollable * 0.26));
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.map(parsePayload).map((p) => p.bucket)).toEqual([25]);

    setScrollY(Math.floor(scrollable * 0.51));
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.map(parsePayload).map((p) => p.bucket)).toEqual([25, 50]);

    setScrollY(Math.floor(scrollable * 0.78));
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.map(parsePayload).map((p) => p.bucket)).toEqual([25, 50, 75]);

    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.map(parsePayload).map((p) => p.bucket)).toEqual([25, 50, 75, 100]);
  });

  it("does not re-dispatch on back-scroll", async () => {
    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: PAGEVIEW_KEY, contentKey: CONTENT_KEY });
    await flushRaf();

    const scrollable = 5000 - 800;

    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.length).toBe(4);

    // Back to top, then to bottom again — no new POSTs.
    setScrollY(0);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();
    expect(calls.length).toBe(4);
  });

  it("coalesces multiple scroll events into a single rAF measurement", async () => {
    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: PAGEVIEW_KEY, contentKey: CONTENT_KEY });
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(Math.floor(scrollable * 0.55));

    // Fire 20 scroll events before the rAF callback runs.
    for (let i = 0; i < 20; i += 1) {
      window.dispatchEvent(new Event("scroll"));
    }

    await flushRaf();

    // Expect at most one fetch per newly-crossed bucket — for 55 %
    // that's 25 + 50 = 2, not 20 × 2.
    expect(calls.length).toBe(2);
  });

  it("emits 100 on a short page without installing a scroll listener", async () => {
    setScrollableHeight(400, 800); // scrollHeight < innerHeight → short page

    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: PAGEVIEW_KEY, contentKey: CONTENT_KEY });

    // No rAF needed — short-page path dispatches synchronously.
    await Promise.resolve();
    await Promise.resolve();

    expect(calls.length).toBe(1);
    expect(parsePayload(calls[0]).bucket).toBe(100);
  });

  it("threads the anti-forgery header on every dispatch", async () => {
    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: PAGEVIEW_KEY, contentKey: CONTENT_KEY });
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    // At least four dispatches for the four buckets this observer
    // crossed; previous test observers may add more (their listeners
    // persist for the suite lifetime — by design; production code
    // never unsubscribes since each pageview gets a fresh module
    // load). Header check applies to every call regardless of source.
    expect(calls.length).toBeGreaterThanOrEqual(4);
    for (const call of calls) {
      const headers = call.init.headers as Record<string, string>;
      expect(headers["X-UMB-XSRF-TOKEN"]).toBe("test-token");
      expect(headers["Content-Type"]).toBe("application/json");
    }
  });

  it("short-circuits when pageviewKey is missing", async () => {
    const { calls } = captureFetch();
    startScrollObserver({ pageviewKey: "", contentKey: CONTENT_KEY });
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    expect(calls.length).toBe(0);
  });
});
