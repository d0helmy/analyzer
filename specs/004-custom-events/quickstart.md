# Quickstart — Slice 004: Custom Events

**Feature**: `004-custom-events`
**Audience**: a developer (or future agent session) opening this slice to implement, review, or extend it.
**Prereq**: slice 003 on `main` (`481b425`); slice 002 + slice 001 already there; Customizer's `Pageview.UserAgent` at `5273c38`; Docker Desktop running for the Aspire SQL container.

---

## TL;DR

```bash
docker info >/dev/null
dotnet --version

git checkout 004-custom-events
dotnet run --project aspire/Analyzer.AppHost --launch-profile https &

dotnet build Analyzer.slnx
dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj \
    --no-build --configuration Release \
    -- -trait- "Category=Integration" -trait- "Category=Perf"

# Integration tests (opt-in)
dotnet run --project src/Analyzer.Tests/Analyzer.Tests.csproj \
    --no-build --configuration Release \
    -- -trait "Category=Integration"

# Pinning regen (one-time after public-surface additions)
ANALYZER_REGENERATE_SNAPSHOTS=1 dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj \
    --filter "FullyQualifiedName~PublicSurfacePinningTests"

# In browser:
#   1. Open https://localhost:44364/umbraco; sign in as dev@analyzer.local
#   2. Render any front-end page in a new tab
#   3. In the browser console:
#        window.analyzer.send("event", "engagement", "click", "header-cta")
#          .then(r => console.log(r))
#   4. Query analyzerCustomEvent — one row with the expected fields
```

---

## Reading order

