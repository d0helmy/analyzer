# Contract — `PageviewCapturedHandler`

**Feature**: `002-pageview-subscription`
**Date**: 2026-05-18
**Stability**: internal (not part of the pinned surface). Behaviour is contract-bound by the spec's FR set; the type itself is not consumer-facing.

The single subscriber Analyzer registers against Customizer's `PageviewCaptured` notification. Bridges Customizer's fire-and-forget publish into Analyzer's bounded write queue + dispatcher pattern.

## Type

```
Analyzer.Features.Events.Application.PageviewCapturedHandler
  : INotificationAsyncHandler<Customizer.Features.Visitors.Application.Contracts.PageviewCaptured>
```

| Aspect | Value |
|---|---|
| **Visibility** | `internal sealed` |
| **DI lifetime** | **Transient** (Umbraco's standard `INotificationAsyncHandler<T>` resolution shape; one instance per dispatch) |
| **Composition site** | `AnalyzerComposer.Compose` — `builder.Services.AddTransient<INotificationAsyncHandler<PageviewCaptured>, PageviewCapturedHandler>();` |
| **Dispatcher** | Customizer's `IEventAggregator.PublishAsync` invoked from `Task.Run` inside `PageviewCapturedNotifier.Notify(...)` |

## Dependencies

```csharp
public PageviewCapturedHandler(
    AnalyzerEventReceiptWriteQueue queue,
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<PageviewCapturedHandler> logger);
```

| Dep | Role |
|---|---|
| `AnalyzerEventReceiptWriteQueue` | Singleton bounded channel — `TryEnqueue` is the only DB-affecting work performed on the dispatch thread. |
| `IServiceScopeFactory` | Used to create a fresh service scope when the request scope is unavailable (typical fire-and-forget case). The handler does NOT depend on the request scope being alive. |
| `IHttpContextAccessor` | Opportunistic — used to reach the request-scoped `AnalyticsEventStateStore` IF the request hasn't yet ended, so in-request consumers can observe the receipt via `IAnalyticsEventStateProvider`. Best-effort; null-tolerant. |
| `TimeProvider` | Sources `ReceivedUtc`. Singleton `TimeProvider.System` in prod; replaced with `FakeTimeProvider` in tests. |
| `ILogger<PageviewCapturedHandler>` | Structured logging at `LogWarning` (drops, swallows), `LogDebug` (back-pressure absent-pageview, duplicate dispatch tolerated). |

## Inputs

The notification's `Pageview` record carries:

| Field (from `Pageview`) | Used? | How |
|---|---|---|
| `Key: Guid` | **Yes** | Becomes `analyzerEventReceipt.pageviewKey`. Validated non-empty. Drives idempotency. |
| `VisitorProfileKey: Guid` | **Yes** | Becomes `analyzerEventReceipt.visitorProfileKey`. Validated non-empty (`Guid.Empty` triggers warning-log skip per `FR-ID-05`). |
| `ContentKey: Guid` | No | Slice 002 does not surface content dimensions. Reachable later via join to `customizerPageview`. |
| `Segments: PageviewSegmentSet` | No | Not stored in slice 002. |
| `WasContentTombstoned: bool` | No | Not stored in slice 002. |
| `RequestUtc: DateTimeOffset` | No | Analyzer records its own `ReceivedUtc` (`TimeProvider.GetUtcNow()`) — distinct from Customizer's capture time. |
| `UtmSource / UtmMedium / UtmCampaign` | No | UTM is intentionally out of scope (`FR-DIM-04` drop, per CLAUDE.md product framing). |

## Method shape

```csharp
public async Task HandleAsync(PageviewCaptured notification, CancellationToken cancellationToken)
{
    try
    {
        var pv = notification.Pageview;

        if (pv.Key == Guid.Empty)
        {
            _logger.LogDebug("Skipping PageviewCaptured with empty Pageview.Key");
            return;
        }

        if (pv.VisitorProfileKey == Guid.Empty)
        {
            _logger.LogWarning(
                "Configuration error: PageviewCaptured fired with VisitorProfileKey=Empty for PageviewKey={PageviewKey}; skipping receipt.",
                pv.Key);
            return;
        }

        var receipt = new AnalyticsEventReceipt(
            Id: Guid.NewGuid(),
            PageviewKey: pv.Key,
            VisitorProfileKey: pv.VisitorProfileKey,
            ReceivedUtc: _timeProvider.GetUtcNow());

        var op = new AnalyzerEventReceiptWriteOp(receipt);
        if (!_queue.TryEnqueue(op))
        {
            _logger.LogWarning(
                "Analyzer write queue at capacity ({Capacity}); dropping receipt for PageviewKey={PageviewKey} VisitorProfileKey={VisitorProfileKey}",
                _queue.Capacity, pv.Key, pv.VisitorProfileKey);
            return;
        }

        // Opportunistic state-store update — best-effort, swallows if
        // request scope already disposed.
        TryUpdateInFlightStateStore(receipt);
    }
    catch (Exception ex)
    {
        // Defence in depth — Customizer's PageviewCapturedNotifier
        // already swallows at warning level, but we double-up so a
        // bug inside this handler (not inside the publish chain) is
        // also contained.
        _logger.LogWarning(ex,
            "PageviewCapturedHandler failed for PageviewKey={PageviewKey}",
            notification.Pageview.Key);
    }
}
```

### `TryUpdateInFlightStateStore`

```csharp
private void TryUpdateInFlightStateStore(AnalyticsEventReceipt receipt)
{
    try
    {
        var requestServices = _httpContextAccessor.HttpContext?.RequestServices;
        var store = requestServices?.GetService<AnalyticsEventStateStore>();
        store?.SetCurrentReceipt(receipt);
    }
    catch (ObjectDisposedException)
    {
        // Request scope already disposed — the common case for fire-
        // and-forget pageview dispatches. Not an error.
    }
    catch (InvalidOperationException)
    {
        // Service provider disposed; same case.
    }
}
```

## Behaviour matrix

| Scenario | Pre-condition | Effect | Log |
|---|---|---|---|
| Happy path | Authenticated request; non-empty `Key`+`VisitorProfileKey`; queue has room | Receipt enqueued; opportunistic store update | none (steady-state) |
| Empty `Pageview.Key` | Customizer regression (should never happen) | Skip; return | `LogDebug` |
| Empty `VisitorProfileKey` | Misconfigured external-login provider | Skip; return | `LogWarning` (configuration error) |
| Queue full | Throughput exceeds dispatcher flush rate | Drop; return | `LogWarning` (drop with `PageviewKey` + `VisitorProfileKey`) |
| Duplicate dispatch (same `Pageview.Key`) | Customizer's fire-and-forget caused a re-fire | Enqueue both; dispatcher's `InsertAsync` catches the unique-index violation, treats as no-op | `LogDebug` (from repository, not handler) |
| Handler exception (any cause) | Bug, transient DI resolution failure, etc. | Swallow; return | `LogWarning` with exception |
| Request scope already disposed when state-store update attempted | Typical for pageview dispatches | Swallow `ObjectDisposedException` / `InvalidOperationException` | none |

## Threading

Runs on a `Task.Run`-spawned thread per Customizer's `PageviewCapturedNotifier.Notify` shape. Never on the request thread; therefore safe to perform synchronous-cheap work (queue enqueue, log emit) without affecting the request's latency. Any I/O is forbidden on this thread by design — the queue's `TryWrite` is non-blocking; the dispatcher (a hosted service) handles all actual DB I/O.

## Tests proving conformance

In `src/Analyzer.Tests/Unit/Features/Events/Application/PageviewCapturedHandlerTests.cs`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `EnqueuesReceiptForValidNotification` | A receipt op is enqueued with the correct field projections. | US1 AS1 (unit) |
| `SkipsEmptyVisitorProfileKey` | No enqueue; warning log emitted. | Spec Edge Case "Notification fires but `VisitorProfileKey` is `Guid.Empty`" |
| `SwallowsHandlerExceptionAndLogs` | A thrown exception inside the body never propagates; warning log emitted. | US1 AS4 |
| `LogsDropWhenQueueFull` | When `TryEnqueue` returns `false`, the handler logs warning + returns. | Spec Edge Case "Bounded write-queue is full" |

In `src/Analyzer.Tests/Integration/PageviewSubscription/EndToEndCaptureTests.cs`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `PublishCausesReceiptRowInDb` | Publishing a `PageviewCaptured` via the real `IEventAggregator` results in exactly one `analyzerEventReceipt` row within 1 s. | US1 AS1 + SC-001 |
| `DuplicatePublishesProduceSingleRow` | Publishing the same `Pageview.Key` twice produces exactly one row; no exception. | US1 AS3 + SC-004 |

In `src/Analyzer.Tests/Integration/PageviewSubscription/BackPressureDropTests.cs`:

| Test | Asserts | Per spec acceptance scenario |
|---|---|---|
| `NotificationWithDroppedParentPageviewWritesReceipt` | When `customizerPageview` is absent (simulated by direct notification publish bypassing the write queue), the receipt still writes (soft FK on PageviewKey). | US1 AS2 |
