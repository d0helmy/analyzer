# Contract — `AnalyzerScrollSampleCascadeStep`

**Feature**: `006-scroll-tracking`
**Date**: 2026-05-19
**Stability**: internal (cascade-step is a participation point in Customizer's `IAnonymizationCascadeStep` orchestrator; not exposed as a third-party extension surface). Discovered via Customizer's DI scan; no Customizer-side change required.

The single hard-delete cascade step that removes a visitor's `analyzerScrollSample` rows during anonymisation. Participation pattern: **hard-delete** (matches Customizer's `GoalReachedCascadeStep` and slices 002/004/005). Runs inside the outer NPoco scope opened by Customizer's `AnonymizationOrchestrator`, so the DELETE rolls back atomically with the visitor-profile re-key + every other step.

## Signature

```csharp
namespace Analyzer.Features.Scroll.Application.Anonymization;

public sealed class AnalyzerScrollSampleCascadeStep : IAnonymizationCascadeStep
{
    public int Order { get; }
    public string Description { get; }
    public Task ExecuteAsync(AnonymizationContext context, CancellationToken cancellationToken);
}
```

## Behavioural contract

1. **Inputs**: `AnonymizationContext` (Customizer-pinned) carries:
   - `VisitorProfileKey: Guid` — the visitor being anonymised (the SAME key on the profile row; Customizer re-keys `IdentityRef` to `anonymized:<guid>` separately).
   - An ambient NPoco `IScope` accessible via DI — the cascade step MUST use this scope, NOT open a new one.

2. **Operation**: issue a single `DELETE FROM analyzerScrollSample WHERE visitorProfileKey = @0` against the ambient scope's database, with `context.VisitorProfileKey` as `@0`. The repository method `IAnalyzerScrollSampleRepository.DeleteByVisitorAsync(visitorProfileKey, ct)` wraps this.

3. **Indexing**: the DELETE uses `IDX_analyzerScrollSample_visitor` (single-column NCI on `visitorProfileKey`) as the seek predicate. SC-004 budget: 1 000 rows in ≤ 200 ms.

4. **Idempotency**: zero-row DELETE is a no-op (does not throw); running the step twice for the same visitor is safe (matches Customizer's expectation of cascade-step replay during retried anonymisation jobs).

5. **Atomic-rollback expectation**: if any subsequent cascade step throws within the same outer scope, the orchestrator's outer `Scope.Complete()` is never called, and the DELETE rolls back. The integration test `CascadeRollbackTests` verifies this.

## Properties

- **`Order`**: the next available `int` after slice-005's two cascade steps (`AnalyzerFormEventCascadeStep` and `AnalyzerFormFieldEventCascadeStep`). Locked at impl time to the next-after-slice-005 value; documented in the slice's `tasks.md` step that creates the class.

- **`Description`**: `"Hard-deletes analyzerScrollSample rows for the anonymised visitor."` — human-readable; surfaced in the anonymisation audit-trail.

## DI lifetime

Registered as **Transient** (cascade steps are short-lived, invoked once per anonymisation; matches slice 005's cascade-step registration). One instance per orchestrator pass.

## Registration

In `AnalyzerScrollComposer.Compose(IUmbracoBuilder builder)`:

```csharp
builder.WithCollectionBuilder<AnonymizationCascadeStepCollectionBuilder>()
       .Append<AnalyzerScrollSampleCascadeStep>();
```

`AnonymizationCascadeStepCollectionBuilder` is Customizer's existing builder; appending registers the step into the orchestrator's enumeration. No Customizer source change.

## Conformance tests

Unit tests (`AnalyzerScrollSampleCascadeStepTests`):

- Zero-row visitor → repository called once, no exception thrown.
- 100-row visitor → repository called once, returns affected row count, no exception.
- Repository throws → exception bubbled (orchestrator decides retry semantics).

Integration tests (`CascadeHardDeleteTests`, `CascadeRollbackTests`):

- Insert N rows for visitor V, invoke orchestrator's anonymisation for V, assert zero `analyzerScrollSample` rows remain for V; rows for *other* visitors untouched. Latency assertion for N=1 000.
- Insert N rows for visitor V, register a sentinel cascade step that throws after `AnalyzerScrollSampleCascadeStep`, invoke orchestrator's anonymisation for V; assert all N rows still exist (outer scope rolled back).
