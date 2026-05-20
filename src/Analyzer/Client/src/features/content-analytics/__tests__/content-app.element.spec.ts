// Slice 008 / T037 + T038 — Vitest tests covering the content-app
// element's loading and populated states. The repository fetch is
// stubbed via the element's `_fetcher` test seam so the test stays
// independent of the network surface. The workspace-context
// dependency is bypassed by setting `_contentKey` directly and
// invoking the private `_load`.
//
// The element imports a handful of `@umbraco-cms/backoffice/*` modules
// whose transitive `@umbraco-ui/uui` dependency uses an ESM directory
// import that Node refuses to resolve under Vitest. The `vi.mock`
// blocks below provide minimal stubs for those surfaces — Vitest
// hoists them above the element import so the broken transitive
// chain never loads.

import { vi } from "vitest";

vi.mock("@umbraco-cms/backoffice/external/lit", async () => {
  const lit = await import("lit");
  const decorators = await import("lit/decorators.js");
  return { ...lit, ...decorators };
});

vi.mock("@umbraco-cms/backoffice/element-api", () => ({
  // Identity mixin — UmbElementMixin's `consumeContext` is never
  // invoked in tests (we drive `_load` directly).
  UmbElementMixin: <T extends new (...args: unknown[]) => object>(base: T) =>
    class extends (base as unknown as new (...args: unknown[]) => { consumeContext: (token: unknown, cb: (ctx: unknown) => void) => void }) {
      consumeContext(_token: unknown, _cb: (ctx: unknown) => void): void {
        // no-op in test env
      }
    } as unknown as T,
}));

vi.mock("@umbraco-cms/backoffice/workspace", () => ({
  UMB_WORKSPACE_CONTEXT: Symbol("UMB_WORKSPACE_CONTEXT"),
}));

import { describe, it, expect } from "vitest";
import "../content-app.element";
import type { AnalyzerContentAnalyticsAppElement } from "../content-app.element";
import type { ContentAnalyticsSnapshot } from "../types";

const CONTENT_KEY = "ac716910-a82e-4280-bdf1-3b752e04b5b3";

function newSnapshot(overrides: Partial<ContentAnalyticsSnapshot> = {}): ContentAnalyticsSnapshot {
  return {
    contentKey: CONTENT_KEY,
    windowEndUtc: new Date().toISOString(),
    pageviews24h: 12,
    pageviews7d: 84,
    pageviews30d: 318,
    uniqueVisitors30d: 47,
    avgTimeOnPageSeconds30d: 92,
    isContentCurrentlyTombstoned: false,
    topReferrers30d: [],
    ...overrides,
  };
}

async function mountElement(
  fetcher: (key: string) => Promise<ContentAnalyticsSnapshot>,
): Promise<AnalyzerContentAnalyticsAppElement> {
  const el = document.createElement(
    "analyzer-content-analytics-app",
  ) as AnalyzerContentAnalyticsAppElement;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (el as any)._fetcher = fetcher;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (el as any)._contentKey = CONTENT_KEY;
  document.body.appendChild(el);
  await el.updateComplete;
  return el;
}

describe("analyzer-content-analytics-app — loading state", () => {
  it("renders aria-busy=true and the skeleton element before fetch resolves", async () => {
    let resolve: ((s: ContentAnalyticsSnapshot) => void) | null = null;
    const pending = new Promise<ContentAnalyticsSnapshot>((r) => {
      resolve = r;
    });
    const fetcher = vi.fn().mockReturnValue(pending);

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    void (el as any)._load();
    await el.updateComplete;

    const section = el.shadowRoot!.querySelector("section");
    expect(section).not.toBeNull();
    expect(section!.getAttribute("aria-busy")).toBe("true");
    expect(section!.getAttribute("data-state")).toBe("loading");
    expect(
      el.shadowRoot!.querySelector("analyzer-content-analytics-skeleton"),
    ).not.toBeNull();
    expect(
      el.shadowRoot!.querySelector('[data-testid="metric-grid"]'),
    ).toBeNull();

    resolve!(newSnapshot());
    await pending;
  });
});

describe("analyzer-content-analytics-app — empty state", () => {
  it("renders empty-state copy + zero metric blocks when all counts are zero and not tombstoned", async () => {
    const snapshot = newSnapshot({
      pageviews24h: 0,
      pageviews7d: 0,
      pageviews30d: 0,
      uniqueVisitors30d: 0,
      avgTimeOnPageSeconds30d: null,
      isContentCurrentlyTombstoned: false,
    });
    const fetcher = vi.fn().mockResolvedValue(snapshot);

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;

    const section = el.shadowRoot!.querySelector("section");
    expect(section!.getAttribute("aria-busy")).toBe("false");
    expect(section!.getAttribute("data-state")).toBe("empty");
    expect(
      el.shadowRoot!.querySelector('[data-testid="empty-state"]'),
    ).not.toBeNull();
    expect(
      el.shadowRoot!.querySelector('[data-testid="empty-state"]')?.textContent,
    ).toMatch(/No activity in the last 30 days/);

    const text = (sel: string) =>
      el.shadowRoot!.querySelector(`[data-testid="${sel}"]`)?.textContent?.trim();
    expect(text("pageviews-24h")).toMatch(/^0$/);
    expect(text("pageviews-7d")).toMatch(/^0$/);
    expect(text("pageviews-30d")).toMatch(/^0$/);
    expect(text("unique-visitors-30d")).toMatch(/^0$/);
    expect(text("avg-time-on-page-30d")).toBe("—");
    expect(
      el.shadowRoot!.querySelector('[data-testid="tombstone-banner"]'),
    ).toBeNull();
  });
});

