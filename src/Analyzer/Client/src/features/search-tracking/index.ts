// Analyzer slice-007 search-tracking module entrypoint.
//
// Attaches `sendSearch` onto `window.analyzer` at module-init time
// (called once by `index.ts`). The helper itself reads
// `analyzer-no-tracking` per call (not at init), differing from slice
// 006's scroll observer which reads the attribute at handler-init
// only — search has no long-lived listener to mute, so toggling the
// attribute live must affect the very next invocation.

import { sendSearch } from "./send-search";

interface AnalyzerNamespace {
  sendSearch?: typeof sendSearch;
}

export function initialiseSearchTracking(): void {
  if (typeof window === "undefined") {
    return;
  }
  const globalAny = globalThis as Record<string, unknown>;
  const namespace = (globalAny.analyzer ??= {}) as AnalyzerNamespace;
  namespace.sendSearch = sendSearch;
  // Also expose on the uppercased alias for parity with the
  // slice-001-established `Analyzer` global.
  const uppercased = (globalAny.Analyzer ??= namespace) as AnalyzerNamespace;
  uppercased.sendSearch = sendSearch;
}

export { sendSearch };
export type { SendSearchOptions } from "./send-search";
export type { SearchEventResult } from "./payload";
