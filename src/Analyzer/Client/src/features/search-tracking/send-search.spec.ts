// Slice-007 / T035 — Vitest unit tests for `sendSearch`. Covers the
// non-opt-out conformance items in
// contracts/AnalyzerSendSearchClient.md: happy path, trim, empty /
// whitespace / oversize / negative / non-integer / NaN / Infinity
// rejection, three pageviewKey sources, pageviewKey unavailable,
// server 400, server 202, anti-forgery header threading. Opt-out
// behaviour is covered in opt-out.spec.ts + opt-out-per-call.spec.ts
// (T043 / T044).

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { sendSearch } from "./send-search";

const PAGEVIEW_KEY = "00000000-0000-0000-0000-000000000001";
const EVENT_KEY = "00000000-0000-0000-0000-000000000099";

interface FetchCall {
  url: string;
  init: RequestInit;
}

function captureFetch(
  responseFactory: () => { status: number; body: unknown } = () => ({
    status: 202,
    body: { eventKey: EVENT_KEY },
  }),
): { mock: ReturnType<typeof vi.fn>; calls: FetchCall[] } {
  const calls: FetchCall[] = [];
  const mock = vi.fn().mockImplementation(async (url: string, init: RequestInit) => {
    calls.push({ url, init });
    const r = responseFactory();
    return {
      status: r.status,
      json: async () => r.body,
    };
  });
  (globalThis as Record<string, unknown>).fetch = mock;
  return { mock, calls };
}

function readBody(call: FetchCall): { pageviewKey: string; query: string; resultCount: number } {
  return JSON.parse(call.init.body as string);
}

function removeAnalyzerMetaTags(): void {
  // Safe DOM removal — no innerHTML usage (per the project's
  // security-hook convention).
  const tags = document.head.querySelectorAll('meta[name="analyzer-pageview-key"]');
  for (const tag of Array.from(tags)) {
    tag.parentNode?.removeChild(tag);
  }
}

beforeEach(() => {
  document.documentElement.removeAttribute("analyzer-no-tracking");
  document.body.removeAttribute("analyzer-no-tracking");
  removeAnalyzerMetaTags();
  document.cookie = "UMB-XSRF-TOKEN=xsrf-stub; path=/";
  // Reset window.analyzer between tests.
  const globalAny = globalThis as Record<string, unknown>;
  delete globalAny.analyzer;
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("sendSearch — happy path", () => {
  it("happy path resolves with eventKey from server", async () => {
    const { calls } = captureFetch();
    const result = await sendSearch("design system", 12, { pageviewKey: PAGEVIEW_KEY });
    expect(result).toEqual({ eventKey: EVENT_KEY });
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe("/umbraco/management/api/v1/analyzer/search-event");
    const body = readBody(calls[0]);
    expect(body.query).toBe("design system");
    expect(body.resultCount).toBe(12);
    expect(body.pageviewKey).toBe(PAGEVIEW_KEY);
  });

  it("trims the query before sending", async () => {
    const { calls } = captureFetch();
    await sendSearch("  hello  ", 3, { pageviewKey: PAGEVIEW_KEY });
    const body = readBody(calls[0]);
    expect(body.query).toBe("hello");
  });

  it("attaches the anti-forgery header", async () => {
    const { calls } = captureFetch();
    await sendSearch("hi", 1, { pageviewKey: PAGEVIEW_KEY });
    const headers = calls[0].init.headers as Record<string, string>;
    expect(headers["X-UMB-XSRF-TOKEN"]).toBe("xsrf-stub");
  });
});

describe("sendSearch — client-side rejection", () => {
  it("rejects empty query with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("", 5, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "query required" });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects whitespace-only query with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("   \n\t  ", 5, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "query required" });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects oversize query (257 chars) with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("x".repeat(257), 5, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "query too long" });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects negative resultCount with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("hello", -1, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "resultCount must be a non-negative integer" });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects non-integer resultCount with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("hello", 3.14, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "resultCount must be a non-negative integer" });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects NaN resultCount with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("hello", Number.NaN, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400 });
    expect(mock).not.toHaveBeenCalled();
  });

  it("rejects Infinity resultCount with status 400", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("hello", Number.POSITIVE_INFINITY, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400 });
    expect(mock).not.toHaveBeenCalled();
  });
});

describe("sendSearch — pageviewKey resolution", () => {
  it("reads pageviewKey from options first", async () => {
    const { calls } = captureFetch();
    await sendSearch("hello", 1, { pageviewKey: PAGEVIEW_KEY });
    expect(readBody(calls[0]).pageviewKey).toBe(PAGEVIEW_KEY);
  });

  it("falls back to window.analyzer.pageviewKey", async () => {
    const { calls } = captureFetch();
    const globalAny = globalThis as Record<string, unknown>;
    globalAny.analyzer = { pageviewKey: PAGEVIEW_KEY };
    await sendSearch("hello", 1);
    expect(readBody(calls[0]).pageviewKey).toBe(PAGEVIEW_KEY);
  });

  it("falls back to <meta name=\"analyzer-pageview-key\">", async () => {
    const { calls } = captureFetch();
    const meta = document.createElement("meta");
    meta.setAttribute("name", "analyzer-pageview-key");
    meta.setAttribute("content", PAGEVIEW_KEY);
    document.head.appendChild(meta);
    await sendSearch("hello", 1);
    expect(readBody(calls[0]).pageviewKey).toBe(PAGEVIEW_KEY);
  });

  it("rejects when pageviewKey is unavailable from all three sources", async () => {
    const { mock } = captureFetch();
    await expect(sendSearch("hello", 1))
      .rejects.toMatchObject({ status: 400, message: "pageviewKey unavailable" });
    expect(mock).not.toHaveBeenCalled();
  });
});

describe("sendSearch — server response mapping", () => {
  it("rejects with server's status + detail on 400", async () => {
    captureFetch(() => ({ status: 400, body: { errors: [{ detail: "bad query" }] } }));
    await expect(sendSearch("hello", 1, { pageviewKey: PAGEVIEW_KEY }))
      .rejects.toMatchObject({ status: 400, message: "bad query" });
  });

  it("resolves with eventKey on 202", async () => {
    captureFetch(() => ({ status: 202, body: { eventKey: EVENT_KEY } }));
    const result = await sendSearch("hello", 1, { pageviewKey: PAGEVIEW_KEY });
    expect(result).toEqual({ eventKey: EVENT_KEY });
  });
});
