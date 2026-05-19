# Contract — `AnalyzerSearchEventCascadeStep`

**Feature**: `007-search-tracking`
**Date**: 2026-05-19
**Stability**: internal (cascade-step is a participation point in Customizer's `IAnonymizationCascadeStep` orchestrator; not exposed as a third-party extension surface). Discovered via Customizer's DI scan; no Customizer-side change required.

The single hard-delete cascade step that removes a visitor's `analyzerSearchEvent` rows during anonymisation. Participation pattern: **hard-delete** (matches Customizer's `GoalReachedCascadeStep` and slices 002/004/006). Runs inside the outer NPoco scope opened by Customizer's `AnonymizationOrchestrator`, so the DELETE rolls back atomically with the visitor-profile re-key + every other step.

## Cascade-disposition divergence from contract D8

The inter-product contract §3 D8 lists `analyzerSearchEvent` with disposition "re-key to anonymised visitor key". This slice ships **hard-delete** instead. The divergence is intentional:

- FR-SRC-04 flags search queries as PII (potentially names of colleagues, sensitive topics).
- Re-keying retains the literal `rawQuery` + `normalisedQuery` strings attached to a pseudonymous identifier — still a record of "this person searched for $X" from any informed adversary's standpoint.
- CCPA/CPRA right-to-delete cannot be satisfied by re-keying for PII-bearing rows.
- Slice-004 (custom events) and slice-006 (scroll samples) established hard-delete precedent for per-row engagement signals with no aggregate-load-bearing role; search is the same class with strictly stronger PII sensitivity.
- Principle IV v1.1.1's participation-pattern menu (delete / soft-delete / re-projection) explicitly authorises per-table choice — pinned in this slice's `plan.md` Constitution Check under Principle IV.

**Contract follow-up**: amend `docs/INTER-PRODUCT-CONTRACT.md` §3 D8 row for `analyzerSearchEvent` from "re-key" to "hard-delete (PII per FR-SRC-04)" — flagged for inclusion in the slice-007 PR description; can land Analyzer-side alone since contract D8 is an Analyzer-owned doc.

## Signature

```csharp
namespace Analyzer.Features.Search.Application.Anonymization;

public sealed class AnalyzerSearchEventCascadeStep : IAnonymizationCascadeStep
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

2. **Operation**: issue a single `DELETE FROM analyzerSearchEvent WHERE visitorProfileKey = @0` against the ambient scope's database, with `context.VisitorProfileKey` as `@0`. The repository method `IAnalyzerSearchEventRepository.DeleteByVisitorAsync(visitorProfileKey, ct)` wraps this.

3. **Indexing**: the DELETE uses `IDX_analyzerSearchEvent_visitor` (single-column NCI on `visitorProfileKey`) as the seek predicate. SC-004 budget: 1 000 rows in ≤ 200 ms.

4. **Idempotency**: zero-row DELETE is a no-op (does not throw); running the step twice for the same visitor is safe (matches Customizer's expectation of cascade-step replay during retried anonymisation jobs).

5. **Atomic-rollback expectation**: if any subsequent cascade step throws within the same outer scope, the orchestrator's outer `Scope.Complete()` is never called, and the DELETE rolls back. The integration test `CascadeRollbackTests` verifies this.

## Properties

- **`Order`**: the next available `int` after slice-006's `AnalyzerScrollSampleCascadeStep`. Locked at impl time to the next-after-slice-006 value; documented in the slice's `tasks.md` step that creates the class.

- **`Description`**: `"Hard-deletes analyzerSearchEvent rows for the anonymised visitor (PII per FR-SRC-04)."` — human-readable; surfaced in the anonymisation audit-trail. The PII rationale is included in the description so operators reading the audit trail can see why this step deletes rather than re-keys.

## DI lifetime

Registered as **Transient** (cascade steps are short-lived, invoked once per anonymisation; matches slice 004/005/006 cascade-step registration). One instance per orchestrator pass.

## Registration

In `AnalyzerSearchComposer.Compose(IUmbracoBuilder builder)`:

```csharp
builder.WithCollectionBuilder<AnonymizationCascadeStepCollectionBuilder>()
       .Append<AnalyzerSearchEventCascadeStep>();
```

`AnonymizationCascadeStepCollectionBuilder` is Customizer's existing builder; appending registers the step into the orchestrator's enumeration. No Customizer source change.

This is the **seventh** registered cascade step (after slice-002 receipts, slice-003 sessions, slice-004 custom events, slice-005's two form-event tables, and slice-006 scroll samples). The cascade-step collection grows additively; the orchestrator's enumeration order is determined by `Order` values, not registration order.

## Conformance tests

Unit tests (`AnalyzerSearchEventCascadeStepTests`):

- Zero-row visitor → repository called once, no exception thrown.
- 100-row visitor → repository called once, returns affected row count, no exception.
- Repository throws → exception bubbled (orchestrator decides retry semantics).

Integration tests (`CascadeHardDeleteTests`, `CascadeRollbackTests`):

- Insert N rows for visitor V, invoke orchestrator's anonymisation for V, assert zero `analyzerSearchEvent` rows remain for V; rows for *other* visitors untouched. Latency assertion for N=1 000.
- Insert N rows for visitor V, register a sentinel cascade step that throws after `AnalyzerSearchEventCascadeStep`, invoke orchestrator's anonymisation for V; assert all N rows still exist (outer scope rolled back).
- **PII-cleanup verification**: after a successful cascade, no row referencing the anonymised visitor's `VisitorProfileKey` exists in `analyzerSearchEvent`; a `SELECT COUNT(*) FROM analyzerSearchEvent WHERE rawQuery LIKE @uniqueQuerySubstring` returns 0 for the visitor's unique seed query (proves the literal query text is gone, not just the link).
