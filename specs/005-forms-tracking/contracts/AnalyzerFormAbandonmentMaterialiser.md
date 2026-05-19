# Contract — `AnalyzerFormAbandonmentMaterialiser`

**Feature**: `005-forms-tracking`
**Date**: 2026-05-19
**Stability**: internal; plugs into slice-003's `AnalyzerSessionSweeperService` via a Scoped service registration.

Materialises `Abandon` rows in `analyzerFormEvent` for `(visitorKey, formKey, sessionKey)` tuples whose session has just been logically closed but whose form lifecycle has a `Start` row without a corresponding `Success` row.

## Signature

```csharp
internal interface IAnalyzerFormAbandonmentMaterialiser
{
    /// <summary>
    /// Invoked by AnalyzerSessionSweeperService after a batch of sessions is closed.
    /// Inserts one Abandon row per (visitorKey, formKey, sessionKey) tuple with a
    /// Start row but no Success row. Runs inside the same outer NPoco scope as the
    /// session-close UPDATEs (atomic rollback safety).
    /// </summary>
    Task MaterialiseAsync(IReadOnlyCollection<Guid> closedSessionKeys, DateTimeOffset logicalCloseUtc, CancellationToken ct);
}

internal sealed class AnalyzerFormAbandonmentMaterialiser : IAnalyzerFormAbandonmentMaterialiser
{
    public Task MaterialiseAsync(IReadOnlyCollection<Guid> closedSessionKeys, DateTimeOffset logicalCloseUtc, CancellationToken ct);
}
```

## Behavioural contract

1. **Batch query**: one SELECT against `analyzerFormEvent` using a `NOT EXISTS` subquery to find `(sessionKey, formKey)` tuples with `Start` but no `Success`. Filtered by `sessionKey IN @closedSessionKeys` to bound the result set.
   ```sql
   SELECT s.sessionKey, s.formKey, s.visitorProfileKey
   FROM analyzerFormEvent s
   WHERE s.sessionKey IN @closedSessionKeys
     AND s.eventType = 1  -- Start
     AND NOT EXISTS (
       SELECT 1 FROM analyzerFormEvent x
       WHERE x.sessionKey = s.sessionKey
         AND x.formKey = s.formKey
         AND x.eventType = 2  -- Success
     )
     AND NOT EXISTS (
       SELECT 1 FROM analyzerFormEvent x
       WHERE x.sessionKey = s.sessionKey
         AND x.formKey = s.formKey
         AND x.eventType = 3  -- Abandon (re-run safety)
     )
   ```
2. **Bulk insert**: for each result row, INSERT one `Abandon` row with `receivedUtc = logicalCloseUtc`, `elapsedMsFromStart = (logicalCloseUtc − start.receivedUtc).TotalMilliseconds`. Inserts run inside the same `IScopeProvider` scope as the sweeper's close-UPDATEs.
3. **Idempotency**: re-running for the same closed-session batch is safe — the second `NOT EXISTS` clause (Abandon-exclusion) prevents double-materialisation (SC-002).
4. **No-op when zero candidates**: empty result set ⇒ zero INSERTs ⇒ no log noise.
5. **Visitor anonymisation safety**: the visitor's identity may have been anonymised between `Start` and session-close. Edge Case in spec: the materialiser MUST NOT create rows referencing an anonymised visitor. Implementation: the SELECT joins to `customizerVisitorProfile` and filters out rows where `isAnonymized = 1`. Anonymisation-during-open-session is then a silent "no Abandon emitted" (the cascade step already deleted the `Start` row, so the JOIN returns nothing).

## DI lifetime

**Scoped** — resolved per sweeper pass. Sweeper uses `IServiceScopeFactory` to open a scope per batch (slice-003 precedent).

## Integration with `AnalyzerSessionSweeperService`

Slice-003's sweeper currently does:

```text
1. Query slice-003 candidates (sessions to close)
2. UPDATE analyzerSession SET isActive = 0, endUtc = @logicalCloseUtc WHERE sessionKey IN @batch
3. scope.Complete()
```

Slice 005 extends it to:

```text
1. Query candidates
2. UPDATE analyzerSession ...
3. await materialiser.MaterialiseAsync(closedSessionKeys, logicalCloseUtc, ct)
4. scope.Complete()
```

The materialiser is invoked AFTER the session-close UPDATE (so its `endUtc` is visible if the materialiser ever queries it; today's implementation uses `logicalCloseUtc` passed in directly, so the ordering is not load-bearing). Both inside the same `scope`.

## Conformance tests

| Conformance | Test class |
|---|---|
| One Abandon per `(visitorKey, formKey, sessionKey)` with Start-no-Success | `Integration/Forms/AbandonmentMaterialisationTests.OneAbandonPerOpenLifecycle` |
| Zero Abandons when Success exists | `Integration/Forms/AbandonmentMaterialisationTests.NoAbandonWhenSuccessRecorded` |
| Idempotent across re-runs | `Integration/Forms/AbandonmentMaterialisationTests.IdempotentAcrossSweeps` |
| Skips anonymised visitors | `Integration/Forms/AbandonmentMaterialisationTests.SkipsAnonymisedVisitors` |
| `elapsedMsFromStart` populated correctly | `Integration/Forms/AbandonmentMaterialisationTests.ElapsedMsFromStartPopulated` |
| Sweeper invokes materialiser inside same scope as session-close UPDATE | `Integration/Forms/AbandonmentMaterialisationTests.SharedScopeWithSessionClose` |
