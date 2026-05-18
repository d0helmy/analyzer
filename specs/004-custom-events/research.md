# Phase 0 Research: Custom Events

**Slice**: 004 — custom events
**Date**: 2026-05-18
**Constitution**: v1.1.1
**Input**: [`spec.md`](spec.md) + [`plan.md`](plan.md) Technical Context

The two load-bearing spec-level decisions are resolved as Clarifications §1 (custom events advance `lastActivityUtc` via new `TouchAsync` method) + §2 (`analyzer.send()` returns `Promise<{ eventKey }>`). This document captures the implementation-level decisions grounded against the existing Customizer + Analyzer codebases.

---

## §1 — Management endpoint shape

**Decision**: register `AnalyzerCustomEventController : ManagementApiControllerBase` (Umbraco 17's standard backoffice-API base class from `Umbraco.Cms.Api.Management`). Route attribute `[Route("management/api/v1/analyzer/custom-event")]` (placeholder per spec Assumption — slice 005 may rename to a pinned Analyzer namespace prefix). Single `[HttpPost]` action `Capture(CustomEventPayload)` returning `Task<IActionResult>`.

Authentication: Umbraco's `[Authorize(Policy = ...)]` decoration on the controller, matching Customizer's slice-007 controller precedent. Anonymous → 401 by framework default.

Anti-forgery: Umbraco's standard `[ValidateAntiForgeryToken]`-equivalent filter applies automatically because the route lives under the standard management-API conventions. No custom anti-forgery code.

**Rationale**: matches Customizer's controller conventions (e.g. `Customizer/Controllers/DocumentTypeSegmentation/...`); reuses Umbraco's pinning of authentication + anti-forgery; satisfies Principle VII without bespoke auth code. The route prefix is documented as placeholder so slice 005 can rename without breaking the contract (the JS-side client wrapper centralises the URL).

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Minimal API (`MapPost("/analyzer/...")`) | No Umbraco backoffice auth integration out of the box; would re-implement anti-forgery + RBAC primitives. |
| Raw `Microsoft.AspNetCore.Mvc.Controller` base | Misses Umbraco's management-API conventions (route prefix, OpenAPI gen, problem-details responses). |
| One controller per kind ("event", "video", ...) | Premature; slice 004 ships one kind. Add new kinds later as additional actions on the same controller or as new controllers under `Features/<Kind>/Web/`. |

---

## §2 — Client-side `send()` shape

**Decision**: `window.analyzer.send(kind, category, action, label?, value?): Promise<{ eventKey: string }>` per Clarification §2.

```typescript
// src/Analyzer/Client/src/analytics/send.ts (sketch)
export async function send(
  kind: "event",
  category: string,
  action: string,
  label?: string,
  value?: number,
): Promise<{ eventKey: string }> {
  const antiForgery = readAntiForgeryToken(); // Umbraco standard cookie/header
  const res = await fetch(buildUrl(kind), {
    method: "POST",
    credentials: "same-origin",
    headers: {
      "Content-Type": "application/json",
      "X-XSRF-TOKEN": antiForgery,
    },
    body: JSON.stringify({ category, action, label, value }),
  });
  if (res.status !== 202) {
    const message = await res.text().catch(() => res.statusText);
    throw Object.assign(new Error(message), { status: res.status, message });
  }
  return res.json() as Promise<{ eventKey: string }>;
}
```

The `kind` first-arg is currently always `"event"` but the shape leaves room for future kinds (`"video"`, `"scroll"`, etc.) per slice 006+. Exposed on `window.analyzer.send` via `src/Analyzer/Client/src/index.ts`.

Anti-forgery token sourced from Umbraco's standard backoffice anti-forgery cookie/header pair (the conventional name `X-XSRF-TOKEN` or whatever Umbraco 17 sets; pin in implementation via reading `umbraco-xsrf-token` cookie). The actual cookie/header name is read from the document and threaded — no hard-coded value.

**Rationale**: spec FR-001 + Clarification §2 bind the shape. Vanilla `fetch` + Promise return matches modern web patterns; no transpilation cost beyond the existing TypeScript bundle.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `XMLHttpRequest`-based wrapper | Outdated; no benefit over `fetch`. |
| `navigator.sendBeacon(...)` | Useful for fire-and-forget but doesn't return a Promise + doesn't carry anti-forgery headers reliably. Could be a future "best-effort dispatch" mode but defeats Clarification §2's "HTTP 202 = row persisted" contract. |
| Promise<void> | Rejected at Clarification §2. |

---

## §3 — Synchronous in-request write path

**Decision**: the controller's `Capture` action runs:

```
1. Read pv.UserAgent from the request (via IHttpContextAccessor; for the controller-on-request-thread case this IS reliable — HttpContext is live)
2. Read visitorProfileKey from authenticated identity via slice-001 IVisitorIdentifier
3. Validate payload (FR-007); return 400 with structured error if invalid
4. Resolve session via IAnalyzerSessionResolver.ResolveAsync(visitorProfileKey, userAgent, receivedUtc, ct)
   — same call slice-003's pageview handler makes; cache-hit or DB-read-then-open
5. Repository.TouchAsync(resolution.SessionKey, receivedUtc, ct)
   — NEW slice-003-repo method (Clarification §1); 1 indexed UPDATE; no pageviewCount change
6. Repository.InsertAsync(customEventDto, ct) — 1 indexed INSERT
7. AnalyticsEventStateStore.AppendCustomEvent(projection) — in-memory request-scoped append
8. ICustomEventAuditor.Audit(actor, eventKey, category, action) — 1 ILogger emit
9. Return 202 { eventKey: ... }
```

Total worst-case SQL: 1 SELECT (resolver cache miss) + 1 UPDATE (touch) + 1 INSERT (custom event) = 3 statements. Cache-hit path: 0 SELECT + 1 UPDATE + 1 INSERT = 2 statements.

**Note on the resolver flow**: slice-003's `AnalyzerSessionResolver.ResolveAsync` calls `repository.ExtendAsync` internally on cache hit — that increments `pageviewCount`. For custom events we want `TouchAsync` instead. Two design choices:

| Option | Pros | Cons |
|---|---|---|
| **(A)** Add a parameter to `ResolveAsync` (e.g. `bool incrementPageviewCount`) — single resolver call site, repository chooses Extend vs Touch internally | Single call site in the controller; resolver still owns the cache-miss/race-collision logic | Changes slice-003's public-internal interface; mild surface churn |
| **(B)** Bypass the resolver's Extend; call `IAnalyzerSessionResolver.ResolveAsync` for the open-or-attach decision but then call `Repository.TouchAsync` directly afterwards | Zero churn on slice-003 resolver; clear separation of "session existence" from "session activity advancement" | Two-step orchestration in the controller; slightly more verbose |

**Pinned**: option (B) — the controller orchestrates `resolver.ResolveAsync` → `repository.TouchAsync`. Rationale: slice-003's resolver is settled and shipped (slice 003 just merged); rather than churn the resolver internals, the controller explicitly handles the two-step shape. The slight verbosity is documented in the controller's contract.

Wait — there's a subtlety. The slice-003 resolver's `ExtendAsync` runs inside ResolveAsync ON CACHE HIT, and that increments pageviewCount. If the controller calls ResolveAsync, the session's pageviewCount has ALREADY been wrongly bumped before TouchAsync runs.

**Pinned (revised)**: option (A) — extend `IAnalyzerSessionResolver.ResolveAsync` with an additional parameter to control the activity-advancement semantic. Concretely, change the signature to `ResolveAsync(Guid visitorProfileKey, string? userAgent, DateTimeOffset receivedUtc, SessionActivityKind activityKind, CancellationToken ct)` where `SessionActivityKind` is an enum `{ Pageview, CustomEvent }`. The resolver passes the kind down to the repository call (`ExtendAsync` for pageview, `TouchAsync` for custom event). Slice-003 callers pass `SessionActivityKind.Pageview` (no behavior change). Slice-004 controller passes `SessionActivityKind.CustomEvent`.

This IS a slice-003 internal interface change. It's small (one parameter on an internal interface), additive in the sense that existing callers must update their call sites with `SessionActivityKind.Pageview`, and matches the original Clarification §1 spirit (one TouchAsync repo method invoked through the resolver).

**Alternatives considered for the activity-kind dispatch**:

| Alternative | Why rejected |
|---|---|
| Two separate resolver methods (`ResolveForPageviewAsync`, `ResolveForCustomEventAsync`) | API surface bloat; the rest of the flow is identical. |
| Pass a delegate `(IAnalyzerSessionRepository, Guid, DateTimeOffset) → Task` for the activity step | Too clever; harder to test; the enum + switch is the obvious shape. |
| Have the resolver always do "Touch" semantics and let the pageview handler do its own "increment pageview count" call as a follow-up | Changes slice-003's race-safety story (the partial unique index relies on Insert-then-extend being atomic from the resolver's perspective; splitting Touch from Extend creates a window). |

