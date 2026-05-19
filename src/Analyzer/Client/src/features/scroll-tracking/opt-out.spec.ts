// Slice-006 / T042 — Vitest unit test for the US2 opt-out
// short-circuit. When `analyzer-no-tracking` is present on `<body>`
// (or `<html>`) at init time, the module installs no scroll listener
// and fires no fetch even after a simulated scroll-through.
//
// Also documents v1 behaviour (US2 AS2): dynamic attribute changes
// after init do NOT retroactively short-circuit. Captured here so
// future readers know it's intentional, not an oversight.

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { initialiseScrollTracking } from "./index";
import { ANALYZER_NO_TRACKING_ATTRIBUTE } from "../../shared/opt-out-attribute";

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
  await Promise.resolve();
}

describe("scroll-tracking US2 opt-out (slice 006 T042)", () => {
  beforeEach(() => {
    document.documentElement.removeAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE);
    document.body.removeAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE);
    document.body.textContent = "";
    setScrollableHeight(5000);
    setScrollY(0);
    (globalThis as Record<string, unknown>).analyzer = {
      pageviewKey: PAGEVIEW_KEY,
      contentKey: CONTENT_KEY,
    };
  });

  afterEach(() => {
    delete (globalThis as Record<string, unknown>).fetch;
    delete (globalThis as Record<string, unknown>).analyzer;
  });

  it("dispatches zero POSTs when <body analyzer-no-tracking> is present at init", async () => {
    document.body.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");
    const { calls } = captureFetch();

    initialiseScrollTracking();
    // initialiseScrollTracking attaches synchronously when readyState
    // is not "loading" (jsdom default).
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    expect(calls.length).toBe(0);
  });

  it("dispatches zero POSTs when <html analyzer-no-tracking> is present at init", async () => {
    document.documentElement.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");
    const { calls } = captureFetch();

    initialiseScrollTracking();
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    expect(calls.length).toBe(0);
  });

  it("documents v1 behaviour — dynamic attribute set AFTER init does NOT short-circuit (US2 AS2)", async () => {
    const { calls } = captureFetch();
    initialiseScrollTracking();
    await flushRaf();

    // Set the attribute AFTER init; capture continues per spec.
    document.body.setAttribute(ANALYZER_NO_TRACKING_ATTRIBUTE, "");

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    expect(calls.length).toBeGreaterThan(0);
  });

  it("does NOT short-circuit when the attribute is absent", async () => {
    const { calls } = captureFetch();
    initialiseScrollTracking();
    await flushRaf();

    const scrollable = 5000 - 800;
    setScrollY(scrollable);
    window.dispatchEvent(new Event("scroll"));
    await flushRaf();

    expect(calls.length).toBeGreaterThan(0);
  });
});
