# Contract — `IAnalyticsEventStateProvider` (revised for slice 004)

**Feature**: `004-custom-events`
**Date**: 2026-05-18
**Stability**: public; pinned. Slice 004 extends the slice-002/003 interface additively with `CurrentRequestCustomEvents`. Slice-002 + slice-003 members unchanged.

Supersedes [`specs/003-session-tracking/contracts/IAnalyticsEventStateProvider.md`](../../003-session-tracking/contracts/IAnalyticsEventStateProvider.md) for the new member; everything else from the slice-003 contract remains in force.

## Shape (after slice 004)

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    /// <summary>
    /// Slice 002 — the current request's captured event-receipt, or
    /// <c>null</c> when the slice-002 subscriber has not yet completed.
    /// </summary>
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }

    /// <summary>
    /// Slice 003 — the current request's resolved session, or
    /// <c>null</c> when not yet resolved.
    /// </summary>
    AnalyticsSession? CurrentSession { get; }

    /// <summary>
    /// Slice 004 — the custom events captured in the current request
    /// scope. Empty list when none captured (never null). The list
    /// grows as the page script makes multiple `analyzer.send(...)`
    /// calls during the same request lifecycle.
    /// </summary>
    /// <remarks>
    /// Slice 004 is the first slice where this state-provider is
    /// reliably populated for in-request consumers — the management
    /// endpoint that captures custom events runs synchronously on the
    /// request thread (vs slice-002's fire-and-forget pageview
    /// dispatch where the state-provider is typically null per the
    /// slice-002/003 caveats).
    /// </remarks>
    IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }
}
```

### Sync Impact (slice 004)

Pinning baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` regenerated with 2 additive diffs:

1. New `TYPE Analyzer.Analytics.AnalyticsCustomEvent : sealed class` with 9 properties (matching [`AnalyticsCustomEvent.md`](AnalyticsCustomEvent.md)).
2. New `PROP System.Collections.Generic.IReadOnlyList<Analyzer.Analytics.AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }` on `IAnalyticsEventStateProvider`.

Both MINOR-additive per Principle X. Slice-002 + slice-003 callers compiled against the prior shape continue to work; positional ctors of `AnalyticsEventReceipt` and `AnalyticsSession` unchanged.

## DI registration

| Aspect | Value | Source |
|---|---|---|
| **Lifetime** | **Scoped** (per-request) | unchanged from slice 002 |
| **Implementation** | `Analyzer.Analytics.AnalyticsEventStateProvider` (internal sealed) | unchanged from slice 002; gains a one-line projection `_store.CurrentRequestCustomEvents` |
| **Backing store** | `Analyzer.Features.Events.Application.AnalyticsEventStateStore` (scoped, internal) | unchanged shape; gains `_currentCustomEvents` list field + `AppendCustomEvent(...)` method |
| **Composition site** | `AnalyzerComposer.Compose` | unchanged registration line — additive method on store is binary-compatible |

## Behavior

### Outputs

| Condition | `CurrentRequestReceipt` | `CurrentSession` | `CurrentRequestCustomEvents` |
|---|---|---|---|
| Request just started | `null` | `null` | `[]` (empty list, never null) |
| Custom event captured this request (slice 004 management endpoint runs on this thread) | `null` (separate from pageview path) | non-`null` (resolver ran) | list of length ≥ 1 |
| Pageview request, handler ran post-response | `null` (typical) | `null` (typical) | `[]` |
| Multiple `analyzer.send(...)` calls in the same request | `null` | non-`null` | list of length = call count |

### Determinism / idempotence

- Within a scope, `CurrentRequestCustomEvents` reads return references to the same underlying list (read-only view).
- Each `AppendCustomEvent(...)` mutation adds exactly one entry; ordering matches call order.

### Thread safety

- Concurrent reads of the read-only list view within a single scope are safe by `List<T>`'s read semantics under the single-writer-single-reader model. The custom-event endpoint runs on a single request thread; multi-thread append within one scope isn't a real scenario for slice 004.

## Behaviour-compatible custom implementations

A third-party may register an alternative `IAnalyticsEventStateProvider`. Compatibility requires (extending slice-003's contract):

1. Same DI lifetime — scoped per-request.
2. Same null/empty semantics for ALL three members.
3. Same threading shape.
4. `CurrentRequestCustomEvents` MUST NOT return `null` — empty list is the no-events sentinel.
5. Members are independent — all three can be populated simultaneously; populated state of one does not imply populated state of others.

## Tests proving conformance (extended for slice 004)

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `ScopedLifetimeTests.CurrentRequestCustomEventsIsEmptyBeforeAppend` | Freshly-created scope returns empty list (never null) from `CurrentRequestCustomEvents`. | US1 AS4 |
| `ScopedLifetimeTests.CurrentRequestCustomEventsGrowsOnAppend` | After two `AppendCustomEvent(...)` calls in the same scope, list has length 2 in append order. | US1 AS5 |
| `CrossRequestIsolationTests.CustomEventsDoNotLeakAcrossRequests` | Two concurrent scopes, only one of which has had its store mutated, return independent `CurrentRequestCustomEvents` values. | (cross-cutting; slice-002 precedent extended) |
| `PublicSurfacePinningTests.SnapshotMatchesBaseline` | The regenerated baseline includes the new `CurrentRequestCustomEvents` line + the new `AnalyticsCustomEvent` type block. | SC-008 |

## Versioning

Public, pinned, stable at slice 004. Future slices may append additional members per Principle X (slice 006 may add `CurrentVideoState`, etc.) using the same additive + regen pattern. Breaking changes outside MAJOR releases PROHIBITED.