---

## §4 — `IAnalyzerSessionRepository.TouchAsync` (new method)

**Decision**: add to the slice-003 repository contract:

```csharp
/// <summary>
/// Slice 004 — advance lastActivityUtc on the session WITHOUT
/// incrementing pageviewCount. Used by the custom-event capture path
/// (Clarification §1). 1 indexed UPDATE.
/// </summary>
Task TouchAsync(
    Guid sessionKey,
    DateTimeOffset newLastActivityUtc,
    CancellationToken ct);
```

Implementation:

```csharp
public async Task TouchAsync(
    Guid sessionKey, DateTimeOffset newLastActivityUtc, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    using var scope = _scopeProvider.CreateScope();
    await scope.Database.ExecuteAsync(
        $"UPDATE {Constants.Database.AnalyzerSession} " +
        $"SET lastActivityUtc = @0 " +
        $"WHERE sessionKey = @1 AND isActive = 1",
        newLastActivityUtc, sessionKey).ConfigureAwait(false);
    scope.Complete();
}
```

Returns `Task` (no post-update columns needed — the controller already has the StartUtc + PageviewCount from the resolver's projection on the cache-hit path, and from ResolveAsync's return on the cache-miss path).

**Rationale**: trivial implementation; the cost is 1 indexed UPDATE against `(visitorProfileKey, isActive)`-keyed rows. Idempotent (UPDATE against `isActive = 0` row is no-op — defends against the race where sweeper closed the session between ResolveAsync and TouchAsync; in that case the next custom-event call resolves a fresh session).

