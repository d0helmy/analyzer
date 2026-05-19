# Contract — `AnalyzerFormEventCascadeStep` + `AnalyzerFormFieldEventCascadeStep`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: internal class, public contract surface is the implemented `Customizer.Features.Visitors.Application.Contracts.Anonymization.IAnonymizationCascadeStep`.

Both cascade steps participate in Customizer's anonymisation orchestrator (contract §3 D5) by hard-deleting their respective table's rows for the visitor being anonymised, inside Customizer's outer NPoco scope (atomic rollback on later-step failure).

## Signatures

```csharp
internal sealed class AnalyzerFormEventCascadeStep : IAnonymizationCascadeStep
{
    public Task ExecuteAsync(Guid visitorKey, CancellationToken ct);
}

internal sealed class AnalyzerFormFieldEventCascadeStep : IAnonymizationCascadeStep
{
    public Task ExecuteAsync(Guid visitorKey, CancellationToken ct);
}
```

## Behavioural contract

1. **Single-statement DELETE**: each step issues exactly one `DELETE FROM <table> WHERE visitorProfileKey = @0` via the repository's `DeleteByVisitorKeyAsync`. No row-by-row iteration (FR-010, SC-004 200ms budget for 1000 rows).
2. **Outer-scope participation**: the repository uses `IScopeProvider.CreateScope()` WITHOUT calling `scope.Complete()` if the orchestrator's outer scope has not yet committed — leveraging NPoco's nested-scope semantics (slice 002's `AnalyzerEventReceiptCascadeStep` precedent).
3. **Idempotency**: re-running the step for the same visitor key (zero rows match) is a no-op. No throw, no log.
4. **Atomic rollback**: if any later cascade step throws inside the outer scope, both DELETEs are reversed when the outer scope disposes without `Complete()` (verified by `Integration/Forms/CascadeRollbackTests`).

## DI lifetime

Both steps registered as **Singleton** (matches slice 002 + slice 004 precedent — no per-request state). Resolved via `IEnumerable<IAnonymizationCascadeStep>` by Customizer's orchestrator.

## Registration order

Per inter-product contract §3 D5, cascade-step ordering is NOT guaranteed. Both Forms cascade steps MUST be commutative w.r.t. each other and w.r.t. existing slice 002/003/004 cascade steps. Hard-delete is commutative (no rows to be in inconsistent state). ✓

## Conformance tests

| Conformance | Test class |
|---|---|
| Deletes rows for the targeted visitor only | `Integration/Forms/CascadeHardDeleteTests.DeletesTargetVisitorOnly` (×2 — one per table) |
| Completes within 200 ms for 1000 rows | `Integration/Forms/CascadeHardDeleteTests.CompletesUnderTwoHundredMsForOneThousandRows` (×2) |
| Zero-row visitor is a no-op | `Integration/Forms/CascadeHardDeleteTests.ZeroRowNoOp` (×2) |
| Rolls back on outer-scope dispose-without-complete | `Integration/Forms/CascadeRollbackTests.ThrowAfterStepRollsBackTheDelete` (×2) |
