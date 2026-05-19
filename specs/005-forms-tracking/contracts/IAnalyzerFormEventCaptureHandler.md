# Contract — `IAnalyzerFormEventCaptureHandler` + `IAnalyzerFormFieldEventCaptureHandler`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: internal (handler contracts are not part of the pinned public surface; consumed only by Analyzer's own management controllers and tests).

The two capture handlers that take a domain `Capture` command, run the Principle-VII gates (identity + payload validation), persist the row via the repository, push to `IAnalyticsEventStateProvider`'s per-request store, emit the audit-log entry, and return the persisted `EventKey`.

## Signatures

```csharp
internal interface IAnalyzerFormEventCaptureHandler
{
    Task<Guid> HandleAsync(AnalyzerFormEventCapture command, CancellationToken ct);
}

internal interface IAnalyzerFormFieldEventCaptureHandler
{
    Task<Guid> HandleAsync(AnalyzerFormFieldEventCapture command, CancellationToken ct);
}
```

## Behavioural contract — `IAnalyzerFormEventCaptureHandler`

1. **Identity gate** (FR-014): if `command.Actor.IsAvailable` is false OR `command.Actor.Key == Guid.Empty`, throw `UnauthorizedAccessException` (controller maps to 401/403, persists zero rows).
2. **Payload validation** (FR-006, FR-007): `command.FormKey != Guid.Empty`. `command.ContentKey != Guid.Empty`. `command.EventType` valid enum value. Timing slot consistency:
   - `EventType == Start` → `ElapsedMsFromImpression` non-null and ≥ 0.
   - `EventType ∈ { Success, Abandon }` → `ElapsedMsFromStart` non-null and ≥ 0.
   - `EventType == Impression` → both elapsed slots null.
   - Violations throw `AnalyzerFormPayloadValidationException` → controller maps to 400.
3. **Session resolution** (FR-003 / FR-011): resolve current session via slice-003's `IAnalyzerSessionResolver.ResolveAsync(command.Actor.Key, command.UserAgent, SessionActivityKind.FormEvent, command.ReceivedUtc, ct)`. The resolver:
   - `Impression` → does NOT advance `lastActivityUtc` (consistent with Edge Case "Impressions are passive"); only resolves the current session for FK linkage.
   - `Start` / `Success` → advances via `TouchAsync` (consistent with slice 004 custom-event behaviour).
   - `Abandon` → never enters this handler (sweeper-materialised, not POST-driven).
4. **Persistence**: insert the `AnalyzerFormEventDto` via `IAnalyzerFormEventRepository.InsertAsync`.
5. **State exposure**: append the equivalent `AnalyticsFormEvent` to `AnalyticsEventStateStore.CurrentRequestFormEvents` (additive; never null).
6. **Audit**: invoke `IAnalyzerFormEventAuditor.Audit(actor, eventKey, formKey, eventType, receivedUtc)`. Always after a successful persist; never before.
7. **Return**: the persisted `EventKey`.

Same contract structure for `IAnalyzerFormFieldEventCaptureHandler`, with:
- Field-level identity / payload validation (`FieldKey != Guid.Empty`; `HadValue` consistency with `EventType`).
- Field-level audit (carrying `FieldKey` + `HadValue` in the audit-log scope).
- Field events `FieldFocus` does not advance `lastActivityUtc`; `FieldUnfocus` does (consistent with the "intentional engagement" model from slice 004).

## DI lifetime

Both handlers registered as **Scoped** (matches slice 004's `ICustomEventCaptureHandler`). One instance per request scope; safe to inject scoped dependencies (`IAnalyzerSessionResolver`, `IAnalyzerFormEventRepository`, `AnalyticsEventStateStore`).

## Conformance tests

| Conformance | Test class |
|---|---|
| Rejects `Guid.Empty` visitor with `UnauthorizedAccessException` | `AnalyzerFormEventCaptureHandlerTests.RejectsEmptyVisitor` |
| Rejects mismatched timing slots with `AnalyzerFormPayloadValidationException` | `AnalyzerFormEventCaptureHandlerTests.RejectsMismatchedTimingSlots` |
| Inserts row + appends to state store on happy path | `AnalyzerFormEventCaptureHandlerTests.HappyPathInsertsAndAppendsState` |
| Audit emits exactly once on success, zero on rejection | `AnalyzerFormEventCaptureHandlerTests.AuditEmittedOnceOnSuccess` |
| Session resolver invoked with correct `SessionActivityKind` per `EventType` | `AnalyzerFormEventCaptureHandlerTests.SessionActivityDispatch` |

Symmetric coverage for `AnalyzerFormFieldEventCaptureHandlerTests`.