---

## §5 — Audit-log substrate

**Decision**: introduce `ICustomEventAuditor` + `CustomEventAuditor` (impl). Audit emit shape:

```csharp
public interface ICustomEventAuditor
{
    void Audit(
        VisitorIdentity actor,
        Guid eventKey,
        string category,
        string action,
        DateTimeOffset receivedUtc);
}

internal sealed class CustomEventAuditor : ICustomEventAuditor
{
    private readonly ILogger<CustomEventAuditor> _logger;
    public CustomEventAuditor(ILogger<CustomEventAuditor> logger) => _logger = logger;

    public void Audit(
        VisitorIdentity actor, Guid eventKey,
        string category, string action, DateTimeOffset receivedUtc)
    {
        _logger.LogInformation(
            "Audit: {AuditAction} by Actor={ActorUpn} ActorOid={ActorOid} " +
            "Target={EventKey} Category={Category} Action={Action} At={ReceivedUtc}",
            Constants.AuditLog.CustomEventCapture,
            actor.Upn, actor.Oid,
            eventKey, category, action, receivedUtc);
    }
}
```

`Constants.AuditLog.CustomEventCapture = "custom-event-capture"` per FR-008.

**Rationale**: structured logging with named properties — operator-side log-shipping captures all fields without parsing free-text. Slice 005's content-app actions will define their own audit action names + reuse the `ICustomEventAuditor` shape (rename to `IAnalyzerAuditor` then if generalised; for slice 004, the narrower name keeps coupling tight). The slice's Assumption "Audit-log substrate" pinned this — no `analyzerAuditLog` table.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Dedicated `analyzerAuditLog` table + write on every audit | Heavier; introduces a queryable surface that operators may or may not need at this point. Defer to a later slice if compliance demands it. |
| OpenTelemetry tracing instead of `ILogger` | More instrumentation work; ILogger is already wired everywhere and Customizer's slice-002 webhook audit uses the same pattern. |
| Emit a webhook through Customizer's outbox (`customEvent.captured`) | Cross-product event emission — slice 010+ territory per inter-product contract. |

---

## §6 — Payload validation

**Decision**: `CustomEventPayload` request DTO uses standard ASP.NET model binding + DataAnnotations:

```csharp
public sealed class CustomEventPayload
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Category { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Action { get; init; } = string.Empty;

    [StringLength(256)]
    public string? Label { get; init; }

    public decimal? Value { get; init; }
}
```