1. **`spec.md`** — 3 user stories, 13 FRs, 9 SCs, 13 Assumptions. 2 Clarifications resolved (`TouchAsync` + `Promise<{eventKey}>`).
2. **`plan.md`** — Constitution Check (all 10 PASS); Project Structure tree (new `Features/CustomEvents/` vertical slice; first `Web/` sub-folder).
3. **`research.md`** — 12 sections. The load-bearing decision is §3 (synchronous in-request write path) + the §3 revisit (the resolver's `ResolveAsync` gains a `SessionActivityKind` parameter — additive to slice-003 internal interface but small).
4. **`data-model.md`** — schema (10-column `analyzerCustomEvent` table; first Analyzer-to-Analyzer hard FK to `analyzerSession.sessionKey`); `AnalyticsCustomEvent` public record (9 properties); state-store extension; M0003 migration.
5. **`contracts/*.md`** — `AnalyticsCustomEvent` (NEW public record); `IAnalyticsEventStateProvider` (REVISED — adds `CurrentRequestCustomEvents`); `AnalyzerCustomEventController` (NEW HTTP endpoint contract); `AnalyzerCustomEventCascadeStep` (NEW).
6. **`tasks.md`** (produced by `/speckit-tasks`, NOT this command).

---

## The four things that aren't obvious from the docs

### 1. The resolver's `ResolveAsync` signature gains a `SessionActivityKind` parameter.

Slice-003's resolver internally calls `repository.ExtendAsync` on cache hit — which increments `pageviewCount`. For custom events we DON'T want that bump. To keep the cache-miss/race-collision logic centralised in the resolver, we extend the signature:

```csharp
ValueTask<SessionResolutionResult> ResolveAsync(
    Guid visitorProfileKey,
    string? userAgent,
    DateTimeOffset receivedUtc,
    SessionActivityKind activityKind,    // NEW for slice 004
    CancellationToken ct);
```

`SessionActivityKind { Pageview, CustomEvent }`. Internally the resolver dispatches to `ExtendAsync` for `Pageview` (slice-003 behavior unchanged) or `TouchAsync` for `CustomEvent` (slice-004 behavior). The slice-003 `PageviewCapturedHandler` call site updates with `SessionActivityKind.Pageview` (compile-time change; no behavior delta).

If you find yourself adding a "Touch the session AFTER ResolveAsync" code path in the controller, you've taken the option-B path from research §3 — re-read research §3, that's the rejected option.

### 2. The controller IS reliable for `HttpContext.Request.Headers.UserAgent`.

Slice 003's lesson #40 said `IHttpContextAccessor.HttpContext` is unreliable under fire-and-forget Task.Run dispatch. **That's not a constraint here** — the controller runs synchronously on the request thread; `HttpContext` is fully live. Read `Request.Headers.UserAgent.ToString()` directly. No need to thread UA via the notification record.

### 3. The cascade step is **hard-delete**, matching slice-002 receipt.

NOT slice-003's soft-anonymise pattern. Custom events are per-row engagement signals; aggregate preservation isn't load-bearing. If you find yourself adding `anonymizedUtc` to the schema, stop and re-read spec Assumption "Cascade-step semantic is hard-delete."

### 4. Pinning baseline regen captures TWO additive diffs.

1. New `AnalyticsCustomEvent` type block (sealed class + 9 props + standard record members).
2. New `PROP ... CurrentRequestCustomEvents { get; }` on `IAnalyticsEventStateProvider`.

Sync Impact note goes in spec.md Assumptions §"Public-surface pinning regeneration" — extend the existing entry (don't create a new section).

---

## File-creation order (suggested)

1. **Constants** — `Database.AnalyzerCustomEvent`, `AuditLog.CustomEventCapture`.
2. **Repository extension** — `IAnalyzerSessionRepository.TouchAsync` + impl (slice-003 modification).
3. **Resolver extension** — `SessionActivityKind` enum + `ResolveAsync` signature change + internal dispatch.
4. **Update slice-003 handler call site** — `PageviewCapturedHandler` passes `SessionActivityKind.Pageview`.
5. **DTO + migration** — `AnalyzerCustomEventDto`, `M0003`, plan-chain update.
6. **Repository** — `IAnalyzerCustomEventRepository` + impl.
7. **Public record** — `Analyzer.Analytics.AnalyticsCustomEvent`.
8. **State store + provider extensions** — `_currentCustomEvents` list + `AppendCustomEvent`; interface + impl projection.
9. **Cascade step** — `AnalyzerCustomEventCascadeStep`.
10. **Auditor** — `ICustomEventAuditor` + `CustomEventAuditor`.
11. **Web layer** — `CustomEventPayload`, `CustomEventResponse`, `AnalyzerCustomEventController`.
12. **Application layer** — `CustomEventCapture` command + `CustomEventCaptureHandler` (orchestrator).
13. **Composer wiring** — register repo, cascade step, auditor, handler.
14. **Client** — `src/Analyzer/Client/src/analytics/send.ts` + expose on `window.analyzer.send`.
15. **Tests** — unit + integration + Vitest + perf-smoke + regen pinning baseline.

---

## When something goes wrong

| Symptom | Likely cause | Fix |
|---|---|---|
| Slice-003 unit tests fail with "signature mismatch on ResolveAsync" | The `SessionActivityKind` parameter was added to the interface but the existing handler call site wasn't updated | Update `PageviewCapturedHandler` to pass `SessionActivityKind.Pageview` |
| Controller test passes with anonymous request | `[Authorize]` policy missing or wrong | Check Umbraco 17's standard backoffice management-API policy name; mirror Customizer's controller registration |
| Integration test fails with "FK constraint violation on sessionKey" | Resolver returned a SessionKey that's no longer active (sweeper closed it between resolve + insert; rare) | Catch the FK violation in the controller; treat as "session closed between resolve and persist"; retry once or return 409 |
| Validation 400 but `ModelState` doesn't carry the field name | DataAnnotations validation didn't fire — likely missing `[ApiController]` attribute on controller | Add `[ApiController]` to inherit automatic ModelState validation + ProblemDetails responses |
| Vitest fails because `window.analyzer` is undefined | JSDOM doesn't expose globals the same way browsers do | In Vitest setup, manually attach `window.analyzer` for tests OR import the module + call the function directly |
| Pinning baseline diff includes lines beyond the 2 expected | An internal type accidentally leaked into the pinned `Analyzer.Analytics` namespace | Read the diff carefully; relocate the leaking type to an internal namespace |

---

## Cross-product hygiene

- **No Customizer file is modified by slice 004.** All needed surfaces shipped in slice 002 (`PageviewCaptured`), slice 003 (cross-product `Pageview.UserAgent`), and Customizer's slice 007 (`IAnonymizationCascadeStep`).
- **Do not introduce new Customizer-side prereqs.** If you discover one during implementation, stop and re-spec — slice 004's plan PASS gate depends on zero new prereqs.

---

## When in doubt

Read slice 003. The session-resolver flow + repository pattern + cascade-step shape are all there. Slice 004 follows the same patterns — most of the novelty is the controller layer (first management endpoint) + the synchronous in-request orchestration (vs slice-003's fire-and-forget handler).
