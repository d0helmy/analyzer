# Contract — `AnalyzerFormEventManagementController`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: management API — versioned + documented via OpenAPI per Constitution's Reporting & Open Surface section.

The single Umbraco backoffice management controller that accepts form lifecycle and field event POSTs from the client bundle. Mirrors slice-004's `AnalyzerCustomEventManagementController` four-corner Principle-VII gate.

## Routes

| Route | Method | Body | Returns |
|-------|--------|------|---------|
| `/umbraco/management/api/v1/analyzer/form-event/lifecycle` | POST | `AnalyzerFormEventPayload` | `202 { eventKey }` on success |
| `/umbraco/management/api/v1/analyzer/form-event/field` | POST | `AnalyzerFormFieldEventPayload` | `202 { eventKey }` on success |

## Payloads

```csharp
public sealed class AnalyzerFormEventPayload
{
    public Guid FormKey { get; set; }
    public Guid ContentKey { get; set; }
    public AnalyzerFormEventType EventType { get; set; }
    public int? ElapsedMsFromImpression { get; set; }
    public int? ElapsedMsFromStart { get; set; }
}

public sealed class AnalyzerFormFieldEventPayload
{
    public Guid FormKey { get; set; }
    public Guid FieldKey { get; set; }
    public AnalyzerFormFieldEventType EventType { get; set; }
    public bool? HadValue { get; set; }
}
```

Both serialised as JSON (System.Text.Json, Umbraco-default). Enums emitted as integers (matching `byte`-backed wire format).

**Privacy invariant**: the payload schema MUST NOT contain any property whose name suggests field content (`*Value`, `*Content`, `*Text`, etc.). Validator runs a name-pattern reject pass before deserialisation (defence in depth — model binder strips unknown properties, validator confirms).

## Principle-VII gates (mirroring slice 004)

1. **Backoffice auth**: `[Authorize(Policy = "BackOffice")]` on both routes; anonymous POST → 401.
2. **Anti-forgery**: enforced by Umbraco's management-API pipeline by convention; missing anti-forgery → 403 with no row written, no audit emitted.
3. **Payload validation**: model-binding + handler-level validation (see capture handler contract). Failures → 400 with no row written, no audit emitted.
4. **Audit**: emitted only on successful persistence (handler invokes auditor after repository insert).

## Status code matrix

| Outcome | Status | Body | Side effects |
|---------|--------|------|--------------|
| Authorised + valid payload + persisted | 202 | `{ "eventKey": "..." }` | Row inserted; state store appended; audit emitted |
| Anonymous | 401 | `{}` | None |
| Authenticated but no backoffice role | 403 | `{}` | None |
| Authenticated + invalid payload | 400 | `{ "errors": [...] }` (Problem Details) | None |
| Authenticated + visitor `IsAvailable=false` | 401 | `{}` | None |

## Conformance tests

Unit:
- `AnalyzerFormEventManagementControllerTests.LifecycleHappyPathReturns202`
- `AnalyzerFormEventManagementControllerTests.FieldHappyPathReturns202`
- `AnalyzerFormEventManagementControllerTests.LifecycleRejectsEmptyFormKey`
- `AnalyzerFormEventManagementControllerTests.LifecycleRejectsMismatchedTimingSlots`
- `AnalyzerFormEventManagementControllerTests.FieldRejectsHadValueOnFocus`

Integration (gated on issue #23 mgmt-API 404 in test host):
- `Integration/Forms/EndToEndCaptureTests.AnonymousPostReturns401`
- `Integration/Forms/EndToEndCaptureTests.AuthenticatedHappyPath`
- `Integration/Forms/EndToEndCaptureTests.AnonymousPostPersistsZeroRows`
