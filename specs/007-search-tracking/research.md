# Phase 0 Research: Internal Search-Tracking Capture

**Slice**: 007-search-tracking
**Date**: 2026-05-19
**Status**: Complete — both spec-level scope-significant decisions (Clarifications §1 dedicated table; §2 hard-delete cascade) are resolved with documented rationale in `spec.md`. This document records the supporting design decisions for the Phase 1 design surface.

## R1 — Client helper API shape

**Decision**: `window.analyzer.sendSearch(query: string, resultCount: number, options?: { pageviewKey?: string }): Promise<{ eventKey: string } | { skipped: true }>`.

- Two-argument primary signature; a third `options` object reserved for future-extensibility (`pageviewKey` override is the only field for v1).
- Returns a Promise. Resolves with `{ eventKey }` on HTTP 202 success; resolves with `{ skipped: true }` if the opt-out predicate fires; rejects with `{ status: number, message: string }` on any other HTTP status.
- The opt-out predicate is evaluated **per call** — every invocation re-reads the attribute presence. The predicate is the slice-006 shared `isOptedOut()` import from `Client/src/shared/opt-out-attribute.ts`.

**Rationale**:
- Mirrors slice-004's `analyzer.send(...)` return shape (`Promise<{ eventKey }>` on success) so callers learn one error model. The `{ skipped: true }` variant adds an explicit "opted-out" branch — callers can distinguish "didn't post because of opt-out" from "post failed" without throwing.
- **Per-call opt-out evaluation** (differing from slice 006's init-only read) is correct because there is no long-lived listener to mute — every call is independent. The DOM read is O(1) (`document.documentElement.hasAttribute(...) || document.body?.hasAttribute(...)`); evaluating it per call costs ~microseconds and gives editors the most intuitive behaviour: "set the attribute, search stops being captured, even on the same page".
- `options.pageviewKey` lets headless hosts pass the key explicitly when the `<meta>` / `window.analyzer.pageviewKey` plumbing is unavailable (e.g. portal embedding scenarios). Default behaviour reads from the same global slice 006 established.

**Alternatives considered**:
- **Throw on opt-out** (rejected Promise) — forces callers to wrap in try/catch even for the expected happy-path-with-opt-out case; rejected.
- **Three-positional-arg signature** (no options object) — closes the door on future-extensibility without a breaking change; rejected. The options-object pattern is the slice-004 helper's evolutionary path too (currently no options; designed to accept some).
- **Init-only opt-out read** (mirror slice 006) — would require a one-time read at module-init; surprising for editors who toggle the attribute live; rejected. The init-only model only makes sense when there's a listener to mute (scroll).

## R2 — Default normalisation algorithm

**Decision** (resolves spec FR-005): `DefaultAnalyzerSearchQueryNormaliser.Normalise(string raw)` applies in order:

1. `raw.Trim()` — strip leading/trailing ASCII + Unicode whitespace.
2. `Normalize(NormalizationForm.FormKC)` — Unicode compatibility-decomposition + canonical-composition, collapsing fullwidth/halfwidth, ligatures, compatibility-encoded variants.
3. `ToLower(CultureInfo.InvariantCulture)` — culture-stable lower-casing (avoids Turkish dotted-i hazard).
4. Internal-whitespace-run collapse: `Regex.Replace(s, @"\s+", " ")` — multiple inner whitespace characters → single space.

The output is the value persisted to `analyzerSearchEvent.normalisedQuery` and the grouping key for "top queries" aggregations.

A 100-pair fixture (input → expected) is checked into `src/Analyzer.Tests/Unit/Features/Search/Application/normaliser-fixture.json` covering: pure-ASCII trims, mixed-case fold, fullwidth letters (`Ｄｅｓｉｇｎ` → `design`), halfwidth katakana → fullwidth katakana, ligature decomposition (`ﬁ` → `fi`), compatibility characters (`①` → `1`), CRLF/tab/NBSP whitespace-run collapse, leading/trailing combining marks, accented Latin (`café` → `café`; NFKC preserves the canonical accent), and emoji passthrough (`hello 🙂` → `hello 🙂` — emoji are not folded). SC-002 asserts 100 / 100 match.

**Rationale**:
- The four-step order is the well-trodden text-search normalisation pipeline. NFKC before lower-casing matters: fullwidth Latin (`Ｄ`) is folded to ASCII `D` by NFKC, then `ToLowerInvariant` lowers to `d`; doing lower-case first would leave `Ｄ` un-folded.
- Invariant-culture lower-case is critical: `"İSTANBUL".ToLower(new CultureInfo("tr-TR"))` is `"i̇stanbul"` (with dotted-i); same string under InvariantCulture is `"istanbul"`. We need stable across hosts — the multilingual host that wants Turkish folding registers its own normaliser per FR-005.
- Whitespace-run collapse handles the "user pasted `  hello\nworld  `" case: trim removes the outer; NFKC leaves internal whitespace; the regex collapses to `"hello world"`.
- Fixture-driven validation is the cheapest way to lock the contract; future normaliser changes will fail the fixture loudly and demand explicit table updates.

**Alternatives considered**:
- **Lucene-style analyzer chain** (StandardTokenizer + LowerCaseFilter + ASCIIFoldingFilter) — overpowered for v1 + drags in a Lucene dependency the slice doesn't otherwise need; rejected.
- **`StringComparer.OrdinalIgnoreCase`** as the grouping key — would skip the NFKC step entirely; fullwidth/halfwidth variants would not collapse; rejected.
- **Hash the normalised form** for the grouping key — saves a few bytes per row but loses operator-facing readability of the "top queries" report; rejected. The DB row stores the readable string; the index works fine.

## R3 — Visitor-bound `pageviewKey` validation

**Decision** (resolves spec FR-008): The endpoint accepts `pageviewKey: Guid` in the payload and validates that it (a) is non-empty, (b) exists in `customizerPageview`, AND (c) **belongs to the same visitor making the POST** (i.e. `customizerPageview.visitorProfileKey == resolvedVisitorKey`). A mismatch returns HTTP 400 with a structured error; zero rows persisted.

The validation is one indexed lookup against `customizerPageview` keyed by `(key)` — well within the hot-path budget at 200 events/min.

**Rationale**:
- Without the binding check, a malicious or buggy page script could POST `pageviewKey = $someOtherVisitorsPageview` and corrupt the per-page search aggregations. The slice-006 management endpoint did *not* perform this check because scroll milestones cannot be exploited to leak meaningful information (a forged pageviewKey produces a phantom scroll row that the eventual heatmap deduplicates), but search queries are first-class PII per FR-SRC-04 and the cost of mis-attribution is higher.
- The check is symmetric with the slice-005 form-event endpoint's "form must belong to a pageview belonging to the visitor" check; reusing the established pattern.
- The check uses Customizer's storage-level read (NPoco against `customizerPageview`); no new Customizer API surface needed.

**Alternatives considered**:
- **No binding check** (slice-006 posture) — cheaper but reduces audit-trail trustworthiness for a PII row; rejected on FR-SRC-04 grounds.
- **Require the client to sign the `pageviewKey`** with a server-issued token — overkill for v1; the existing anti-forgery cookie already ties the request to the authenticated session.
- **Hard FK + RAISERROR** at the DB layer — works but the error path is harder to translate into a friendly HTTP 400; controller-level validation is cleaner.

## R4 — Opt-out attribute reuse from slice 006

**Decision**: The slice-006 shared opt-out predicate (`Client/src/shared/opt-out-attribute.ts`'s `isOptedOut()`) is consumed unchanged. The search module imports it and invokes it **per call** in the helper (not per-listener-init, since search has no listener).

**Rationale**:
- The predicate's shape (returns boolean, reads `<html>` / `<body>` / scroll-root attribute presence) is correct for search; no extension needed.
- Reading per call costs nothing measurable and gives editors live opt-out behaviour. Slice 006's init-only read was a property of having a long-lived scroll listener — there is no equivalent here.
- This is the first slice to consume the slice-006 shared predicate without modifying it; the extraction's value is realised.

**Alternatives considered**:
- **Re-implement locally** — code duplication for no benefit; rejected.
- **Wrap in a thin "search-specific" predicate** — pointless indirection; rejected. The shared predicate is correctly product-wide.

## R5 — Extension-point lifetime + registration convention

**Decision**: `IAnalyzerSearchQueryNormaliser` is registered as `Scoped` (per-request lifetime). `DefaultAnalyzerSearchQueryNormaliser` is the default; replacement is via a single `services.AddScoped<IAnalyzerSearchQueryNormaliser, MyCustomNormaliser>()` call in a host composer (last registration wins, per Umbraco's DI conventions).

**Rationale**:
- `Scoped` matches slice-001's `IVisitorIdentifier` lifetime decision (Clarification Q3) and Umbraco's per-request convention. The normaliser is stateless in v1 but registering as `Scoped` future-proofs: a host implementation that wants to consult per-request state (e.g. the visitor's `preferredLanguage` claim to pick a locale-specific folding) gets that capability for free.
- Singleton would be theoretically valid for the stateless default (no per-request state), but creates a refactor burden the moment the first stateful custom implementation lands.
- Transient is wasteful: a normaliser is constructed once per request anyway in the controller's DI resolution.

**Alternatives considered**:
- **Singleton** — premature optimisation; rejected. Per-request construction cost is ~nanoseconds.
- **Static class** — closes the extension point entirely; rejected (defeats FR-005).
- **`IServiceCollection.TryAddScoped`** for the default — the slice's composer uses plain `AddScoped`, allowing the host composer's later `AddScoped` to override per Umbraco's last-registration-wins behaviour. Confirmed against Customizer's `IPersonalizationProfile` composer pattern.

## R6 — Audit-log PII-redaction payload shape

**Decision** (resolves spec FR-009): One `AnalyzerSearchEventAuditor` (parallel to slice 004/005/006 auditors) emitted via Microsoft.Extensions.Logging structured logging:

```text
LogInformation(
  "AnalyzerSearchEventCaptured EventKey={EventKey} PageviewKey={PageviewKey} ResultCount={ResultCount} ActorUpn={ActorUpn} ActorOid={ActorOid} ReceivedUtc={ReceivedUtc:o}",
  eventKey, pageviewKey, resultCount, actorUpn, actorOid, receivedUtc);
```

**Critical**: neither `rawQuery` nor `normalisedQuery` is in the parameter list. The structured-log pipeline (Serilog → Elastic / App Insights) is configured by hosts with broader read access than the audit-controlled DB row — the DB row is the canonical, role-gated record of the literal query text; logs are operationally accessible to ops/SRE staff who should *not* see PII.

The auditor is invoked on a successful insert (after the repo returns the inserted DTO). Failed validations (400/401/403/409/500) are not audit-logged at this level — the model-binder + auth pipeline already log them.

**Rationale**:
- FR-SRC-04 explicitly flags search queries as potential PII. NFR-SEC-02 + NFR-SEC-08 require auditable records, but the audit substrate (logs) and the canonical record (DB row) can have different access policies. Logs already include actor identity (`UPN` + `oid`) for who-did-what reconstruction; omitting the query text preserves accountability without broadening PII surface.
- Resolves the apparent tension between "every successful state-change emits an audit-log entry" (Principle VII) and "search queries are PII" (FR-SRC-04) by *redacting the PII field from the log entry* — not by *suppressing the audit entry*.
- SC-006 enshrines the redaction as a measurable success criterion.

**Alternatives considered**:
- **Hash the normalised query** into the log entry — gives a "we logged something" feel while still being PII (a hash of `"john smith offices"` is one-to-one mappable for an attacker with a dictionary); rejected.
- **Log only on operator-side reads** of the rows — would leave the capture path unaudited; rejected (Principle VII regression).
- **Log the query in DEBUG only** — relies on log-level config being correct; hosts misconfiguring would leak; rejected. Better to never write it.

## R7 — Management endpoint route + Principle-VII gate

**Decision**: One endpoint, mirroring slice-004/005/006's controllers:

- Route: `POST /umbraco/management/api/v1/analyzer/search-event`
- Auth: `[Authorize(AuthenticationSchemes = "UmbracoBackofficeAuth")]` (same scheme as slices 004/005/006).
- Anti-forgery: `[ValidateAntiForgeryToken]` (or the management API's anti-forgery filter — confirm at impl time, matching slice 006's resolution).
- Payload validation: `AnalyzerSearchEventPayload` record with `[Required]` annotations on `query`, `resultCount`, `pageviewKey`; range checks on `resultCount ∈ [0, 1_000_000]`; length checks on `query ∈ [1, 256]`; visitor-bound `pageviewKey` check per R3.
- Audit: every accepted call writes one structured log entry per R6.
- Response: HTTP 202 with `{ eventKey }` body on success; 401/403 on identity gate fail; 400 on payload validation fail; 500 on unexpected.
- **No 409 path**: the search-event table has no idempotency unique index — re-running the same search IS a distinct engagement signal (Spec Edge Case "Concurrent same-query submissions"). This is the principal route-shape difference from slice 006.

**Rationale**:
- Mirrors slice-005 / slice-006 route convention exactly; operators learn one URL pattern.
- The four-corner gate is the project's house style for any state-changing management endpoint; deviating would be a Principle-VII regression.
- Omitting the 409 path is the right call: search submissions are not idempotent like scroll milestones are; the user resubmitting the same query is a real signal, not a duplicate.

**Alternatives considered**:
- **Batch-POST endpoint** (multiple queries per request) — needless v1 complexity at 200 events/min throughput. Rejected.
- **Idempotency unique index on `(pageviewKey, normalisedQuery)`** — would suppress the legitimate re-search case; rejected.

## R8 — Cascade-step disposition: hard-delete + contract follow-up

**Decision** (resolves spec FR-010 + Spec Clarifications §2): The new cascade step **hard-deletes** every `analyzerSearchEvent` row matching the anonymised `visitorProfileKey`. This diverges from contract D8's "re-key" disposition.

The divergence is intentional and documented in two places:
1. Spec Clarifications §2 — full rationale (PII posture, slice-004/006 precedent).
2. PR description (to be written at merge time) — explicit follow-up to amend contract D8.

**Rationale**:
- `FR-SRC-04` + CCPA/CPRA right-to-delete + the principle of least personal data combine to make hard-delete the only defensible choice for a PII-bearing row. Re-keying leaves the literal `rawQuery` value attached to a pseudonymous identifier — still a record of "this person searched for $X" from any informed adversary's standpoint.
- Slice-004 (custom events) and slice-006 (scroll samples) already established the hard-delete precedent for per-row engagement signals with no aggregate-load-bearing role; search is the same class.
- The contract was drafted before the v1 spec of search-tracking; the divergence is an evolution of the slice author's understanding, not a constitution violation. Principle IV v1.1.1's participation-pattern menu (delete / soft-delete / re-projection) explicitly authorises per-table choice.

**Contract follow-up**: amend `docs/INTER-PRODUCT-CONTRACT.md` §3 D8 row for `analyzerSearchEvent` from "re-key to anonymised visitor key" to "hard-delete (PII per FR-SRC-04)". This is a paired-PR consideration but can land on the Analyzer side alone, since contract D8 is itself an Analyzer-owned doc.

**Alternatives considered**:
- **Re-key per contract D8** — fails the PII test; rejected with rationale above.
- **Soft-delete (`anonymizedUtc` flag)** — same PII issue as re-key; rejected.
- **Re-projection** (overwrite query columns with a sentinel) — equivalent to "row exists, query is `[redacted]`, still attributable to the now-anonymous visitor". Marginally better than re-key but worse than hard-delete because the row still occupies storage and signals "this anonymous person submitted *something* on this pageview"; rejected for PII parsimony.

## R9 — Public-surface pinning diff

**Decision**: Additive only. Pinning baseline regenerated to include:

- New interface `Analyzer.Analytics.IAnalyzerSearchQueryNormaliser` (one method: `string Normalise(string rawQuery)`).
- New class `Analyzer.Features.Search.Application.Normalisation.DefaultAnalyzerSearchQueryNormaliser` — internal-class-by-default but accessible to the host via its DI registration (no public surface diff for the default impl itself; the public surface is the interface).
- New record `Analyzer.Analytics.AnalyticsSearchEvent` (immutable, `init`-only props).
- New member `IAnalyticsEventStateProvider.CurrentRequestSearchEvents : IReadOnlyList<AnalyticsSearchEvent>`.

No existing members removed or renamed. The pinning test compares against the regenerated baseline; the diff is auto-approved by the `Analyzer.Tests.PublicSurfacePinningTests` baseline-regeneration helper (same pattern as slices 002-006).

**Rationale**:
- Principle X requires that breaking changes only happen on MAJOR releases; this slice is MINOR (additive), so the diff is fine.
- **Note**: this is the first time since slice 001 that the slice introduces a brand-new Analyzer-defined extension surface (`IAnalyzerSearchQueryNormaliser`). Slice 001's `IVisitorIdentifier` set the pattern; slice 007 follows it directly.

## R10 — No Customizer-side change verification

**Verified**:
1. **Cascade-step discovery**: Customizer's `AnonymizationOrchestrator` (slice 002 onward) discovers `IAnonymizationCascadeStep` implementations via DI scan — no central registry, no Customizer source change. ✅ (same as slice 006 R10 item 1).
2. **Pageview FK semantics**: Inter-product contract §3 D9 enumerates Analyzer-owned tables FK'd to `customizerPageview.Key`; `analyzerSearchEvent` joins that list. FK declared via raw SQL in `M0007` per slice-002 precedent — no `Customizer.Features.Pageview.Persistence.CustomerPageviewDto` import. ✅
3. **PageviewKey reading**: Customizer's `IAnalyticsStateProvider.CurrentRequest.PageviewKey` is the pinned read contract; no Customizer-side change needed. ✅
4. **Visitor identity**: `IVisitorIdentifier` (Analyzer-owned, slice 002) handles `oid`/`upn` resolution; no Customizer-side change. ✅
5. **Visitor-bound `pageviewKey` lookup** (new this slice): reads `customizerPageview` via NPoco at the Analyzer-owned repository layer, projecting only `(key, visitorProfileKey)`; no Customizer API surface added or changed. The read is the same shape slice 006 R10 item 3 already validated, just with an extra `WHERE visitorProfileKey = @resolvedVisitorKey` predicate. ✅
6. **Session `TouchAsync`**: slice-004 introduced `IAnalyzerSessionRepository.TouchAsync(sessionKey, lastActivityUtc)`; slice 007 reuses it unchanged. ✅

Zero Customizer-side change required. Spec assertion confirmed.
