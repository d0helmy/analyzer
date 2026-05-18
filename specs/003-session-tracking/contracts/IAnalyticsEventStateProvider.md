# Contract — `IAnalyticsEventStateProvider` (revised)

**Feature**: `003-session-tracking`
**Date**: 2026-05-18
**Stability**: public; pinned. Slice 003 extends the slice-002 interface additively — adding a new `CurrentSession` member alongside the existing `CurrentRequestReceipt`. The slice-002 member is unchanged in name, return type, and semantics.

This contract supersedes [`specs/002-pageview-subscription/contracts/IAnalyticsEventStateProvider.md`](../../002-pageview-subscription/contracts/IAnalyticsEventStateProvider.md) for the `CurrentSession` addition; everything else from the slice-002 contract remains in force.

## Namespace

```
Analyzer.Analytics.IAnalyticsEventStateProvider
```

Unchanged from slice 002.

## Shape (after slice 003)

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Per-request read surface for Analyzer-side captured event state.
/// Deliberately distinct from Customizer's pinned
/// <see cref="Customizer.Analytics.IAnalyticsStateProvider"/>
/// (Constitution Principle III + inter-product contract D3).
/// </summary>
public interface IAnalyticsEventStateProvider
{
    /// <summary>
    /// Slice 002 — the current request's captured event-receipt, or
    /// <c>null</c> when Analyzer's subscriber has not yet completed for
    /// this request.
    /// </summary>
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }

    /// <summary>
    /// Slice 003 — the current request's resolved session, or
    /// <c>null</c> when the subscriber has not yet completed. Same
    /// scoping semantics as <see cref="CurrentRequestReceipt"/>:
    /// typically <c>null</c> on the pageview request itself (the
    /// <c>Task.Run</c> dispatch usually outlives the request scope);
    /// reliably populated on in-request consumer flows at later slices.
    /// </summary>
    AnalyticsSession? CurrentSession { get; }
}
```

The reachable-by-signature record types `AnalyticsEventReceipt` (slice 002; now with the additive `SessionKey` init-only property) and `AnalyticsSession` (slice 003; new) both live in `Analyzer.Analytics`, inside the pinned namespace list — pinning captures their shapes directly with no reliance on transitive-reference walking.

### Sync Impact (slice 003)

The slice-002 baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` is regenerated with three additive diffs:

1. New `TYPE Analyzer.Analytics.AnalyticsSession : sealed class` with seven properties (matching [`AnalyticsSession.md`](AnalyticsSession.md)).
2. New `PROP Analyzer.Analytics.AnalyticsSession CurrentSession { get; }` on `IAnalyticsEventStateProvider`.
3. New `PROP System.Nullable\`1[[System.Guid…]] SessionKey { get; init; }` on `AnalyticsEventReceipt`.

All three are MINOR-additive per Principle X — no member rename, no breaking signature change, no removed member. Slice 002 callers compiled against the prior shape continue to work; the slice-002 positional constructor of `AnalyticsEventReceipt` is preserved verbatim (the new `SessionKey` is init-only, not positional).

## DI registration

| Aspect | Value | Source |
|---|---|---|
| **Lifetime** | **Scoped** (per-request) | unchanged from slice 002 |
| **Implementation** | `Analyzer.Analytics.AnalyticsEventStateProvider` (internal sealed) | unchanged from slice 002; gains a one-line projection `_store.CurrentSession` for the new member |
| **Backing store** | `Analyzer.Features.Events.Application.AnalyticsEventStateStore` (scoped, internal) | unchanged from slice 002; gains parallel `_currentSession` field + `SetCurrentSession` method |
| **Composition site** | `AnalyzerComposer.Compose` | unchanged — slice 003 doesn't re-register the interface; the registration line stays the same since the impl extension is binary-compatible |

## Behavior

### Inputs

Unchanged: no caller arguments. The implementation depends on the slice-002 `AnalyticsEventStateStore`.

### Outputs

