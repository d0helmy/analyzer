// Vitest unit test for the bundle namespace (FR-006 / US3 AS2 +
// slice 004 send() exposure). Loading `./index` must populate both
// `globalThis.Analyzer` (slice-001 token) and `globalThis.analyzer`
// (slice-004 lower-cased alias carrying `send`).
//
// Slice 008 added a side-effect import of the content-analytics
// content-app element to `./index`. That element imports
// `@umbraco-cms/backoffice/external/lit`, `/element-api`, and
// `/workspace` — whose transitive `@umbraco-ui/uui` chain does a
// directory import that Node refuses to resolve under Vitest. The
// `vi.mock` hoists below stub those surfaces so the import side-effect
// chain completes; the element's @customElement registration runs
// against the stubbed Lit + identity-mixin, which is sufficient for
// the bundle-namespace assertions below.

import { vi } from "vitest";

vi.mock("@umbraco-cms/backoffice/external/lit", async () => {
  const lit = await import("lit");
  const decorators = await import("lit/decorators.js");
  return { ...lit, ...decorators };
});

vi.mock("@umbraco-cms/backoffice/element-api", () => ({
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

import { describe, it, expect, beforeAll } from "vitest";

interface AnalyzerNamespace {
  version: string;
  send: (...args: unknown[]) => unknown;
}

declare global {
  // eslint-disable-next-line no-var
  var __ANALYZER_VERSION__: string;
}

(globalThis as Record<string, unknown>).__ANALYZER_VERSION__ = "0.1.0-test";

describe("Analyzer namespace (FR-006 + slice 004 send)", () => {
  beforeAll(async () => {
    await import("./index");
  });

  it("populates globalThis.Analyzer", () => {
    const ns = (globalThis as unknown as { Analyzer: AnalyzerNamespace }).Analyzer;
    expect(ns).toBeDefined();
  });

  it("exposes a version string", () => {
    const ns = (globalThis as unknown as { Analyzer: AnalyzerNamespace }).Analyzer;
    expect(typeof ns.version).toBe("string");
    expect(ns.version.length).toBeGreaterThan(0);
  });

  it("exposes window.analyzer.send (slice 004 callable API)", () => {
    const upper = (globalThis as unknown as { Analyzer: AnalyzerNamespace }).Analyzer;
    const lower = (globalThis as unknown as { analyzer: AnalyzerNamespace }).analyzer;
    expect(typeof upper.send).toBe("function");
    expect(typeof lower.send).toBe("function");
    expect(lower).toBe(upper);
  });
});
