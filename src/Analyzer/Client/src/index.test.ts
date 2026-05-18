// Vitest unit test for the bundle namespace (FR-006 / US3 AS2 +
// slice 004 send() exposure). Loading `./index` must populate both
// `globalThis.Analyzer` (slice-001 token) and `globalThis.analyzer`
// (slice-004 lower-cased alias carrying `send`).

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
