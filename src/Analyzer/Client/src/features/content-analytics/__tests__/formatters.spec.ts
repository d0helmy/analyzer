import { describe, it, expect } from "vitest";
import { formatNumber, formatDurationSeconds } from "../formatters";

describe("formatNumber", () => {
  it("renders zero as 0", () => {
    expect(formatNumber(0)).toBe("0");
  });

  it("inserts thousands separator", () => {
    // Intl.NumberFormat output is locale-specific; the default `en-US`
    // separator is a comma. Tests run in jsdom with the host's default
    // locale — be defensive and accept either comma or period as the
    // grouping char.
    expect(formatNumber(12345)).toMatch(/12[,. ]?345|12345/);
  });
});

describe("formatDurationSeconds", () => {
  it("returns dash for null", () => {
    expect(formatDurationSeconds(null)).toBe("—");
  });

  it("renders sub-minute as Xs", () => {
    expect(formatDurationSeconds(59)).toBe("59s");
  });

  it("renders one-minute-32-seconds as 1m 32s", () => {
    expect(formatDurationSeconds(92)).toBe("1m 32s");
  });

  it("renders one hour cleanly as 60m 0s", () => {
    expect(formatDurationSeconds(3600)).toBe("60m 0s");
  });

  it("renders zero as 0s (not the null dash)", () => {
    expect(formatDurationSeconds(0)).toBe("0s");
  });
});
