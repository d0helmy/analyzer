# Contract — `AnalyzerEventReceiptCascadeStep`

**Feature**: `002-pageview-subscription`
**Date**: 2026-05-18
**Stability**: internal (not part of the pinned surface; per slice 002 Clarifications Q3). Behaviour is contract-bound by Customizer's `IAnonymizationCascadeStep` interface and by spec FR-006 + US2.

The Analyzer-side cascade step that participates in Customizer's `AnonymizeVisitorProfileCommand` atomic transaction. **Hard-deletes** `analyzerEventReceipt` rows for the supplied visitor profile key — matching Customizer's `GoalReachedCascadeStep` precedent (see `research.md` §3 for the why).

## Type

```
Analyzer.Features.Events.Application.Anonymization.AnalyzerEventReceiptCascadeStep
  : Customizer.Features.Visitors.Application.Contracts.Anonymization.IAnonymizationCascadeStep
```

| Aspect | Value |
|---|---|
| **Visibility** | `internal sealed` (per Customizer's `GoalReachedCascadeStep` precedent) |
| **DI lifetime** | **Scoped** (per Customizer's `IAnonymizationCascadeStep` registration convention; the orchestrator resolves cascade steps inside its outer scope) |
| **Composition site** | `AnalyzerComposer.Compose` — `builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerEventReceiptCascadeStep>();` |
| **Discoverability** | Automatic — Customizer's `AnonymizeVisitorProfileHandler` ctor takes `IEnumerable<IAnonymizationCascadeStep>` and iterates every registered impl. Analyzer's registration is sufficient; no manual binding to Customizer's orchestrator. |

## Dependencies

```csharp
public AnalyzerEventReceiptCascadeStep(IAnalyzerEventReceiptRepository repository);
```

| Dep | Role |
|---|---|
| `IAnalyzerEventReceiptRepository` | Performs the actual delete. The repository's implementation opens an Umbraco `IScopeProvider.CreateScope()` for its DB call, which enlists in the outer scope opened by Customizer's `AnonymizeVisitorProfileHandler`. The nested scope's lifetime is the repository call duration. |

No logger, no `TimeProvider`, no other infrastructure — matching `GoalReachedCascadeStep`'s minimal shape.

## Method shape

```csharp
public Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct) =>
    _repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct);
```

Single line. No null-checks (the orchestrator guarantees a non-empty `visitorProfileKey` — the calling `AnonymizeVisitorProfileCommand` validates it upstream).

## Underlying SQL

The repository's `DeleteByVisitorKeyAsync` issues:

```sql
DELETE FROM analyzerEventReceipt
WHERE visitorProfileKey = @visitorProfileKey;
```

The `IDX_analyzerEventReceipt_visitorProfileKey` non-unique index (`data-model.md` §1) supports the predicate; under SC-003 (`≤ 10 000 rows in ≤ 200 ms`) on a reasonable SQL Server this is one indexed range delete.

The repository never returns the affected-row count out of `ExecuteAsync` (the contract returns `Task`, not `Task<int>`). If a future slice wants drop-counter observability, it's added through a metric counter, not through the contract return value.

## Behaviour matrix

| Scenario | Pre-condition | Effect | Outcome |
|---|---|---|---|
| Visitor has receipts | One or more `analyzerEventReceipt` rows reference `@visitorProfileKey` | Rows deleted in the outer scope's transaction | Step returns `Task.CompletedTask` after the DELETE awaits |
| Visitor has zero receipts | No rows reference `@visitorProfileKey` (visitor never produced a pageview, or already anonymised) | DELETE matches 0 rows; no error | Step returns `Task.CompletedTask`; no log emitted |
| Repository throws | DB connectivity issue, deadlock, etc. | Exception propagates through `ExecuteAsync` | Customizer's outer scope is disposed without `Complete()`, rolling back the whole anonymisation atomically (per `IAnonymizationCascadeStep` contract remark "Throwing rolls back the outer transaction unconditionally") |
| `CancellationToken` triggered | Operator cancels the anonymisation command mid-step | Repository propagates `OperationCanceledException` | Outer scope rolls back; orchestrator surfaces the cancel to the operator |

## Atomic-rollback proof

`AnonymizeVisitorProfileHandler` (Customizer) — relevant excerpt for context:

```csharp
using var scope = _scopeProvider.CreateScope();
// ... read existing profile, check rowVersion ...
foreach (var step in _cascadeSteps)
{
    await step.ExecuteAsync(command.VisitorKey, cancellationToken);
}
// If we get here, all cascade steps succeeded. Now overwrite IdentityRef.
var anonymised = _profiles.Anonymize(...);
// ... audit log + outbox enqueue ...
scope.Complete();   // Only call to Complete; without it, scope rolls back on Dispose.
```

If `AnalyzerEventReceiptCascadeStep.ExecuteAsync` throws (or any other registered step throws), the `foreach` exits via exception, `scope.Complete()` is never called, and `scope.Dispose()` rolls back the outer NPoco transaction — including:

- Any deletes Analyzer's step had already performed against `analyzerEventReceipt`.
- Any deletes prior steps had performed against their own tables.
- Customizer's visitor-row overwrite never occurs.

This is the load-bearing US2 AS2 assertion — verified by the integration test `CascadeRollbackTests.ThrowFromAnalyzerStepRollsBackEverything`.

## Behaviour-compatible custom implementations

This step is **internal-by-convention**, not a public extension surface. Third parties wanting Analyzer-receipt-aware anonymisation behaviour MUST register their own `IAnonymizationCascadeStep` (a separate registration; Customizer's orchestrator runs every registered impl). They MUST NOT replace `AnalyzerEventReceiptCascadeStep` itself — doing so would skip the receipt-delete step entirely, leaving stale receipt rows after anonymisation. The hard-delete behaviour is non-negotiable.

## Tests proving conformance

In `src/Analyzer.Tests/Unit/Features/Events/Application/Anonymization/AnalyzerEventReceiptCascadeStepTests.cs`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `ZeroRowsIsNoOp` | Step against a visitor with no receipts returns success; repository called with the right key. | US2 AS3 |
| `DelegatesToRepositoryWithSuppliedKey` | The repository's `DeleteByVisitorKeyAsync` is called with the exact `visitorProfileKey` and propagates the `CancellationToken`. | (Unit-level contract conformance) |

In `src/Analyzer.Tests/Integration/Anonymization/`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `CascadeDeleteTests.AnonymisationDeletesReceiptsForOneVisitorOnly` | Receipts for visitor A are deleted; receipts for visitor B unchanged. | US2 AS1 / SC-003 |
| `CascadeDeleteTests.PostAnonymisationCountIsZero` | Row count for `visitorProfileKey = A` is exactly `0` after `AnonymizeVisitorProfileCommand`. | SC-003 |
| `CascadeDeleteTests.CompletesUnderTwoHundredMsForTenThousandRows` | Step completes in ≤ 200 ms for a 10 k-row seed. | SC-003 budget |
| `CascadeRollbackTests.ThrowFromAnalyzerStepRollsBackEverything` | A test-only step registered to throw causes the entire anonymisation to roll back: Customizer's visitor row is not overwritten, Analyzer's deletes are undone. | US2 AS2 |
