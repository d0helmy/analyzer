# Accessibility checklist — Content Analytics content app (slice 008 / T057)

Reviewed against the source of `analyzer-content-analytics-app.element.ts` +
`analyzer-content-analytics-skeleton.element.ts`. Live screen-reader testing
is deferred behind the slice-007-followup #34 EntraID-claims shim (no
local backoffice login available yet) — this checklist documents the
source-level guarantees the slice ships and a follow-up TODO for the
live audit when the shim lands.

| # | Item | Status | Notes |
|---|------|--------|-------|
| 1 | `aria-busy` on the `<section>` container flips with state | ✅ | `loading` → `true`, all other states → `false`. Asserted by `content-app.element.spec.ts` loading + populated + empty + error tests. |
| 2 | Metric numbers wrapped in `<dl>` / `<dt>` / `<dd>` so screen readers announce "Pageviews 24 hours: 12" not just "12" | ✅ | See `_renderSnapshot()` — `<dl class="metrics">` with `<dt>` labels + `<dd>` values. |
| 3 | Tombstone banner uses `<uui-tag color="warning">` with descriptive copy | ✅ | "This content is in the recycle bin or unpublished. Historical analytics are still available below." Asserted in Vitest T051. |
| 4 | Error state retry button uses `<uui-button>` with explicit `label="Retry"` | ✅ | `_renderError()` — semantic button. Asserted by Vitest T052 + T054. |
| 5 | Error state never leaks stack traces into the DOM | ✅ | Only `error.title` (when present) + status code rendered. Asserted by Vitest T053. |
| 6 | Skeleton animation honours `prefers-reduced-motion: reduce` | ✅ | `@media (prefers-reduced-motion: reduce) { .block { animation: none; ... } }` in `skeleton.element.ts`. Asserted by `skeleton.element.spec.ts`. |
| 7 | Focus order: tombstone banner → metric blocks → retry button (error only) | ✅ | DOM order matches: banner rendered first, then metrics, then retry only in error state. Tab order follows DOM. |
| 8 | All user-visible copy strings sourced via `localize.term('analyzer_contentAnalytics_*')` | ⚠️ Deferred | T058 — localisation pass not done in MVP polish. Tracked as a follow-up. The English strings live inline in the element; refactor to `lang/en.ts` once the slice ships. |
| 9 | Live screen-reader audit (VoiceOver / NVDA / JAWS) | ⚠️ Deferred | Requires a working backoffice login locally; blocked by slice-007-followup #34. |
| 10 | Live keyboard-only navigation audit | ⚠️ Deferred | Same blocker as item 9. |

## Follow-ups (post-slice)

- File a slice-008-followup issue covering items 8-10 once #34 lands. The
  fix surface is the element source — no API or DTO changes required.
- Confirm `<uui-tag>` and `<uui-button>` from `@umbraco-ui/uui` carry the
  expected ARIA roles in the host's Umbraco 17.3.5 deployment (these are
  the upstream components — Analyzer doesn't override them).