| Condition | `CurrentRequestReceipt` | `CurrentSession` | Side effects |
|---|---|---|---|
| Request just started; subscriber has not run | `null` | `null` | none |
| Subscriber completed for an in-request dispatch; receipt + session written to store | non-`null` immutable record | non-`null` immutable record | none |
| Subscriber completed for the current pageview on a post-request thread (typical fire-and-forget case) | `null` (different scope's store) | `null` (different scope's store) | none |
| Subscriber completed but with an empty `VisitorProfileKey` (configuration-error skip from slice 002) | `null` (handler short-circuited before enqueue) | `null` (resolver never ran) | none |
| Read concurrently from multiple in-request consumers | Same value as a prior read in the same scope | Same value as a prior read in the same scope | none |
| Read after request scope has been disposed | Object-disposed behaviour from the DI container | Object-disposed behaviour from the DI container | none Analyzer-owned |

### Determinism / idempotence

- Reads within the same scope return the same value (no caching layer; the store's fields are the source of truth).
- The store's values are `null` at scope start; a single set call by the handler may transition each once. Slice 003 has no path that resets either to `null` after it's been set.
- The two members can be set in any order (the slice-002 receipt setter and the slice-003 session setter are independent operations on the same store). In practice the handler sets both atomically in its `TryUpdateInFlightStateStore(receipt, session)` helper; observers see them transition together.

### Thread safety

- The interface is read-only; concurrent readers within a single scope are safe by .NET's normal field-read semantics on `object?`.
- The store's single writer (the handler's opportunistic `SetCurrentReceipt` + `SetCurrentSession`) runs on the dispatched task thread; readers run on the request thread. Visibility is enforced by the DI container's scope construction barrier.

## Behaviour-compatible custom implementations (extended for slice 003)

A third-party developer may register an alternative `IAnalyticsEventStateProvider`. Behaviour compatibility requires:

1. **Same DI lifetime** — scoped per-request. Unchanged from slice 002.
2. **Same null semantics for BOTH members** — `null` MUST be returnable when no receipt / no session has been captured. Synthesising a non-`null` placeholder is a contract violation; in-process consumers' downstream logic depends on the null sentinel.
3. **Same threading shape** — concurrent reads within the same scope MUST be safe.
4. **Independence of members** — `CurrentRequestReceipt` and `CurrentSession` MUST NOT be mutually exclusive (both can be non-`null` at the same time once the handler completes). Custom impls that override one to derive the other (e.g., by joining receipt → session under the covers) MUST NOT cache the derivation in a way that violates the per-request scope.

Custom implementations MAY add behaviour-compatible side effects (e.g., metrics on every read) provided they do not change the return value semantics.

## Tests proving conformance (extended for slice 003)

Slice 003's contract test corpus, in `src/Analyzer.Tests/Integration/StateProvider/` (extends slice-002's corpus):

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `ScopedLifetimeTests.CurrentSessionIsNullBeforeResolverWrites` | A freshly-created scope returns `null` from `CurrentSession`. | US1 AS5 (slice 003) |
| `ScopedLifetimeTests.CurrentSessionResolutionReturnsSameInstanceWithinScope` | Two reads of `CurrentSession` within one scope return the same `AnalyticsSession` reference (or both `null`). | US3 AS1 (slice 002 — extended) |
| `CrossRequestIsolationTests.SessionsDoNotLeakAcrossRequests` | Two concurrent scopes, only one of which has had its store mutated, return independent `CurrentSession` values. | US3 AS2 (slice 002 — extended) |
| `PublicSurfacePinningTests.SnapshotMatchesBaseline` | The regenerated baseline includes the new `CurrentSession` line on the interface and the new `AnalyticsSession` type block. Tampering with either fails the test. | SC-007 (slice 003) |

All slice-002 conformance tests for `CurrentRequestReceipt` continue to pass — the interface extension is additive, so the slice-002 corpus runs unchanged.

## Versioning

This contract is **public, pinned, and stable** at slice 003 with the additive `CurrentSession` member. The pinning baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` is regenerated as part of this slice; the diff is reviewable in the slice-003 PR. Future slices append members per Principle X with the same regen + spec-acknowledgement pattern.
