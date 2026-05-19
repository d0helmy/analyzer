# Contract — `IAnalyzerScrollEventCaptureHandler`

**Feature**: `006-scroll-tracking`
**Date**: 2026-05-19
**Stability**: internal (the handler contract is not part of the pinned public surface; consumed only by Analyzer's own management controller and tests).

The single capture handler that takes a domain `AnalyzerScrollEventCapture` command, runs the Principle-VII gates (identity + payload validation), persists the row via the repository (idempotency enforced by the DB unique index), pushes to `IAnalyticsEventStateProvider`'s per-request store, emits the audit-log entry, and returns the persisted `EventKey`.

## Signature

```csharp
internal interface IAnalyzerScrollEventCaptureHandler
{
    Task<Guid> HandleAsync(AnalyzerScrollEventCapture command, CancellationToken ct);
}
```

## Behavioural contract

1. **Identity gate** (FR-008): if `command.Actor.IsAvailable` is false OR `command.Actor.Key == Guid.Empty`, throw `UnauthorizedAccessException` (controller maps to 401/403, persists zero rows).
2. **Payload validation** (FR-006, FR-007):
   - `command.PageviewKey != Guid.Empty` — required for FK + idempotency invariant.
   - `command.ContentKey != Guid.Empty`.
   - `command.Bucket` is a defined enum value — exactly one of `Quarter` (25), `Half` (50), `ThreeQuarters` (75), `Full` (100).
   - Violations throw `AnalyzerScrollPayloadValidationException` → controller maps to 400.
3. **Session resolution** (FR-007): resolve current session via slice-003's `IAnalyzerSessionResolver.ResolveAsync(command.Actor.Key, command.UserAgent, SessionActivityKind.ScrollEvent, command.ReceivedUtc, ct)`. Scroll milestones advance `lastActivityUtc` via `TouchAsync` (consistent with slice-004 / slice-005 "intentional engagement" model — visitor reaching a depth bucket is a deliberate signal of attention).
4. **Persistence**: insert the `AnalyzerScrollSampleDto` via `IAnalyzerScrollSampleRepository.InsertAsync`. The repository catches `SqlException` numbered `(2601, 2627)` on `UX_analyzerScrollSample_pageviewBucket` and re-throws `ScrollSampleDuplicateException` (handler bubbles to controller → 409, audit-tagged `Duplicate`).
5. **State exposure**: on a successful insert, append the equivalent `AnalyticsScrollSample` to `AnalyticsEventStateStore.CurrentRequestScrollEvents` (additive; never null).
6. **Audit**: invoke `IAnalyzerScrollEventAuditor.Audit(actor, eventKey, pageviewKey, bucket, receivedUtc)`. Always after a successful persist; never before. On duplicate (409), audit is tagged `Duplicate` and a separate log entry is emitted (R8).
7. **Return**: the persisted `EventKey`.

## Domain command shape

```csharp
namespace Analyzer.Features.Scroll.Domain;

internal sealed record AnalyzerScrollEventCapture
{
    public required VisitorIdentity Actor { get; init; }
    public required Guid PageviewKey { get; init; }
    public required Guid ContentKey { get; init; }
    public required AnalyzerScrollBucket Bucket { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
    public string? UserAgent { get; init; }
}
```

`VisitorIdentity` is the existing slice-002 identity-projection type returned by `IVisitorIdentifier.Resolve()`.

## DI lifetime

Registered as **Scoped** (matches slice 004's `ICustomEventCaptureHandler` and slice 005's `IAnalyzerFormEventCaptureHandler`). One instance per request scope; safe to inject scoped dependencies (`IAnalyzerSessionResolver`, `IAnalyzerScrollSampleRepository`, `AnalyticsEventStateStore`).

## Conformance tests

Unit tests (under `src/Analyzer.Tests/Unit/Features/Scroll/Application/AnalyzerScrollEventCaptureHandlerTests.cs`) MUST cover:

- Anonymous actor → `UnauthorizedAccessException` thrown; repository never invoked; auditor never invoked.
- `Guid.Empty` key → same.
- Invalid `Bucket` value (e.g. `(AnalyzerScrollBucket)42`) → `AnalyzerScrollPayloadValidationException`; no insert, no audit.
- Empty `PageviewKey` → validation exception; no insert.
- Happy path → `Insert` called once, `AppendScrollEvent` called once, `Audit` called once with matching `EventKey`; return value equals the persisted `EventKey`.
- Duplicate row (repository throws `ScrollSampleDuplicateException`) → exception bubbled; auditor invoked with `Duplicate` tag exactly once; state store NOT appended (no row landed).
