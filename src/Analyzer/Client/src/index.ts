// Analyzer backoffice client bundle.
//
// Slice 001 (FR-006) shipped a minimal detectable namespace token.
// Slice 004 adds `window.analyzer.send("event", ...)` — the first
// callable client API, used by Razor pages to push custom engagement
// events to the management endpoint (US1).

import { send, type CustomEventResponse, type CustomEventError } from "./analytics/send";

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
