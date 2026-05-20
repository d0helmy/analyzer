// Slice 008 / T056 — Vitest test pinning the reduced-motion override
// in the skeleton element. The skeleton's animation is disabled via a
// `@media (prefers-reduced-motion: reduce)` rule in the constructable
// stylesheet; this test asserts the rule is present in the
// stylesheet so users who set the OS preference don't see the pulse.

import { vi } from "vitest";

vi.mock("@umbraco-cms/backoffice/external/lit", async () => {
  const lit = await import("lit");
  const decorators = await import("lit/decorators.js");
  return { ...lit, ...decorators };
});

import { describe, it, expect } from "vitest";
import { AnalyzerContentAnalyticsSkeleton } from "../skeleton.element";

describe("analyzer-content-analytics-skeleton — reduced motion", () => {
  it("contains a prefers-reduced-motion media rule that disables shimmer animation", () => {
    const stylesArray = (AnalyzerContentAnalyticsSkeleton as unknown as {
      elementStyles?: Array<{ cssText?: string }>;
      styles?: { cssText?: string } | Array<{ cssText?: string }>;
    }).styles;
    const styles = Array.isArray(stylesArray) ? stylesArray : [stylesArray];
    const cssText = styles
      .map((s) => (s as { cssText?: string } | undefined)?.cssText ?? "")
      .join("\n");

    expect(cssText).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)/);
    expect(cssText).toMatch(/animation:\s*none/);
  });

  it("renders five placeholder blocks", () => {
    const el = document.createElement(
      "analyzer-content-analytics-skeleton",
    ) as AnalyzerContentAnalyticsSkeleton;
    document.body.appendChild(el);
    return el.updateComplete.then(() => {
      const blocks = el.shadowRoot!.querySelectorAll(".block");
      expect(blocks).toHaveLength(5);
    });
  });
});
