// Vitest unit test for the slice-004 `send()` client wrapper.
// Validates: fetch invocation shape (method, URL, content-type),
// anti-forgery header threading from the UMB-XSRF-TOKEN cookie,
// 202 → { eventKey } Promise resolution, 4xx/5xx → rejection with
// { status, message } per Clarification §2.

import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { send } from "./send";

const ENDPOINT = "/umbraco/management/api/v1/analyzer/custom-event";

describe("send() (T018 — slice 004 client API)", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    fetchMock = vi.fn();
    (globalThis as Record<string, unknown>).fetch = fetchMock;
    document.cookie = "";
  });

  afterEach(() => {
    vi.restoreAllMocks();
    document.cookie =
      "UMB-XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/";
  });

  it("POSTs JSON to the management endpoint", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ eventKey: "abc-123" }), { status: 202 }),
    );

    await send("event", "engagement", "click", "header-cta", 42.5);

    expect(fetchMock).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(ENDPOINT);
    expect(init?.method).toBe("POST");
    expect(init?.credentials).toBe("same-origin");
    expect((init?.headers as Record<string, string>)["Content-Type"]).toBe(
      "application/json",
    );
    expect(init?.body).toBe(
      JSON.stringify({
        category: "engagement",
        action: "click",
        label: "header-cta",
        value: 42.5,
      }),
    );
  });

  it("threads the anti-forgery header from the UMB-XSRF-TOKEN cookie", async () => {
    document.cookie = "UMB-XSRF-TOKEN=abc%2Fxyz; path=/";
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ eventKey: "g-1" }), { status: 202 }),
    );

    await send("event", "engagement", "click");

    const init = fetchMock.mock.calls[0][1];
    expect((init?.headers as Record<string, string>)["X-UMB-XSRF-TOKEN"]).toBe(
      "abc/xyz",
    );
  });

  it("resolves with { eventKey } on HTTP 202", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify({ eventKey: "happy-path" }), { status: 202 }),
    );

    const result = await send("event", "engagement", "click");

    expect(result).toEqual({ eventKey: "happy-path" });
  });

  it("rejects with { status, message } on 4xx", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response("Category is required", {
        status: 400,
        statusText: "Bad Request",
      }),
    );

    await expect(send("event", "", "click")).rejects.toMatchObject({
      status: 400,
      message: "Category is required",
    });
  });

  it("rejects with { status, message } on 5xx", async () => {
    fetchMock.mockResolvedValueOnce(
      new Response("upstream broke", {
        status: 503,
        statusText: "Service Unavailable",
      }),
    );

    await expect(send("event", "engagement", "click")).rejects.toMatchObject({
      status: 503,
    });
  });

  it("rejects unsupported kinds", async () => {
    await expect(
      // @ts-expect-error — intentional type violation for runtime check.
      send("video", "engagement", "click"),
    ).rejects.toMatchObject({ status: 0 });
  });
});
