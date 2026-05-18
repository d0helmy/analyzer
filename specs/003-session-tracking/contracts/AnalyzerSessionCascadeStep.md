# Contract — `AnalyzerSessionCascadeStep`

**Feature**: `003-session-tracking`
**Date**: 2026-05-18
**Stability**: internal. Implements Customizer's public `IAnonymizationCascadeStep` extension contract.

The cascade step that soft-anonymises a visitor's `analyzerSession` rows when Customizer's `AnonymizeVisitorProfileHandler` runs. The second `IAnonymizationCascadeStep` registration Analyzer ships, alongside slice-002's `AnalyzerEventReceiptCascadeStep`. Distinct in semantic — receipts hard-delete, sessions soft-anonymise (Principle IV v1.1.1 per-table choice; spec Assumption #2).

## Namespace

```
Analyzer.Features.Sessions.Application.Anonymization.AnalyzerSessionCascadeStep
```

Mirrors the slice-002 cascade-step location (`Analyzer.Features.Events.Application.Anonymization.AnalyzerEventReceiptCascadeStep`).

## Shape

```csharp
namespace Analyzer.Features.Sessions.Application.Anonymization;

internal sealed class AnalyzerSessionCascadeStep : IAnonymizationCascadeStep
{
    public AnalyzerSessionCascadeStep(
        IAnalyzerSessionRepository repository,
        AnalyzerSessionCacheStore cacheStore,
        ILogger<AnalyzerSessionCascadeStep> logger);

    /// <summary>
    /// Customizer's contract entry point. Runs inside the outer
    /// <c>AnonymizeVisitorProfileHandler</c> NPoco scope.
    /// </summary>
    public Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct);
}
```