describe("analyzer-content-analytics-app — populated state", () => {
  it("renders all five metric blocks with formatted numbers when fetch resolves", async () => {
    const snapshot = newSnapshot();
    const fetcher = vi.fn().mockResolvedValue(snapshot);

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;

    const section = el.shadowRoot!.querySelector("section");
    expect(section!.getAttribute("aria-busy")).toBe("false");
    expect(section!.getAttribute("data-state")).toBe("populated");
    expect(
      el.shadowRoot!.querySelector("analyzer-content-analytics-skeleton"),
    ).toBeNull();

    const text = (sel: string) =>
      el.shadowRoot!.querySelector(`[data-testid="${sel}"]`)?.textContent?.trim();
    expect(text("pageviews-24h")).toMatch(/12/);
    expect(text("pageviews-7d")).toMatch(/84/);
    expect(text("pageviews-30d")).toMatch(/318/);
    expect(text("unique-visitors-30d")).toMatch(/47/);
    expect(text("avg-time-on-page-30d")).toBe("1m 32s");
    expect(
      el.shadowRoot!.querySelector('[data-testid="tombstone-banner"]'),
    ).toBeNull();
    expect(el.shadowRoot!.querySelector('[data-testid="empty-state"]')).toBeNull();
  });
});

describe("analyzer-content-analytics-app — tombstone banner", () => {
  it("renders the warning banner when isContentCurrentlyTombstoned is true (T051)", async () => {
    const snapshot = newSnapshot({ isContentCurrentlyTombstoned: true });
    const fetcher = vi.fn().mockResolvedValue(snapshot);

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;

    const banner = el.shadowRoot!.querySelector('[data-testid="tombstone-banner"]');
    expect(banner).not.toBeNull();
    expect(banner!.getAttribute("color")).toBe("warning");
    expect(banner!.textContent).toMatch(/recycle bin|unpublished/i);
  });
});

describe("analyzer-content-analytics-app — error state", () => {
  it("renders error headline + status + retry on 404 (T052)", async () => {
    const fetcher = vi.fn().mockRejectedValue({ status: 404, title: "Content node not found" });

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;

    const section = el.shadowRoot!.querySelector("section");
    expect(section!.getAttribute("data-state")).toBe("error");
    expect(section!.textContent).toMatch(/Content node not found/);
    expect(section!.textContent).toMatch(/HTTP 404/);
    expect(
      el.shadowRoot!.querySelector('[data-testid="retry-button"]'),
    ).not.toBeNull();
  });

  it("renders generic error + status on 500 with no stack trace in DOM (T053)", async () => {
    const fetcher = vi.fn().mockRejectedValue({ status: 500, title: undefined });

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;

    const section = el.shadowRoot!.querySelector("section");
    expect(section!.getAttribute("data-state")).toBe("error");
    expect(section!.textContent).toMatch(/Couldn't load analytics/);
    expect(section!.textContent).toMatch(/HTTP 500/);
    expect(section!.textContent).not.toMatch(/at .+:\d+/); // no stack trace
  });

  it("retry button triggers a re-fetch and transitions to populated (T054)", async () => {
    let calls = 0;
    const fetcher = vi.fn().mockImplementation(async () => {
      calls += 1;
      if (calls === 1) {
        throw { status: 500 } as ContentAnalyticsError;
      }
      return newSnapshot();
    });

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    await (el as any)._load();
    await el.updateComplete;
    expect(el.shadowRoot!.querySelector("section")!.getAttribute("data-state")).toBe(
      "error",
    );

    const retry = el.shadowRoot!.querySelector(
      '[data-testid="retry-button"]',
    ) as HTMLElement;
    retry.dispatchEvent(new Event("click", { bubbles: true, composed: true }));
    // Allow the awaited load() promise to resolve, then re-render.
    await new Promise<void>((r) => setTimeout(r, 0));
    await el.updateComplete;

    expect(fetcher).toHaveBeenCalledTimes(2);
    expect(el.shadowRoot!.querySelector("section")!.getAttribute("data-state")).toBe(
      "populated",
    );
  });
});

describe("analyzer-content-analytics-app — lifecycle", () => {
  it("disconnect during in-flight fetch does not surface an unhandled rejection (T055)", async () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    let reject: ((reason: unknown) => void) | null = null;
    const pending = new Promise<ContentAnalyticsSnapshot>((_, r) => {
      reject = r;
    });
    const fetcher = vi.fn().mockReturnValue(pending);

    const el = await mountElement(fetcher);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    void (el as any)._load();
    await el.updateComplete;

    document.body.removeChild(el);
    reject!({ status: 500 });
    await new Promise<void>((r) => setTimeout(r, 0));

    expect(consoleError).not.toHaveBeenCalled();
    consoleError.mockRestore();
  });
});
