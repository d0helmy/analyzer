# Analyzer ↔ Customizer Inter-Product Contract

**Status:** Draft v2 — 2026-05-17 (inverted layering)
**Authors:** Product owner (Dia) + AI
**Binding on:** [`analyzer`](https://github.com/d0helmy/analyzer) and [`customizer`](https://github.com/d0helmy/customizer)
**v1 → v2 change:** dependency direction inverted. Analyzer now depends on Customizer (was the opposite in v1). Rationale: Customizer is shipped, tested, pinned; Analyzer is paper. Always-deployed-together makes the layering a code-organization choice, not a deployment topology one. Less churn → faster shipping.

This contract resolves the scope overlap that exists between Analyzer
(new, being specified) and Customizer (shipped — slices 001–008 on
`main`, slice 009 closed at `64dadd1`). It must be ratified **before**
Analyzer's first `/speckit-specify` slice opens, so that Analyzer's
foundational slice can scaffold against a committed layering.

Both repos' `CLAUDE.md` files MUST link to this document, and any change
to the decisions below MUST be reflected as an inter-product commit
sweep (one PR per repo, referenced cross-wise).

---

## 1. Layering & dependency direction

```
┌─────────────────────────────────────────────────────────────────┐
│ Analyzer  (analytics — full product)                            │
│   - Sessions, custom events, video, forms, scroll, search       │
│   - Per-content-node Analytics content app                      │
│   - Reports + dashboards (FR-RPT-*)                             │
│   - Traffic Filters (FR-FLT-*)                                  │
│   - Goal reporting (definitions stay in Customizer per D5)      │
│   - IEventDimensionExtractor (new extension surface)            │
│   - IAnalyticsEventStateProvider (new; separate from            │
│     Customizer's IAnalyticsStateProvider)                       │
└─────────────────────────────┬───────────────────────────────────┘
                              │ runtime dependency
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Customizer  (platform substrate + personalization)              │
│   - VisitorProfile + identity (slice 003)                       │
│   - PageviewCaptureMiddleware + customizerPageview (slice 003)  │
│   - IAnalyticsStateProvider + IPersonalizationProfile (003)     │
│   - Goals feature + IVisitorReachedGoalsLookup (slice 007)      │
│   - UTM capture columns (slice 007)                             │
│   - IAnonymizationCascadeStep + anonymisation flow (003/007)    │
│   - Webhook dispatcher + outbox (slice 002)                     │
│   - Segments + Personalizations + Resolver (slices 001/005/008) │
└─────────────────────────────────────────────────────────────────┘
```

**Analyzer depends on Customizer.** Not the other way around. Customizer
has zero knowledge of Analyzer; Customizer can in principle deploy
without Analyzer (analytics-less segmentation product), but the
operator intent is to deploy them together as a single intranet
analytics-+-personalization bundle.

**Rationale:**
- Customizer is shipped — its public surface is pinned, its tests are
  green, its concepts are mature. Touching it costs a major version
  bump and a retrofit slice. Touching Analyzer costs nothing (it
  doesn't exist yet).
- Customizer's slice-003 substrate was framed as "minimum-viable
  analytics + visitor-profile". With the inverted layering it
  matures into "the platform substrate" without a single code change.
- Always-deployed-together (operator commitment) means the conceptual
  weirdness of "to use analytics you need the personalization
  package" never surfaces in practice — both packages always land
  together.
- The inversion preserves Customizer's pinned `PublicSurfacePinningTests`
  exactly. No major version bump. No deprecation cycle. No retrofit
  slice. No migration head re-pointing.

**Conceptual trade accepted:** the natural product layering (analytics
below personalization, à la Engage) is inverted here for pragmatic
reasons. Documented and visible to future Claudes via this file +
both CLAUDE.md links.

---

## 2. Decision summary

| # | Topic | Decision | Owner | Analyzer integration shape |
|---|-------|----------|-------|----------------------------|
| D1 | Visitor identity + profile | Customizer's `VisitorProfile` (slice 003) is the canonical visitor record. One row per EntraID `oid`. | Customizer | Analyzer consumes via Customizer's existing `IPersonalizationProfile` / `IAnalyticsStateProvider.CurrentVisitor`; may project into a richer Analyzer-facing read view for its own reports |
| D2 | Pageview capture | Customizer's `PageviewCaptureMiddleware` + `customizerPageview` table (slice 003) is the canonical pageview store. Analyzer does not ship its own pageview middleware. | Customizer | Analyzer subscribes to pageview events (Umbraco notification or a Customizer-published event) for its reports + content-app; Analyzer's *non-pageview* event capture (custom events, video, forms, scroll, search) lives in Analyzer-owned tables FK'd to `customizerPageview.Key` |
| D3 | `IAnalyticsStateProvider` | Customizer's existing `Customizer.Analytics.IAnalyticsStateProvider` stays exactly as it is. Analyzer ships a separate `Analyzer.Analytics.IAnalyticsEventStateProvider` for its event-stream concerns (custom events fired this request, video position, etc.). | Both (non-overlapping surfaces) | No churn on Customizer's pinned surface |
| D4 | `IPersonalizationProfile` | Unchanged — stays in Customizer, stays the contract `ISegmentRule` evaluation receives. | Customizer | Analyzer may consume it directly for reports requiring visit-history aggregates |
| D5 | Goals | Goal definitions + management UI + `IVisitorReachedGoalsLookup` stay in Customizer (slice 007 ships these). Analyzer adds **goal-completion reports** on top, reading from Customizer's existing storage. | Customizer (definitions, capture); Analyzer (reports) | Analyzer's `FR-GOL-*` surfaces become reporting views over Customizer's goal-completion data |
| D6 | Campaigns / UTM | Unchanged — Customizer's slice-007 UTM capture stays. Analyzer's `FR-DIM-04` scope-drop holds at Analyzer's reporting layer (Analyzer dashboards do not show UTM). | Customizer | Analyzer ignores UTM columns; Customizer's Campaigns rule keeps working |
| D7 | Outbox + webhook dispatcher | Customizer's slice-002 dispatcher is the canonical outbound transport. Analyzer's new events (`session.*`, `customEvent.*`, etc.) emit through it. | Customizer | Analyzer registers event-type producers; no separate Analyzer transport |
| D8 | Anonymisation | Customizer owns the operator-facing erasure action and `IAnonymizationCascadeStep` (slice 003/007). Analyzer registers cascade steps for its own tables (custom events, video events, forms events, sessions, scroll, search). | Customizer (orchestrator); Analyzer (cascade steps) | Analyzer composer wires N cascade steps at startup |
| D9 | Per-content-node "Analytics" content app | Analyzer owns. Reads from `customizerPageview` + `customizerVisitorProfile` + Analyzer's own event tables. | Analyzer | Adds a new content-app element; no conflict with Customizer's "Used by personalizations" panel |
| D10 | Identity claim semantics | `oid`-first, `upn`-fallback. Matches Customizer's existing implementation. UPN is display-only. | Customizer (canonical) | Analyzer adopts; no change |
| D11 | Sessions | New concept; Analyzer-owned. Customizer never modelled sessions (only a cumulative `VisitCount`). | Analyzer | New `analyzerSession` table FK'd to `customizerVisitorProfile.Key`; sessionization runs as an Analyzer-side derivation over Customizer's pageview stream |

---

## 3. Per-decision detail

### D1 — Visitor identity + profile

`customizerVisitorProfile` (slice 003) stays as the canonical visitor
record. Schema in Customizer; Analyzer never writes to it.

Analyzer's read needs:
- **Identity** — `IPersonalizationProfile.Key` (Guid) is the foreign-key
  Analyzer uses on every Analyzer-owned event table. Never the
  `IdentityRef` string; that's a logging-only concern and is pinned to
  Customizer's slice-003 log-template metatest.
- **Visit-history aggregates** — already on Customizer's
  `IAnalyticsStateProvider` (slice-007 added the history members).
  Analyzer reuses these for report queries.
- **Profile enrichment** — Customizer's slice-003 punted on "extensible
  profile property bag for arbitrary third-party data enrichment".
  Analyzer needs this for EntraID-claim enrichment per the Analyzer
  spec (`FR-ENR-*` — `department`, `officeLocation`, etc.).
  **Open question (see §6 item 2):** does Analyzer add a Customizer-side
  extension point (touches Customizer's pinned surface — small bump) or
  ship its own side table keyed on visitor `Key` (no Customizer change,
  some query-join cost)?

Identity claim ordering: `oid`-first, `upn`-fallback per D10.
No retrofit on Customizer for identity (it already matches).

### D2 — Pageview capture

`customizerPageview` (slice 003) stays as the canonical pageview row.
Analyzer **does not** ship a competing middleware.

Analyzer subscribes to pageview-captured events via one of:
1. An Umbraco `INotification` / `INotificationHandler` published by
   Customizer's middleware (preferred — idiomatic).
2. A direct in-process publish on the slice-002 outbox (less coupled
   to Umbraco notification scheduling but adds latency).

**Resolution required (see §6 item 1):** Customizer's slice-003 does
not currently publish a `PageviewCaptured` notification — it captures
into a write queue and that's it. Analyzer's subscription requires
Customizer to add this notification. That's a small additive change
on Customizer (no breaking-surface impact; pure new public type) and
can land as **Customizer slice 0xx (small)** before Analyzer's first
slice opens. Sequencing detail in §4.

Analyzer-owned event tables (custom events, video, forms, scroll,
search) FK on `customizerPageview.Key`. Analyzer never duplicates the
pageview row.

Throughput target: 1000 pv/s sustained / 5000 pv/s peak — already met
by Customizer slice-003. Analyzer-side event captures must not regress
that envelope (NFR).

### D3 — `IAnalyticsStateProvider` (no collision)

Customizer's pinned `Customizer.Analytics.IAnalyticsStateProvider` is
**unchanged**. No re-export, no bump, no deprecation.

Analyzer ships a separate, narrower contract for its own concerns:

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    IReadOnlyCollection<CustomEvent> CurrentRequestCustomEvents { get; }
    VideoState? CurrentVideoState { get; }
    // ... extensible per Analyzer slice
}
```

Name choice (`IAnalyticsEventStateProvider`, not `IAnalyticsStateProvider`)
explicitly avoids collision with Customizer's existing pinned name.
Both interfaces can be injected into the same consumer — no ambiguity.

### D4 — `IPersonalizationProfile`

Unchanged. Stays in Customizer, stays the contract `ISegmentRule`
evaluation receives.

Analyzer may inject `IPersonalizationProfile` directly into its
report-query services to read the same visit-history aggregates
segment rules read. This is intentional duplication of the read path,
not a wrapper — Analyzer is just another consumer of an existing
public contract.

### D5 — Goals

Goals stay in Customizer:

- Definition CRUD: Customizer goals feature (slice 007).
- `IVisitorReachedGoalsLookup`: Customizer cross-feature DI seam
  (slice 007).
- Completion capture: wherever Customizer captures it today (per
  slice-007 implementation).

Analyzer adds:

- **Goal-completion report views** (`FR-GOL-*`) — backoffice surfaces
  that visualize goal funnels, conversion rates per content set, per
  visitor segment, etc.
- Per-content-node goal-completion counts on the Analytics content
  app (D9).

Analyzer reads from `IVisitorReachedGoalsLookup` and the underlying
goal-completion store (likely needs a `IGoalCompletionStore` or
similar read contract — additive on Customizer's public surface).

**Open question (see §6 item 3):** Analyzer's `FR-GOL-*` spec items
include a more elaborate goal-definition model than Customizer's
slice-007 needs (e.g. multi-step funnels, time-bounded goals).
Does Analyzer drop those items, or are they expansions to Customizer's
goals feature? Recommend: drop the elaborated items from Analyzer
v1; revisit after slice-007 has been in production.

### D6 — Campaigns / UTM

Unchanged from v1. Customizer keeps UTM capture (slice 007). Analyzer's
`FR-DIM-04` scope-drop holds at the **reporting** layer — Analyzer's
dashboards do not surface UTM data.

### D7 — Outbox + webhook dispatcher

Customizer's slice-002 dispatcher becomes the **canonical platform
outbox** for both products. Analyzer emits its event types
(`session.started`, `session.ended`, `customEvent.recorded`,
`pageview.captured` if introduced — though pageview-level events are
NOT emitted per slice-003 FR-020, keeping that decision) via the
same dispatcher.

This is much cleaner than v1's "two outboxes, same envelope" split —
single transport, single retry/throttle pipeline, single ops surface
for subscribers.

Analyzer's composer registers its event types with the slice-002
event registry. No new transport code in Analyzer.

### D8 — Anonymisation

Customizer owns the operator-facing erasure action and the
`IAnonymizationCascadeStep` extension contract (already shipped in
slice 003 / extended in slice 007).

Analyzer registers cascade steps for each Analyzer-owned table:

- `analyzerCustomEvent` — re-key to anonymised visitor key
- `analyzerVideoEvent` — re-key
- `analyzerFormsEvent` — re-key
- `analyzerSession` — re-key
- `analyzerScrollSample` — re-key
- `analyzerSearchEvent` — re-key
- Plus any side tables FK'd to visitor

Each is a small composer registration. Analyzer's anonymisation
testing inherits Customizer's slice-003 contract tests.

### D9 — Per-content-node "Analytics" content app

Analyzer ships the content-app element. Reads:

- Pageview counts + unique-visitor counts from `customizerPageview`
  joined to `customizerVisitorProfile`.
- Average time on page from Analyzer's session data (D11).
- Scroll heatmap from Analyzer's `analyzerScrollSample`.
- Goal completions from Customizer's goals storage (D5).
- Matched segments per content from Customizer's
  `customizerPageviewSegmentSnapshot` (slice-009).

Role-gated per `NFR-SEC-*` — individual-level UPN data visible only
to an authorised user group.

### D10 — Identity claim ordering

`oid`-first, `upn`-fallback — ratified 2026-05-17. Matches Customizer
slice-003. See v1 for rationale.

### D11 — Sessions (new)

Customizer never modelled sessions (only `VisitCount`). Analyzer adds:

- `analyzerSession { Key, VisitorProfileKey, StartedUtc, LastActivityUtc,
  EndedUtc?, PageviewCount, EventCount }` table.
- Sessionization rule: configurable inactivity timeout (e.g. 30 min);
  derived at pageview-capture time by an Analyzer subscription handler
  (D2) — increments existing open session or starts a new one.
- Session-close write happens lazily on next pageview after timeout, or
  by a periodic close job.

This is purely additive — no Customizer code is touched. Sessions
become available as a dimension to Analyzer reports and to the per-
content-node content app.

---

## 4. Integration plan

No retrofit on Customizer. The work is:

**Customizer side** (small, single PR; can run in parallel with
Analyzer specification):

- **Customizer slice 011** — "Pageview-captured notification". Adds
  an `INotification` published from `PageviewCaptureMiddleware` after
  the row commits. Pure additive surface; no breaking change; tiny
  perf impact (one notification dispatch per pageview, already batched
  with the existing write queue). Estimated 1–2 days. **This is the
  only pre-requisite Customizer change for Analyzer to start.**

**Analyzer side** (everything else — sequenced):

```
Analyzer  slice 001  package skeleton + Customizer dep + composer
                     + IVisitorIdentifier (reads from Customizer's
                     existing identity layer; no new identity code)
Analyzer  slice 002  IAnalyticsEventStateProvider scaffold +
                     pageview-notification subscription
Analyzer  slice 003  Sessions (D11) — first real Analyzer feature
Analyzer  slice 004  Custom events + analyzer.send client API
Analyzer  slice 005  Per-content-node Analytics content app (D9)
Analyzer  slice 006  Goal-completion reports (D5)
Analyzer  slice 007  Video tracking
Analyzer  slice 008  Forms tracking
Analyzer  slice 009  Scroll heatmap
Analyzer  slice 010  Internal search tracking
Analyzer  slice 011  Traffic Filters
Analyzer  slice 012  Top-level reports + dashboards
Analyzer  slice 013  Anonymisation cascade-step registrations
Analyzer  slice 014  Per-EntraID enrichment (FR-ENR-*) — gated on
                     §6 item 2 resolution
```

Slice ordering is illustrative; sequence by user value, not by this
list.

---

## 5. What Analyzer adds beyond what Customizer has

Customizer slice-003 was deliberately the **minimum-viable** substrate.
Analyzer is the full product. Net-new vs. what's in Customizer today:

- **Sessions** (`FR-EVT-*` session semantics — D11)
- Custom events + client-side push API (`analyzer.send(…)`)
- Video tracking (`FR-VID-*`)
- Forms tracking (`FR-FRM-*`)
- Scroll heatmap (`FR-HMP-*`)
- Internal search tracking (`FR-SRC-*`)
- Traffic Filters (`FR-FLT-*`)
- Reports + dashboards (`FR-RPT-*`)
- Per-content-node Analytics content app
- `IEventDimensionExtractor` extension surface (new in Analyzer)
- `IAnalyticsEventStateProvider` (new in Analyzer; separate name from
  Customizer's pinned `IAnalyticsStateProvider`)
- Role-gated access to individual-level UPN data (`NFR-SEC-*`)
- Goal-completion **reports** (definitions stay in Customizer per D5)

None of these conflict with anything Customizer ships. Most are purely
additive on top of Customizer's existing substrate.

---

## 6. Things requiring explicit user sign-off before ratification

1. **`PageviewCaptured` notification on Customizer (D2)** — Analyzer's
   subscription model needs Customizer to publish an `INotification`
   from its pageview-capture middleware. This is a one-PR additive
   change on Customizer (let's call it slice 011) and is the only
   Customizer-side prerequisite for Analyzer to start. **Confirm
   you're OK landing this single small slice on Customizer before
   opening Analyzer.** Alternative: Analyzer reads from
   `customizerPageview` polling-style instead of notification-driven,
   which is uglier but needs zero Customizer change.

2. **Per-EntraID profile enrichment (D1 / `FR-ENR-*`)** — Analyzer
   needs to attach `department`, `officeLocation`, etc. to the visitor
   profile. Options:
   - **(a)** Add an extensibility hook on Customizer's `VisitorProfile`
     (touches Customizer's pinned surface — small bump, breaks pinning
     snapshot; behaviourally additive only).
   - **(b)** Analyzer ships its own side table `analyzerVisitorEnrichment`
     keyed on `customizerVisitorProfile.Key` — zero Customizer change,
     extra query join.
   Recommend **(b)** to keep the invariant "Analyzer never touches
   Customizer's pinned surface".

3. **Goal model expansion (D5)** — Analyzer's spec hints at richer
   goal semantics (multi-step funnels, time-bounded). Confirm
   Analyzer v1 sticks to Customizer's slice-007 goal model
   (single-event match) and defers funnels/etc. to a later phase
   that may expand Customizer's goals feature.

4. **UTM capture ownership** — D6 keeps UTM in Customizer. Unchanged
   from v1. Confirm this still holds under the inverted layering
   (it should — Customizer is now the canonical owner anyway).

5. ~~**Dependency direction**~~ — ✅ **Ratified 2026-05-17: Analyzer
   depends on Customizer.** Driven by Customizer being shipped/tested
   and the always-deployed-together operator commitment.

6. ~~**Customizer major version bump**~~ — ✅ **Ratified: not
   required.** The inverted layering means Customizer's public
   surface is unchanged.

7. ~~**Identity claim ordering**~~ — ✅ **Ratified 2026-05-17:
   `oid`-first, `upn`-fallback.** See D10.

8. ~~**Slice 010 sizing**~~ — ✅ **Ratified: N/A.** No Customizer
   retrofit slice exists under the inverted layering. The only
   Customizer-side work is item 1 above (the `PageviewCaptured`
   notification).

---

## 7. Maintenance

- Update this document when any decision changes; flag the change in
  both repos' CLAUDE.md.
- Update the dependency diagram in §1 if the layering changes.
- This document is **not** a substitute for per-slice specs in either
  repo — those still live under `specs/`. This is the binding meta-
  contract between products.
- Version bumps: major v# bump on layering / ownership flips; minor on
  decision refinement; patch on clarifications.
