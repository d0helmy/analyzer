// Vitest unit test for the empty-but-detectable bundle (spec FR-006
// + US3 AS2). Loading `./index` must populate
// `globalThis.Analyzer = { version }`.

import { describe, it, expect, beforeAll } from "vitest";

interface AnalyzerNamespace {
  version: string;
}

declare global {
  // eslint-disable-next-line no-var
  var __ANALYZER_VERSION__: string;
}

(globalThis as Record<string, unknown>).__ANALYZER_VERSION__ = "0.1.0-test";

describe("Analyzer namespace token (FR-006 / US3 AS2)", () => {
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

  it("exposes no callable API at slice 001 (analyzer.send is deferred to slice 004)", () => {
    const ns = (globalThis as unknown as { Analyzer: AnalyzerNamespace & { send?: unknown } }).Analyzer;
    expect("send" in ns).toBe(false);
  });
});
