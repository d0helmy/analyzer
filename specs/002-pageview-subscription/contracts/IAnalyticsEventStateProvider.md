# Contract — `IAnalyticsEventStateProvider`

**Feature**: `002-pageview-subscription`
**Date**: 2026-05-18
**Stability**: public; first member of an Analyzer-pinned surface to ship with a formal `PublicSurfacePinningTests` baseline. Pinning scope per slice 002 Clarifications Q3.

Analyzer's request-scoped read surface for in-process consumers that need to know what analytics-event state has been captured for the current request. **Deliberately distinct** from Customizer's pinned `Customizer.Analytics.IAnalyticsStateProvider` (Constitution Principle III + inter-product contract D3); both interfaces can be injected into the same consumer without ambiguity.

## Namespace

```
Analyzer.Analytics.IAnalyticsEventStateProvider
```

The short root namespace `Analyzer.Analytics` parallels Customizer's `Customizer.Analytics` so consumer-facing imports stay symmetric across the two co-deployed products.

## Shape

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Per-request read surface for Analyzer-side captured event state.
/// Injectable inside any Analyzer or Customizer request scope without
/// name-collision against Customizer's pinned
/// <see cref="Customizer.Analytics.IAnalyticsStateProvider"/>
/// (inter-product contract D3).
/// </summary>
/// <remarks>
/// Members on this interface grow additively per slice:
/// <list type="bullet">
///   <item>Slice 002 — <see cref="CurrentRequestReceipt"/>.</item>
///   <item>Slice 003 — adds session-state members.</item>
///   <item>Slice 004 — adds <c>CurrentRequestCustomEvents</c>.</item>
///   <item>Slice 007 — adds <c>CurrentVideoState</c>.</item>
/// </list>
/// Breaking changes to existing members are PROHIBITED outside MAJOR
/// releases (Constitution Principle X).
/// </remarks>
public interface IAnalyticsEventStateProvider
{
    /// <summary>
    /// The current request's captured event-receipt, or <c>null</c>
    /// when Analyzer's subscriber has not yet completed for this
    /// request.
    /// </summary>
    /// <remarks>
    /// On pageview requests, this property is typically <c>null</c>:
    /// Customizer publishes <see cref="Customizer.Features.Visitors.Application.Contracts.PageviewCaptured"/>
    /// via a <c>Task.Run</c> fire-and-forget dispatch, so the handler
    /// may complete after the request thread has already produced
    /// the response. On in-request dispatches at later slices (e.g.
    /// custom events fired in-page via slice 004's
    /// <c>analyzer.send(...)</c>), it populates reliably because the
    /// dispatch runs synchronously inside the request scope.
    /// </remarks>
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }
}
```

The reachable-by-signature record type `AnalyticsEventReceipt` lives in `Analyzer.Analytics` (alongside this interface), inside the pinned namespace list — pinning captures its shape directly, with no reliance on transitive-reference walking. (Pre-decided by slice-002 `/speckit-analyze` finding U2 to eliminate the empirical-branch task.)

## DI registration

| Aspect | Value |
|---|---|
| **Lifetime** | **Scoped** (per-request). Locked in by FR-007; matches Customizer's `IAnalyticsStateProvider` for symmetry. |
| **Implementation** | `Analyzer.Features.Events.Application.AnalyticsEventStateProvider` (internal sealed). |
| **Composition site** | `AnalyzerComposer.Compose` — `builder.Services.AddScoped<IAnalyticsEventStateProvider, AnalyticsEventStateProvider>();` |
| **Backing store** | `AnalyticsEventStateStore` (also scoped, internal). The provider injects the store and returns `store.CurrentRequestReceipt`. The handler writes to the store opportunistically (see `PageviewCapturedHandler.md`). |
| **Composer ordering** | Slice 001's `AnalyzerComposer` is registered with `[ComposeAfter(typeof(VisitorAnalyticsComposer))]`; slice 002 extends the same composer without changing ordering. |

## Behavior

### Inputs

`IAnalyticsEventStateProvider` takes no caller arguments — it is a pure read surface. The implementation depends on:

1. `AnalyticsEventStateStore` (Analyzer; per-request DI surface, internal).

### Outputs

| Condition | `CurrentRequestReceipt` | Side effects |
|---|---|---|
| Request just started; subscriber has not run | `null` | none |
| Subscriber completed; in-request dispatch (later slices); receipt written to store | non-`null`, immutable record | none |
| Subscriber completed for the current pageview but on a post-request thread (typical fire-and-forget case) | `null` (the store on the dispatched task's scope is a different instance than the request scope's store) | none |
| Read concurrently from multiple in-request consumers | Same value as a prior read in the same scope (single-writer-then-reads ordering) | none |
| Read after request scope has been disposed | Object-disposed behaviour from the DI container; consumers MUST resolve through their own scope, not a captured one | none Analyzer-owned |

### Determinism / idempotence

- Reads within the same scope return the same value (no caching layer; the store's field is the source of truth).
- The store's value is `null` at scope start; a single set call by the handler may transition it once. Slice 002 has no path that resets the value to `null` after it's been set.

### Thread safety

- The interface is read-only; concurrent readers within a single scope are safe by .NET's normal field-read semantics on `object?`.
- The store's single writer (the handler's opportunistic `SetCurrentReceipt`) runs on the dispatched task thread; readers run on the request thread. Visibility is enforced by the DI container's scope construction barrier (each scope's services are fully constructed before any caller resolves them).

## Behaviour-compatible custom implementations

A third-party developer may register an alternative `IAnalyticsEventStateProvider` to swap in a custom backing store (e.g. one that aggregates receipts across multiple sub-requests for a hypothetical view-composition scenario). Behaviour compatibility requires:

1. **Same DI lifetime** — scoped per-request. A singleton or transient registration leaks state across requests / re-resolves on every read.
2. **Same null semantics** — `null` MUST be returnable when no receipt has been captured. Synthesising a non-`null` placeholder when no receipt exists is a contract violation (the in-process consumers' downstream logic depends on the null sentinel).
3. **Same threading shape** — concurrent reads within the same scope MUST be safe.

Custom implementations MAY add behaviour-compatible side effects (e.g. metrics on every read) provided they do not change the return value semantics.

## Tests proving conformance

Slice 002's contract test corpus, in `src/Analyzer.Tests/Integration/StateProvider/`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `ScopedLifetimeTests.ResolutionReturnsSameInstanceWithinScope` | Two `IServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>` calls within one scope return reference-equal instances. | US3 AS1 |
| `ScopedLifetimeTests.ResolutionReturnsDifferentInstanceAcrossScopes` | Resolving from two distinct scopes returns non-equal instances. | US3 AS1 |
| `ScopedLifetimeTests.CurrentReceiptIsNullBeforeHandlerWrites` | A freshly-created scope returns `null` from `CurrentRequestReceipt`. | US3 AS1 |
| `CrossRequestIsolationTests.ConcurrentRequestsDoNotShareState` | Two concurrent scopes, only one of which has had its store mutated, return independent values. | US3 AS2 |
| `PublicSurfacePinningTests.SnapshotMatchesBaseline` | The reflection-canonical-form serialisation of pinned namespaces matches the checked-in baseline; tampering with `IAnalyticsEventStateProvider`'s signature fails the test. | US3 AS3 / SC-005 |

## Versioning

This contract is **public, pinned, and stable** at slice 002. The pinning baseline at `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt` captures the current shape. Future slices append members per Principle X; the pinning test regenerates with a justification line in the slice's spec.
