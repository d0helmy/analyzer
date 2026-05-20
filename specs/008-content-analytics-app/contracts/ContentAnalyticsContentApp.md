# Contract: Content-app extension (backoffice)

**Slice**: 008-content-analytics-app
**Surface**: Umbraco backoffice content-app tab
**Bundle**: `/App_Plugins/Analyzer/analyzer.js` (existing — extended with a new module)
**Manifest entry**: appended to `src/Analyzer/Client/public/umbraco-package.json`

## Manifest registration

```json
{
  "id": "Analyzer",
  "name": "Analyzer",
  "version": "0.1.0",
  "allowTelemetry": false,
  "extensions": [
    {
      "name": "Analyzer Bundle",
      "alias": "Analyzer.Bundle",
      "type": "bundle",
      "js": "/App_Plugins/Analyzer/analyzer.js"
    },
    {
      "name": "Content Analytics",
      "alias": "Analyzer.ContentApp.ContentAnalytics",
      "type": "workspaceView",
      "kind": "contentApp",
      "element": "/App_Plugins/Analyzer/analyzer.js",
      "elementName": "analyzer-content-analytics-app",
      "weight": 200,
      "meta": {
        "label": "Analytics",
        "pathname": "analytics",
        "icon": "icon-chart-line"
      },
      "conditions": [
        { "alias": "Umb.Condition.WorkspaceAlias", "match": "Umb.Workspace.Document" }
      ]
    }
  ]
}
```

The `Umb.Workspace.Document` workspace-alias condition causes the tab to attach to every content node regardless of document type (`FR-RPT-001`).

## Custom element

```ts
// src/Analyzer/Client/src/features/content-analytics/content-app.element.ts

import { LitElement, html, css, nothing } from "lit";
import { customElement, state } from "lit/decorators.js";
import { UmbElementMixin } from "@umbraco-cms/backoffice/element-api";

@customElement("analyzer-content-analytics-app")
export class AnalyzerContentAnalyticsApp extends UmbElementMixin(LitElement) {
  @state() private _snapshot: ContentAnalyticsSnapshot | null = null;
  @state() private _error: string | null = null;
  @state() private _loading = true;

  // ... fetch on `connectedCallback`, render skeleton or numbers
}
```

(Full TypeScript shape lives in `src/Analyzer/Client/src/features/content-analytics/types.ts`.)

## Rendering states

The element renders one of four mutually-exclusive states. State transitions are linear: `loading` → (`populated` | `empty` | `error`). Once a terminal state is reached, the element does not re-fetch in MVP (re-fetch on tab re-enter is a future enhancement).

### State 1: `loading` (initial + while fetch is in flight)

- Container: `<section aria-busy="true">…</section>`
- Renders 5 skeleton blocks at the layout positions where the metric numbers will appear.
- No "Loading…" text (a screen reader will announce `aria-busy` change of state).
- Skeleton animation: subtle shimmer via `@keyframes shimmer-pulse` (1.2s loop, 200ms reduced-motion fallback to static grey).

### State 2: `populated` (HTTP 200 with non-zero counts)

- Container: `<section aria-busy="false">…</section>`
- Five metric blocks in a CSS grid:
  - Pageviews — three sub-numbers labelled `24h`, `7d`, `30d`.
  - Unique visitors — one number labelled `30d`.
  - Avg time on page — one number formatted via `formatDurationSeconds()` labelled `30d`. Renders `—` if `avgTimeOnPageSeconds30d === null`.
- If `isContentCurrentlyTombstoned === true`: a small `<uui-tag color="warning">` banner at the top of the section reads "This content is in the recycle bin or unpublished. Historical analytics are still available below."

### State 3: `empty` (HTTP 200 with all metric fields = 0 / null)

- Container: `<section aria-busy="false">…</section>`
- One headline: "No activity in the last 30 days."
- Sub-copy: "Once visitors view this page, their pageviews, unique visitors, and average time on page appear here."
- Same five metric blocks are rendered with `0` / `—` values so layout doesn't shift when activity arrives — the headline + sub-copy disappear on next load if data appears.

### State 4: `error` (HTTP 4xx / 5xx, or fetch throws)

- Container: `<section aria-busy="false">…</section>`
- One headline: "Couldn't load analytics for this content."
- One detail line carrying the HTTP status code + the localised problem-details `title` when available.
- A `<uui-button look="secondary" label="Retry">` triggers a re-fetch (NOT a full element teardown — preserves any user-selected sub-state).
- No stack traces. No raw error payload.

## Accessibility

- `aria-busy` attribute on the section element, true during fetch / false after resolution. Screen-reader announcement happens automatically on the change.
- Metric numbers wrapped in `<dl>` / `<dt>` / `<dd>` so screen readers announce "Pageviews 24 hours: 12" not just "12".
- All copy strings sourced from `@umbraco-cms/backoffice/localization` so future localisation slices can override.
- Focus order: tombstone banner → metric blocks → retry button (in `error` state).

## Test plan (`content-app.element.spec.ts`)

Tests use Vitest's `jsdom` environment + `@open-wc/testing-helpers` for shadow DOM assertions. The repository is mocked via `vi.fn()` returning a controllable promise.

| # | Scenario | Assertions |
|---|---|---|
| 1 | Initial render before fetch resolves | `aria-busy="true"`, 5 skeleton blocks present, no metric numbers |
| 2 | Fetch resolves with populated snapshot | `aria-busy="false"`, 5 metric blocks with correct numbers, no skeleton |
| 3 | Fetch resolves with all-zero snapshot | Empty-state headline present, metric blocks show `0`, no error banner |
| 4 | Fetch rejects with 404 | Error headline present, status code 404 shown, retry button present |
| 5 | Fetch rejects with 500 | Error headline present, status code 500 shown, no stack trace in DOM |
| 6 | Retry button click in error state | Triggers re-fetch (mock called twice); on success, transitions to populated |
| 7 | `isContentCurrentlyTombstoned: true` | Tombstone banner present with warning colour |
| 8 | `avgTimeOnPageSeconds30d: null` | Avg-time block renders `—` |
| 9 | `avgTimeOnPageSeconds30d: 92` | Avg-time block renders "1m 32s" |
| 10 | Number formatting: 12345 pageviews | Renders "12,345" (thousands separator) |
| 11 | Reduced-motion media query active | Skeleton animation disabled (CSS-only assertion) |
| 12 | Element unmount during in-flight fetch | No console error / no unhandled promise rejection |

## Out of scope (deferred to future slices)

- Time-window selector (24h / 7d / 30d as user-selectable). MVP shows all three at once.
- Date-range picker for custom windows.
- Drill-down to per-visitor list (gated on `IIndividualDataAccessCheck` — owned by future per-visitor drill-down slice).
- Sparkline / chart visualisations.
- CSV export.
- Comparison-to-previous-period delta indicators.
- Top-referrers list rendering (the response shape carries an empty array placeholder, but the UI does not render anything for it in MVP).
