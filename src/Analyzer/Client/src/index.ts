// Analyzer backoffice client bundle — slice 001 (FR-006).
//
// Intentionally minimal per spec Clarification Q4: registers no
// backoffice UI elements, exposes no callable client API. The bundle
// exists so the host's `umbraco-package.json` channel is wired up
// (User Story 3) and exports a single detectable namespace token so
// the bundle's presence is verifiable from a runtime inspector.
//
// The callable client API (`analyzer.send(...)`) and content-app
// elements ship in later slices (custom events at slice 004; per-
// content-node Analytics content app at slice 005).

declare const __ANALYZER_VERSION__: string;

// Vite's `define` config replaces __ANALYZER_VERSION__ with the
// quoted version string from package.json at build time.
(globalThis as Record<string, unknown>).Analyzer = {
  version: __ANALYZER_VERSION__,
};
