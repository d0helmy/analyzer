# Contract — `IAnalyzerSessionResolver`

**Feature**: `003-session-tracking`
**Date**: 2026-05-18
**Stability**: **internal**. Not part of Analyzer's pinned public surface (per slice-002 Clarifications Q3 — pinned namespace list excludes `Analyzer.Features.*`). Documented here as a contract because it's the load-bearing extension point inside the slice; a third-party can still swap the implementation by registering an alternative against the interface.

## Namespace

```
Analyzer.Features.Sessions.Application.IAnalyzerSessionResolver
```

## Shape

```csharp
namespace Analyzer.Features.Sessions.Application;

internal interface IAnalyzerSessionResolver
{
    /// <summary>
    /// Resolve the session this pageview belongs to. Either extends an
    /// in-progress active session for the visitor+device, closes a
    /// stale one and opens a new session, or opens a fresh session if
    /// none exists. Synchronous to the calling thread.
    /// </summary>
    /// <param name="visitorProfileKey">
    /// Customizer-resolved visitor key. MUST be non-empty (caller
    /// guarantees; the slice-002 handler short-circuits empty keys
    /// before reaching the resolver).
    /// </param>
    /// <param name="userAgent">
    /// Raw <c>User-Agent</c> as carried on the immutable
    /// <c>Pageview</c> record by the <c>PageviewCaptured</c>
    /// notification (cross-product prerequisite — Customizer
    /// captures synchronously on the request thread; see
    /// <c>customizer-prereq.md</c>). NOT read from
    /// <c>IHttpContextAccessor</c> — that path is unreliable under
    /// typical fire-and-forget handler timing (slice analysis C1).
    /// Null / whitespace tolerated — hashed to a deterministic
    /// sentinel device key.
    /// </param>
    /// <param name="receivedUtc">
    /// When the handler observed the notification. Set on the new /
    /// extended session row as <c>lastActivityUtc</c> (and as
    /// <c>startUtc</c> on fresh sessions).
    /// </param>
    /// <param name="ct">Cancellation token from the handler chain.</param>
    /// <returns>
    /// The session's <c>sessionKey</c> (publicly-exposed stable handle)
    /// AND the consumer-facing <see cref="AnalyticsSession"/> projection
    /// the handler writes to the request-scoped state store.
    /// </returns>
    ValueTask<SessionResolutionResult> ResolveAsync(
        Guid visitorProfileKey,
        string? userAgent,
        DateTimeOffset receivedUtc,
        CancellationToken ct);
}

internal readonly record struct SessionResolutionResult(
    Guid SessionKey,
    AnalyticsSession Projection);
```

The `SessionResolutionResult` is a `readonly record struct` for zero-allocation hot-path return — the resolver is called on every pageview notification.

## DI registration

| Aspect | Value |
|---|---|
| **Lifetime** | **Scoped** — same lifetime as the receipt repository the slice-002 handler depends on, for symmetry. The resolver's transitive deps (`IAnalyzerSessionRepository` scoped, `AnalyzerSessionCacheStore` singleton, `IOptionsMonitor<AnalyzerSessionOptions>` singleton, `TimeProvider` singleton) all compose cleanly under scoped resolution. |
| **Implementation** | `Analyzer.Features.Sessions.Application.AnalyzerSessionResolver` (internal sealed) |
| **Composition site** | `AnalyzerComposer.Compose` — `services.AddScoped<IAnalyzerSessionResolver, AnalyzerSessionResolver>();` |

## Behavior

### Inputs

