// Analyzer backoffice client bundle.
//
// Slice 001 (FR-006) shipped a minimal detectable namespace token.
// Slice 004 adds `window.analyzer.send("event", ...)` — the first
// callable client API, used by Razor pages to push custom engagement
// events to the management endpoint (US1).
// Slice 005 adds auto-attached per-form lifecycle capture
// (Impression / Start / Success) for every Umbraco Form rendered at
// DOMContentLoaded.

import { send, type CustomEventResponse, type CustomEventError } from "./analytics/send";
import { initialiseFormsTracking } from "./features/forms-tracking";
import { initialiseScrollTracking } from "./features/scroll-tracking";

declare const __ANALYZER_VERSION__: string;

export type { CustomEventResponse, CustomEventError };
export { send };

interface AnalyzerNamespace {
  version: string;
  send: typeof send;
}

const globalAny = globalThis as Record<string, unknown>;
const existing = globalAny.Analyzer as Partial<AnalyzerNamespace> | undefined;

const namespace: AnalyzerNamespace = {
  ...(existing ?? {}),
  version: __ANALYZER_VERSION__,
  send,
};

globalAny.Analyzer = namespace;
// Lowercase alias — spec naming convention `window.analyzer.send(...)`.
globalAny.analyzer = namespace;

// Slice 005 — auto-attach the forms-tracking module. Safe to call
// pre-DOMContentLoaded: the initialiser defers attach until the DOM
// is ready when the document is still loading.
initialiseFormsTracking();

// Slice 006 — auto-attach the scroll-tracking module. Sequential
// after forms-tracking so module-init ordering is deterministic;
// each module's initialiser is independent (no shared state).
initialiseScrollTracking();
