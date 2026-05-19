// Slice-006 / T034 — Vitest unit tests for the milestone-crossing
// detector. Pure function; no DOM needed.

import { describe, it, expect } from "vitest";
import { ScrollBucket } from "./scroll-event-dispatcher";
import { detectNewlyCrossed, allBuckets } from "./milestone-tracker";

describe("milestone-tracker (slice 006 T034)", () => {
  it("returns no buckets below the lowest threshold", () => {
    const crossed = new Set<ScrollBucket>();
    expect(detectNewlyCrossed(10, crossed)).toEqual([]);
    expect(crossed.size).toBe(0);
  });

  it("returns 25 when percent is exactly 25", () => {
    const crossed = new Set<ScrollBucket>();
    expect(detectNewlyCrossed(25, crossed)).toEqual([ScrollBucket.Quarter]);
    expect(crossed.has(ScrollBucket.Quarter)).toBe(true);
  });

  it("returns multiple buckets on a jumpy scroll (Home/End)", () => {
    // Visitor scrolls smoothly to 30 %, then End-key-jumps to 100 %.
    const crossed = new Set<ScrollBucket>();
    expect(detectNewlyCrossed(30, crossed)).toEqual([ScrollBucket.Quarter]);
    expect(detectNewlyCrossed(100, crossed)).toEqual([
      ScrollBucket.Half,
      ScrollBucket.ThreeQuarters,
      ScrollBucket.Full,
    ]);
  });

  it("does not re-emit already-crossed buckets on back-scroll", () => {
    // Visitor scrolls top → 80 % → top → 80 %.
    const crossed = new Set<ScrollBucket>();
    detectNewlyCrossed(80, crossed); // crosses 25, 50, 75
    expect(detectNewlyCrossed(0, crossed)).toEqual([]);
    expect(detectNewlyCrossed(80, crossed)).toEqual([]);
  });

  it("emits each bucket in ascending order in a single call", () => {
    const crossed = new Set<ScrollBucket>();
    const result = detectNewlyCrossed(100, crossed);
    expect(result).toEqual([
      ScrollBucket.Quarter,
      ScrollBucket.Half,
      ScrollBucket.ThreeQuarters,
      ScrollBucket.Full,
    ]);
  });

  it("allBuckets returns the four pinned values", () => {
    expect(allBuckets()).toEqual([
      ScrollBucket.Quarter,
      ScrollBucket.Half,
      ScrollBucket.ThreeQuarters,
      ScrollBucket.Full,
    ]);
  });
});