| Field | Type | Constraint |
|---|---|---|
| `visitorProfileKey` | `Guid` | non-empty (caller's responsibility) |
| `userAgent` | `string?` | any value tolerated; null/whitespace hashes to sentinel |
| `receivedUtc` | `DateTimeOffset` | non-default-MinValue (UTC clock) |
| `ct` | `CancellationToken` | passed through to repository calls |

### Outputs

`SessionResolutionResult { SessionKey, Projection }` — never null. The projection's `IsActive` is always `true` for the result returned (the resolver returns the just-extended-or-just-opened session, both of which are active).

### Resolution flow (normative)

```
1. deviceKey = DeviceKeyHasher.Compute(userAgent)
2. inactivityTimeout = TimeSpan.FromMinutes(options.CurrentValue.InactivityTimeoutMinutes)

3. cache.TryGet(visitorProfileKey, deviceKey, out entry):
3a.   if hit AND entry.LastActivityUtc + inactivityTimeout >= receivedUtc:
3a-i.     await repository.ExtendAsync(entry.SessionKey, receivedUtc, ct)
3a-ii.    cache.UpdateActivity(visitorProfileKey, deviceKey, entry.SessionKey, receivedUtc)
3a-iii.   re-read session row (single SELECT) to build the projection
3a-iv.    return SessionResolutionResult { entry.SessionKey, projection }
3b.   if hit AND stale:
3b-i.     await repository.CloseAsync(entry.SessionKey, entry.LastActivityUtc + inactivityTimeout, ct)
3b-ii.    cache.Invalidate(visitorProfileKey, deviceKey)
3b-iii.   fall through to step 4

4. row = await repository.GetLatestActiveAsync(visitorProfileKey, deviceKey, ct)
4a.   if row is not null AND row.LastActivityUtc + inactivityTimeout >= receivedUtc:
4a-i.     await repository.ExtendAsync(row.SessionKey, receivedUtc, ct)
4a-ii.    cache.UpdateActivity(…)
4a-iii.   return SessionResolutionResult { row.SessionKey, projection-with-incremented-count }
4b.   if row is not null AND stale:
4b-i.     await repository.CloseAsync(row.SessionKey, row.LastActivityUtc + inactivityTimeout, ct)
4b-ii.    fall through to step 5
4c.   if row is null: step 5

5. Open new:
5a.   newDto = AnalyzerSessionDto { Id = Guid.NewGuid(), SessionKey = Guid.NewGuid(),
                                    VisitorProfileKey = …, DeviceKey = …,
                                    StartUtc = receivedUtc, LastActivityUtc = receivedUtc,
                                    EndUtc = null, PageviewCount = 1, IsActive = true,
                                    AnonymizedUtc = null }
5b.   try:
5b-i.     await repository.InsertAsync(newDto, ct)
5b-ii.    cache.UpdateActivity(visitorProfileKey, deviceKey, newDto.SessionKey, receivedUtc)
5b-iii.   return SessionResolutionResult { newDto.SessionKey, projection }
5c.   catch DbException ex when IsUniqueConstraintViolation(ex):
5c-i.     winner = await repository.GetLatestActiveAsync(visitorProfileKey, deviceKey, ct)
5c-ii.    if winner is null: rethrow (shouldn't happen)
5c-iii.   await repository.ExtendAsync(winner.SessionKey, receivedUtc, ct)
5c-iv.    cache.UpdateActivity(…)
5c-v.     return SessionResolutionResult { winner.SessionKey, projection-with-incremented-count }
```

### Determinism / idempotence

- Two consecutive `ResolveAsync` calls for the same `(visitorProfileKey, userAgent)` with monotonic `receivedUtc` values within the inactivity window return the **same `SessionKey`** with an incremented `PageviewCount` on the second call.
- Two concurrent `ResolveAsync` calls for the same `(visitorProfileKey, userAgent)` are race-safe: exactly one new session row is created, both callers receive the same `SessionKey`, and `PageviewCount` reflects both extensions (atomically incremented via the repository's `ExtendAsync`).
- The lazy-close path is idempotent — `repository.CloseAsync` on an already-closed row is a no-op (the UPDATE matches zero rows; the cache invalidation is also a no-op on a missing key).

### Thread safety

- Multiple threads may call `ResolveAsync` concurrently. The cache (`AnalyzerSessionCacheStore`) is concurrent-by-design (`MemoryCache`-backed). The DB layer's partial unique index serialises the rare collision case.
- A single `ResolveAsync` call performs ≤ 3 indexed SQL statements (≤ 1 SELECT + ≤ 2 UPDATE/INSERT). No locks held between statements.

### Error handling

| Error | Behaviour |
|---|---|
| `DbException` with unique-violation on insert | Caught + race-resolve via re-read (step 5c). |
| `DbException` from any other operation | Propagates out of `ResolveAsync`. The slice-002 handler's outer catch swallows-and-logs, so the receipt enqueue is skipped (this pageview produces no session AND no receipt — consistent with "session resolution failure means we don't enqueue the receipt either, because the FK wouldn't be valid"). |
| `OperationCanceledException` (token cancelled) | Propagates. Handler's outer catch handles. |
| `ArgumentException` on empty `visitorProfileKey` | Resolver doesn't validate (caller does); behaviour is "open a session with empty `visitorProfileKey`", which the FK would reject on SQL Server (transferring the bug to the DB layer). Caller's responsibility to gate. |

## Behaviour-compatible custom implementations

A third-party may register an alternative `IAnalyzerSessionResolver` by re-registering the interface in their own composer (`services.Replace(...)`). Compatibility requires:

1. **Same return semantics** — never returns `null` `SessionKey`; always returns an `IsActive = true` projection.
2. **Same race-safety guarantee** — concurrent calls for the same `(visitorProfileKey, userAgent)` must produce exactly one new session row.
3. **Same session-key durability** — the returned `SessionKey` must point at a row that is durable in the DB BEFORE `ResolveAsync` returns (the slice-002 handler's receipt enqueue depends on this).
4. **Same lazy-close obligation** — if attaching to a stale session, the custom impl must close the stale one in the same call (the spec's edge cases depend on this).

Custom impls MAY:

- Use a different cache substrate (Redis, distributed cache) — provided the durability + race-safety guarantees hold.
- Replace the `deviceKey` derivation — provided the result is a stable bounded-cardinality string per request (an empty or unbounded `deviceKey` violates the partial unique index's purpose).

## Tests proving conformance

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `Unit/Features/Sessions/Application/AnalyzerSessionResolverTests.CacheHitExtendsExistingSession` | Cache hit + fresh entry → repository.ExtendAsync called; no insert; cache updated. | US1 AS2 |
| `…CacheMissDbHitExtendsExistingSession` | Cache miss + repository.GetLatestActiveAsync returns fresh row → extend; cache populated. | US1 AS2 |
| `…StaleSessionClosedThenNewOpened` | Cache hit + stale → close + new insert with monotonic `startUtc`. | US1 AS3 |
| `…ConcurrentResolutionProducesOneRow` | Two concurrent `ResolveAsync` for same `(visitorProfileKey, userAgent)` → exactly one new row; both return same `SessionKey`. | US1 AS4 |
| `…OptionsReloadAdjustsTimeout` | `IOptionsMonitor` reload changes `InactivityTimeoutMinutes`; next resolve uses the new value. | FR-008 |
| `…NullUserAgentResolvesToSentinelDeviceKey` | `userAgent = null` → resolution proceeds; deviceKey is the deterministic empty-string sentinel. | data-model §14 |
| `Integration/Sessions/ResolveAndAttachTests.EndToEndOpensAndExtends` | Through `PageviewCapturedHandler` → resolver → DB; receipt persists with correct `sessionKey` FK. | US1 AS1 + AS6 |

## Versioning

`IAnalyzerSessionResolver` is **internal** at slice 003. Future slices may promote it to public (e.g., if a customer needs to swap the implementation for a Redis-backed cache). Promotion would land as a new pinned-namespace entry under `Analyzer.Features.Sessions.Application.Contracts` or similar, following the slice-001 `IVisitorIdentifier` precedent. Slice 003 deliberately does not promote — the interface is still being shaped, and a public commitment now would lock in details that may need iteration in slice 004+ (custom events ride the same resolver call).
