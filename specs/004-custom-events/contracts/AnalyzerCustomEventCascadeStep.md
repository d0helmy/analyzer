# Contract — `AnalyzerCustomEventCascadeStep`

**Feature**: `004-custom-events`
**Date**: 2026-05-18
**Stability**: internal. Implements Customizer's public `IAnonymizationCascadeStep` extension contract.

Third Analyzer-registered cascade step (alongside slice-002 receipt hard-delete + slice-003 session soft-anonymise). Hard-deletes the visitor's `analyzerCustomEvent` rows inside Customizer's outer NPoco scope on operator-triggered anonymisation.

## Namespace

```
Analyzer.Features.CustomEvents.Application.Anonymization.AnalyzerCustomEventCascadeStep
```

## Shape

```csharp
namespace Analyzer.Features.CustomEvents.Application.Anonymization;

internal sealed class AnalyzerCustomEventCascadeStep : IAnonymizationCascadeStep
{
    public AnalyzerCustomEventCascadeStep(
        IAnalyzerCustomEventRepository repository,
        ILogger<AnalyzerCustomEventCascadeStep> logger);

    public Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct);
}
```

## DI registration

| Aspect | Value |
|---|---|
| **Lifetime** | **Scoped** — matches slice-002 receipt cascade + slice-003 session cascade. |
| **Implementation** | `Analyzer.Features.CustomEvents.Application.Anonymization.AnalyzerCustomEventCascadeStep` (internal sealed) |
| **Composition site** | `AnalyzerComposer.Compose` — `services.AddScoped<IAnonymizationCascadeStep, AnalyzerCustomEventCascadeStep>();` (third registration alongside slice-002 + slice-003 cascade steps) |

## Operation (normative)

```
1. if visitorProfileKey == Guid.Empty:
     log.Debug("AnalyzerCustomEventCascadeStep called with empty key; skipping")
     return

2. await repository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)
   -- single indexed DELETE inside Customizer's outer NPoco scope:
   --   DELETE FROM analyzerCustomEvent WHERE visitorProfileKey = @0;

3. log.Information(
     "AnalyzerCustomEvent cascade-delete completed for VisitorProfileKey={VisitorKey}",
     visitorProfileKey)
```

## Determinism / idempotence

- Re-run on the same visitor key → zero rows affected on the second invocation (rows already deleted); no error.
- Zero-row visitor → DELETE matches zero rows; no error, no warning at info level.

## Atomic rollback

When the outer `AnonymizeVisitorProfileHandler` scope does NOT call `Complete()` (downstream cascade step throws), the DELETE rolls back atomically — `analyzerCustomEvent` rows revert (re-appear). Slice-002 receipts also revert; slice-003 sessions revert from soft-anonymised state; Customizer's visitor row is not overwritten. All four states are consistent post-rollback.

## Thread safety

- Single-threaded per `AnonymizeVisitorProfileCommand` invocation (Customizer's orchestrator serialises cascade steps).
- The repository's nested `IScopeProvider.CreateScope()` enlists in the outer transaction; SQL Server serialises the row-level DELETE.

## Tests proving conformance

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `AnalyzerCustomEventCascadeStepTests.HardDeletesVisitorRowsOnly` | DELETE called with `visitorProfileKey = A`; visitor B's rows untouched. | US2 AS1 |
| `AnalyzerCustomEventCascadeStepTests.ZeroRowNoOp` | Visitor with no custom events → DELETE matches 0 rows; no error. | US2 AS3 |
| `AnalyzerCustomEventCascadeStepTests.EmptyVisitorKey_short_circuits` | Guid.Empty → repo not called. | (defensive) |
| Integration: `CascadeHardDeleteTests.HardDeleteInsideOuterScope` | End-to-end through `AnonymizeVisitorProfileCommand`; A's custom events deleted; A's receipts deleted (slice-002); A's sessions soft-anonymised (slice-003); B untouched. | US2 AS1 |
| Integration: `CascadeRollbackTests.CascadeThrowRollsBackAllSteps` | Inject a throwing cascade step; A's custom events revert + receipts revert + sessions revert + visitor row not overwritten. | US2 AS2 |

## Versioning

Internal; replaceable through `services.Replace(...)`. The Customizer-published `IAnonymizationCascadeStep` interface is the pinned contract.