Controller action checks `ModelState.IsValid` → 400 with ProblemDetails if invalid. Additional manual checks for `Value` non-finiteness (DataAnnotations doesn't reject `decimal.MaxValue` precision overflow at bind time; `decimal?` is already non-NaN by type — `decimal` doesn't have NaN/Infinity; the JSON deserialiser will throw on `NaN`/`Infinity` literals). The spec's FR-007 "value non-finite" rule is enforced by System.Text.Json's strict float-literal parsing (defaults reject NaN/Infinity unless `JsonNumberHandling.AllowNamedFloatingPointLiterals` is set — confirm default in Umbraco's JSON options; if not strict, add a custom converter).

Whitespace-only handling: the `MinimumLength = 1` + `AllowEmptyStrings = false` check together with a manual `Category.Trim().Length > 0` guard in the action body.

**Rationale**: standard ASP.NET pattern; no new validation library; produces ProblemDetails-shape error responses out of the box.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| FluentValidation | New NuGet dep for trivial validation rules; DataAnnotations sufficient. |
| Manual validation in the action body without DataAnnotations | Misses ASP.NET's automatic 400 + ProblemDetails shape. |

---

## §7 — `analyzerCustomEvent` migration `M0003`

**Decision**: `M0003_AddAnalyzerCustomEventTable : AsyncMigrationBase`. Migration body (same shape as `M0002`):

```csharp
protected override Task MigrateAsync()
{
    var isSqlite = Database.DatabaseType.GetProviderName()?
        .Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    if (TableExists(Constants.Database.AnalyzerCustomEvent) is false)
    {
        Create.Table<AnalyzerCustomEventDto>().Do();

        if (!isSqlite)
        {
            // FK to customizerVisitorProfile.key
            Database.Execute(
                $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                $"ADD CONSTRAINT [FK_analyzerCustomEvent_VisitorProfile] " +
                $"FOREIGN KEY ([visitorProfileKey]) " +
                $"REFERENCES [customizerVisitorProfile]([key])");

            // FK to analyzerSession.sessionKey (hard FK — first Analyzer-to-Analyzer hard FK)
            Database.Execute(
                $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                $"ADD CONSTRAINT [FK_analyzerCustomEvent_Session] " +
                $"FOREIGN KEY ([sessionKey]) " +
                $"REFERENCES [{Constants.Database.AnalyzerSession}]([sessionKey])");

            // No FK on receiptKey — soft FK per spec
        }
    }

    return Task.CompletedTask;
}
```

NPoco `[Index]` attributes on the DTO handle the non-clustered indexes (`UX_analyzerCustomEvent_eventKey`, `IDX_analyzerCustomEvent_sessionKey`, `IDX_analyzerCustomEvent_visitorProfileKey`, `IDX_analyzerCustomEvent_receiptKey`, `IDX_analyzerCustomEvent_category_action`).

Chain in `AnalyzerMigrationPlan`:

```csharp
.To<M0003_AddAnalyzerCustomEventTable>("0003-AddAnalyzerCustomEventTable");
```

**Rationale**: matches `M0001` + `M0002` precedent. First Analyzer-to-Analyzer hard FK (`analyzerCustomEvent.sessionKey → analyzerSession.sessionKey`) — useful precedent for future Analyzer tables that depend on session existence.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Composite index on `(visitorProfileKey, sessionKey)` instead of two separate non-clustered indexes | The two access patterns (cascade-step deletes by visitor; reports group by session) have different leading keys; separate indexes are clearer + lower-write-overhead at this row volume. |
| FK on `receiptKey` (hard) instead of soft | Receipts are hard-deleted on slice-002 cascade; custom events are hard-deleted on slice-004 cascade — they go together so the FK would never break in practice. BUT: receipts may be dropped under back-pressure (slice-002 contract); a hard FK would fail in that case. Soft FK is the documented Analyzer convention for `receiptKey`/`pageviewKey` references. |

---

## §8 — Cascade step

**Decision**: `AnalyzerCustomEventCascadeStep : IAnonymizationCascadeStep` is an `internal sealed` class under `Analyzer.Features.CustomEvents.Application.Anonymization`. `ExecuteAsync(visitorProfileKey, ct)` delegates to `IAnalyzerCustomEventRepository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)` — single indexed DELETE.

Registered via `services.AddScoped<IAnonymizationCascadeStep, AnalyzerCustomEventCascadeStep>()` in `AnalyzerComposer` (third registration — alongside slice-002 receipt cascade + slice-003 session cascade).

**Rationale**: matches slice-002 receipt cascade shape. The atomic-rollback semantic (throw inside the cascade rolls outer scope) inherits from Customizer's `AnonymizeVisitorProfileHandler` orchestration unchanged.

**Alternatives considered**: same as slice-002 §3 — soft-delete + re-key both rejected for the same reasons (custom events are per-row engagement signals; aggregate preservation N/A).

---

## §9 — Public-surface pinning regen

**Decision**: regenerate baseline. The diff is purely additive:

1. NEW `TYPE Analyzer.Analytics.AnalyticsCustomEvent : sealed class` (record; 9 properties: EventKey, SessionKey, VisitorProfileKey, ReceiptKey?, Category, Action, Label?, Value?, ReceivedUtc).
2. NEW `PROP System.Collections.Generic.IReadOnlyList<Analyzer.Analytics.AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }` on `IAnalyticsEventStateProvider`.

Same `ANALYZER_REGENERATE_SNAPSHOTS=1` regen pattern as slice 002/003 (lesson #37). Spec.md Assumptions §"Public-surface pinning regeneration" is amended with the 2 specific diff lines per lesson #50's deliberate-baseline-change discipline.

---

## §10 — Integration-test substrate

**Decision**: reuse slice-002 `AnalyzerIntegrationTestBase` unchanged. New integration tests inherit from it; tagged `[Trait("Category", "Integration")]` for CI opt-out (lesson #31 / #32). The base's per-fixture migration run already picks up `M0003` once the plan chains it.

Slice 005 may need a `WebApplicationFactory`-backed variant to exercise the controller through HTTP. For slice 004, controller tests are split:

- **Unit**: `AnalyzerCustomEventControllerTests` — instantiate the controller directly; assert `IActionResult` shape (401 / 400 / 202).
- **Integration**: drive the controller via the `WebApplicationFactory` already set up in slice-002's `AnalyzerIntegrationTestBase` (which uses `Program` from `samples/Analyzer.Host`). POST against the actual route, validate end-to-end including authentication.

If the base doesn't already expose an HTTP client, extend it with one (small additive change).

---

## §11 — Reference inventory

Source files in `../customizer/src/Customizer/` referenced by this research:

| Path | Purpose in this slice |
|---|---|
| `Controllers/DocumentTypeSegmentation/...Controller.cs` | Management-API controller pattern (auth, anti-forgery, ProblemDetails) (§1). |
| `Features/Visitors/Application/Contracts/Anonymization/IAnonymizationCascadeStep.cs` | Cascade-step contract (§8). |
| `Middleware/PageviewCaptureMiddleware.cs` | Anti-forgery + auth integration precedent (§1). |
| `Features/Resolution/Pipeline/PersonalizationResolutionFilter.cs` | ExtractUserAgent pattern (§3 — controller mirrors it for in-request UA read). |

Files in `src/Analyzer/` referenced:

| Path | Purpose in this slice |
|---|---|
| `Composers/AnalyzerComposer.cs` | Composer wiring (§extend with new registrations). |
| `Features/Sessions/Application/IAnalyzerSessionResolver.cs` | Extended with `SessionActivityKind` parameter (§3). |
| `Features/Sessions/Application/AnalyzerSessionResolver.cs` | Internal switch on `SessionActivityKind` → Extend vs Touch (§3). |
| `Features/Sessions/Infrastructure/Persistence/IAnalyzerSessionRepository.cs` | New `TouchAsync` method (§4). |
| `Features/Sessions/Infrastructure/Persistence/AnalyzerSessionRepository.cs` | `TouchAsync` impl (§4). |
| `Features/Events/Application/AnalyticsEventStateStore.cs` | Extended with `_currentCustomEvents` + `AppendCustomEvent` (§Analytics surface). |
| `Analytics/IAnalyticsEventStateProvider.cs` | Extended with `CurrentRequestCustomEvents` (§Analytics surface). |
| `Analytics/AnalyticsEventStateProvider.cs` | Projection from store (§Analytics surface). |
| `Migrations/AnalyzerMigrationPlan.cs` | Chain M0003 (§7). |

No Customizer file is modified by this slice. All references are read-only.

---

## §12 — Open items deferred from research to `/speckit-tasks`

- Exact route prefix (placeholder `management/api/v1/analyzer/custom-event` proposed; slice-005 may pin a canonical Analyzer namespace).
- Exact JSON-deserialiser configuration for rejecting `NaN` / `Infinity` (default `JsonSerializerOptions` should reject; verify with a unit test).
- Specific Umbraco `[Authorize(Policy = ...)]` policy name (depends on Umbraco 17's standard backoffice-management-API policy; pin in implementation).
- Whether `ICustomEventAuditor` should be renamed to a generalised `IAnalyzerAuditor` now (for slice 005 reuse) or kept narrow (slice 005 can rename later) — proposed: keep narrow at slice 004; rename if slice 005 needs the broader contract.