The interface `IAnonymizationCascadeStep` is published by Customizer at `Customizer.Features.Visitors.Application.Contracts.Anonymization` (slice-007 contract; pinned on Customizer's side). Analyzer consumes it as an extension surface — Principle III + inter-product contract §4.

## DI registration

| Aspect | Value |
|---|---|
| **Lifetime** | **Scoped** — matches `IAnonymizationCascadeStep` convention (slice-002 precedent). Customizer's orchestrator resolves cascade steps from its own scope; the scoped lifetime ensures both registrations enlist in the same outer scope. |
| **Implementation** | `Analyzer.Features.Sessions.Application.Anonymization.AnalyzerSessionCascadeStep` (internal sealed) |
| **Composition site** | `AnalyzerComposer.Compose` — `services.AddScoped<IAnonymizationCascadeStep, AnalyzerSessionCascadeStep>();` registered alongside slice-002's `AnalyzerEventReceiptCascadeStep`. Customizer's orchestrator collects ALL registered `IAnonymizationCascadeStep` instances (via constructor `IEnumerable<IAnonymizationCascadeStep>`) and runs them in registration order. |

## Behavior

### Inputs

| Field | Type | Constraint |
|---|---|---|
| `visitorProfileKey` | `Guid` | The visitor profile being anonymised — supplied by Customizer's orchestrator. May be `Guid.Empty` in malformed-test cases; defensively short-circuited (zero-row no-op). |
| `ct` | `CancellationToken` | Propagated through to the repository. Cancellation between batch UPDATE statements is supported. |

### Outputs

`Task` — no return value. The cascade step's effect is observable in the `analyzerSession` table state.

### Operation (normative)

```
1. if visitorProfileKey == Guid.Empty:
     log.Debug("AnalyzerSessionCascadeStep called with empty visitorProfileKey; skipping")
     return

2. var affectedSessionKeys = await repository.SoftAnonymizeByVisitorKeyAsync(visitorProfileKey, ct)
     -- single indexed UPDATE inside Customizer's outer NPoco scope:
     --   UPDATE analyzerSession
     --   SET anonymizedUtc = SYSUTCDATETIME(), deviceKey = ''
     --   WHERE visitorProfileKey = @visitorProfileKey
     --     AND anonymizedUtc IS NULL
     -- returns the sessionKey column values of the UPDATEd rows.

3. foreach var sessionKey in affectedSessionKeys:
     cacheStore.InvalidateBySessionKey(sessionKey)
     -- Cache invalidation runs AFTER repository success. If the outer
     -- scope rolls back (a downstream cascade step throws), the cache
     -- entries remain — which is wrong-but-recoverable: a stale cache
     -- entry pointing at a "still-active-but-not-actually-anonymised"
     -- row falls through on next lookup because the entry's
     -- LastActivityUtc + inactivityTimeout will eventually expire OR
     -- the next pageview's resolver will re-read.

4. log.Information(
     "Analyzer session soft-anonymisation completed for VisitorProfileKey={…} Count={…}",
     visitorProfileKey, affectedSessionKeys.Count)
```

### Determinism / idempotence

- Idempotent re-runs: the `WHERE anonymizedUtc IS NULL` predicate ensures a second invocation on the same visitor key affects zero rows; `affectedSessionKeys` is empty; cache invalidation is a no-op; log is emitted with `Count=0` (acceptable).
- Zero-row no-op: a visitor with no `analyzerSession` rows produces `affectedSessionKeys = []`; no error, no warning.
- Concurrent re-runs (two orchestrator invocations on the same visitor in parallel) → both UPDATE the same rows; SQL Server serialises the row-level writes; the second UPDATE matches zero rows; both return successfully. Cache invalidation is idempotent.

### Atomic rollback

When the outer `AnonymizeVisitorProfileHandler` scope does NOT call `Complete()` (e.g., a downstream cascade step throws, or the orchestrator itself fails before commit), the session-UPDATE rolls back atomically:

- `analyzerSession` rows revert to `anonymizedUtc = null, deviceKey = <previous value>`.
- Slice-002 `analyzerEventReceipt` rows are also reverted (slice-002 cascade-step is hard-delete; rollback re-creates the rows).
- Customizer's `customizerVisitorProfile.IdentityRef` is also reverted.

The cache invalidation in step 3 above is the **only** observable side effect that does NOT rollback. The mitigation: stale cache entries are self-healing — the next `ResolveAsync` call falls back to a DB read which observes the un-anonymised state and re-populates the cache.

### Thread safety

- `ExecuteAsync` is called once per `AnonymizeVisitorProfileCommand` invocation; no concurrent calls within a single command. Customizer's orchestrator serialises cascade steps inside its scope.
- The repository's single UPDATE is atomic at the SQL Server level. The cache invalidation enumerates returned `sessionKey`s sequentially; each `InvalidateBySessionKey` call is thread-safe against concurrent `MemoryCache` operations.

### Error handling

| Error | Behaviour |
|---|---|
| `DbException` from the UPDATE | Propagates out of `ExecuteAsync`; Customizer's orchestrator catches and rolls back the outer scope. Cache is NOT invalidated. |
| `OperationCanceledException` | Propagates; orchestrator handles. |
| `ArgumentException` on null repository dependency at construction | Unreachable in DI-registered usage — the composer wires concrete impls. |

## Tests proving conformance

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `Unit/Features/Sessions/Application/Anonymization/AnalyzerSessionCascadeStepTests.SoftAnonymisesVisitorRowsOnly` | UPDATE called with `visitorProfileKey = A`; visitor B's rows untouched. | US2 AS1 |
| `…SetsAnonymizedUtcAndClearsDeviceKey` | Post-call, the affected row's `anonymizedUtc` is non-null and `deviceKey` is empty string; aggregates (`pageviewCount`, `startUtc`, `endUtc`) preserved. | US2 AS1 |
| `…IdempotentOnAlreadyAnonymisedRow` | Second invocation against the same visitor → repository returns empty `affectedSessionKeys`; no error, no warning. | US2 AS4 |
| `…ZeroRowNoOp` | Visitor with no session rows → repository returns empty; no error, no warning. | US2 AS3 |
| `…CacheInvalidationRunsAfterRepositorySuccess` | Cache invalidation count == affected-session count. | research §3 |
| `Integration/Sessions/CascadeSoftAnonymiseTests.SoftAnonymiseInsideOuterScope` | End-to-end through `AnonymizeVisitorProfileCommand`; cascade step soft-anonymises sessions for A; receipts for A also deleted (slice-002 cascade); B's sessions + receipts untouched; A's anonymised sessions still queryable by aggregate. | US2 AS1 |
| `Integration/Sessions/CascadeRollbackTests.CascadeThrowRollsBackAllSteps` | Inject a third cascade step that throws after Analyzer's session step runs; assert visitor's sessions + receipts revert; customizerVisitorProfile.IdentityRef also reverted. | US2 AS2 |

## Versioning

`AnalyzerSessionCascadeStep` is **internal** at slice 003 — not part of the pinned public surface. The behaviour it provides is observable through Customizer's `AnonymizeVisitorProfileCommand`; any third-party that needs to replace it would register their own `IAnonymizationCascadeStep` (and `services.Replace(...)` to swap Analyzer's). The contract obligation is on the Customizer-published `IAnonymizationCascadeStep` interface, which is itself pinned.
