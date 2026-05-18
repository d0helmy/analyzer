# Phase 0 Research: Pageview Subscription + Analytics-Event State Provider

**Slice**: 002 — pageview subscription
**Date**: 2026-05-18
**Constitution**: v1.1.0
**Input**: [`spec.md`](spec.md) + [`plan.md`](plan.md) Technical Context

The plan's Technical Context contains no `NEEDS CLARIFICATION` markers — all spec-level uncertainty resolved via `/speckit-clarify` (Q1 retention, Q2 durability, Q3 pinning scope). This document captures the design decisions grounded against the existing Customizer codebase reference, the alternatives considered, and the rationale, so `/speckit-tasks` can drive directly into implementation.

---

## §1 — Pageview subscription mechanism

**Decision**: Implement `Analyzer.Features.Events.Application.PageviewCapturedHandler : INotificationAsyncHandler<Customizer.Features.Visitors.Application.Contracts.PageviewCaptured>`. Register via `builder.Services.AddTransient<INotificationAsyncHandler<PageviewCaptured>, PageviewCapturedHandler>()` in `AnalyzerComposer` — Umbraco's standard subscriber registration shape; `IEventAggregator` discovers it automatically.

**Rationale**: This is the Umbraco-idiomatic way and the path Customizer's `PageviewCaptured` notification documents in its remarks block ("Subscriber contract: handlers SHOULD treat the carried `Pageview` as a read-only snapshot of capture-time state"). Customizer's `PageviewCapturedNotifier` (`src/Customizer/Features/Visitors/Application/PageviewCapturedNotifier.cs`) already wraps `IEventAggregator.PublishAsync` in `Task.Run` for fire-and-forget dispatch and swallows any subscriber throw at warning level — so the FR-005 "MUST NOT propagate" requirement is satisfied at the Customizer layer; Analyzer's handler MUST also catch its own exceptions defensively because the wrapper-swallow only catches what the publish chain throws, not what an individual handler swallows internally.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Synchronous `INotificationHandler<PageviewCaptured>` (non-async) | Blocks the dispatcher task; provides no benefit because Analyzer's enqueue is already sync-cheap (TryWrite is non-blocking). Async signature future-proofs for handlers that need I/O without breaking the contract. |
| Polling `customizerPageview` table on a timer | Inter-product contract §4 explicitly calls this "the uglier alternative." Adds query load on Customizer's hot table; introduces a sync-lag dimension Analyzer's spec doesn't model. |
| Direct in-process subscribe on Customizer's slice-002 outbox | The outbox is for cross-process delivery (webhooks). Analyzer's subscriber is in-process and shouldn't bypass the in-process notification channel. |

---

## §2 — Bounded-queue + dispatcher pattern

**Decision**: Mirror Customizer's `VisitorWriteQueue` + `VisitorWriteDispatcher` pattern verbatim. New types under `Analyzer.Features.Events.Infrastructure.Dispatcher`:

- `AnalyzerEventReceiptWriteQueue` — wraps `Channel<AnalyzerEventReceiptWriteOp>` constructed with `BoundedChannelOptions { Capacity = options.WriteQueueCapacity, FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false }`. Exposes `bool TryEnqueue(...)` + `ChannelReader<...> Reader`. Singleton DI lifetime.
- `AnalyzerEventReceiptWriteDispatcher : BackgroundService` — drains the channel in batches sized by `FlushBatchSize`, flushed every `FlushIntervalMs` (or earlier when the batch fills), and bulk-inserts via `IAnalyzerEventReceiptRepository.InsertManyAsync(...)`. Hosted-service lifetime.
- `AnalyzerWriteQueueOptions` — `IOptions<>`-bound configuration with `WriteQueueCapacity` (default 10 000), `FlushBatchSize` (default 100), `FlushIntervalMs` (default 250).

**Rationale**: Customizer's pattern is field-tested under Customizer's slice-003 throughput envelope (1000 pv/s sustained, 5000 pv/s peak — the same envelope FR-010 binds Analyzer to). `BoundedChannelFullMode.Wait` + `TryWrite` is the specific combination that lets the caller learn about drops (`TryWrite` returns `false`) and log them — the Clarifications Q2 at-most-once contract. `DropWrite` returns `true` silently and discards internally, which is the wrong shape because the caller can't account for the loss. `SingleReader = true` permits the runtime to optimise away the multi-consumer concurrency overhead — only one dispatcher consumes the channel.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `BoundedChannelFullMode.DropWrite` | Silent loss; caller can't log → violates Clarifications Q2's "drop with warning log" contract. |
| Synchronous insert on the handler thread | Throughput regression — every notification becomes a DB round-trip on the `Task.Run` thread; SC-002's p95 ≤ 2 ms delta likely fails. |
| `IBackgroundTaskQueue` (ASP.NET Core extensions) | Less batching control; not used by Customizer either — would diverge the two products' patterns. |
| Persistent file-backed queue | At-least-once semantics; rejected via Clarifications Q2. Adds disk-IO complexity, fsync cost, and a new operational surface. |

---

## §3 — Cascade-step pattern (hard-delete, atomic-rollback)

**Decision**: `AnalyzerEventReceiptCascadeStep : IAnonymizationCascadeStep` is an `internal sealed` class under `Analyzer.Features.Events.Application.Anonymization`. Its `ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)` delegates straight to `IAnalyzerEventReceiptRepository.DeleteByVisitorKeyAsync(visitorProfileKey, ct)` — one-liner, no orchestration logic. Registered via `builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerEventReceiptCascadeStep>()` in `AnalyzerComposer`.

**Rationale**: Customizer's own `GoalReachedCascadeStep` (`src/Customizer/Features/Goals/Application/Anonymization/GoalReachedCascadeStep.cs`) is exactly this shape — internal sealed, ctor-injects the repository, single-line delete. Following the precedent (a) keeps the cascade-step landscape uniform across products, (b) satisfies the contract's "implementations MUST be side-effect-light per step" remark, (c) inherits Customizer's outer-scope rollback semantic for free (the repository's DB calls open nested scopes that enlist in the outer transaction; a throw rolls the entire `AnonymizeVisitorProfileHandler` atomically).

The earlier draft of the spec asserted "re-key, not delete" and cited a non-existent re-key precedent — that was a misreading. **The actual precedent (`GoalReachedCascadeStep`) is hard-delete.** Re-key is not addressable through `IAnonymizationCascadeStep.ExecuteAsync` anyway: the contract supplies only the original `visitorProfileKey`, and Customizer keeps the same `VisitorProfile.Key` (only rewriting `IdentityRef` from `oid:…` to `anonymized:…`) — there is no "anonymised key" to re-key TO. The constitution's Principle IV wording ("re-keys it deterministically") is interpreted as the visitor's *identity* being re-keyed, not each subsidiary table's row; each subsidiary table participates by doing what makes sense for it, and the established Customizer pattern is delete.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Soft-delete (add `IsAnonymized` column to receipt) | Diverges from `GoalReachedCascadeStep` precedent; complicates every report query with a where-clause filter; preserves aggregate counts at the cost of every consumer carrying anonymisation logic. CCPA right-to-delete obligation favours hard-delete anyway. |
| Re-key to anonymised key | Not addressable through the contract (no anonymised key exists distinct from the visitor key). Logically incoherent. |
| Skip the cascade step entirely | Constitution Principle IV violation — every new Analyzer table MUST register a cascade step. |

---

## §4 — Migration pattern

**Decision**: NPoco DTO + `AsyncMigrationBase` + `IMigrationPlan`, matching Customizer's `M0001..M0010` series. New types:

- `Analyzer.Features.Events.Infrastructure.Persistence.AnalyzerEventReceiptDto` — NPoco-decorated DTO mapping the `analyzerEventReceipt` table (columns + indexes annotated on the DTO).
- `Analyzer.Migrations.M0001_AddAnalyzerEventReceiptTable : AsyncMigrationBase` — `MigrateAsync` calls `Create.Table<AnalyzerEventReceiptDto>().Do()` guarded by `TableExists(Constants.Database.AnalyzerEventReceipt)`.
- `Analyzer.Migrations.AnalyzerMigrationPlan : IMigrationPlan` — lists M0001 only at slice 002; future slices append M0002, M0003, etc.
- `AnalyzerSchemaComposer : IComposer` — registers the migration plan; `[ComposeAfter(typeof(AnalyzerComposer))]` so the schema composer runs after the service registration composer.

**Rationale**: Customizer's existing migration series is the binding precedent. `Create.Table<T>()` keeps the DTO as the single source of truth for column shape; `TableExists` makes migrations re-runnable on re-deploy (slice 001 already established this discipline implicitly because no migrations existed yet to break). `AsyncMigrationBase` is the right base because Umbraco's migration pipeline `await`s migrations; sync migrations can deadlock under certain `SynchronizationContext` configurations.

No SQLite-specific override is needed for slice 002's table — single-column PK (`Id`, Guid). The composite-PK SQLite degradation Customizer's `M0009` works around does not apply here.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| EF Core migrations | Umbraco's native migration framework is NPoco; mixing ORMs is a project-shape diverge that Customizer explicitly avoided. |
| Raw `CREATE TABLE` SQL via `Database.Execute(...)` | Portable but loses NPoco DTO as the schema source of truth; adds DDL drift risk between dev SQLite (if ever) and prod SQL Server. |
| `MigrationBase` (sync) | Risk of deadlock under certain Umbraco hosting configurations; async is the safer default. |

---

## §5 — `IAnalyticsEventStateProvider` runtime shape

**Decision**: Scoped contract with a single member at slice 002. Implementation:

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    /// <summary>
    /// The current request's captured event-receipt — or null if the
    /// subscriber has not yet completed for this request.
    /// </summary>
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }
}
```

Backed by a request-scoped `AnalyticsEventStateStore` class (singleton-shape inside the scope; mutable `CurrentRequestReceipt` field) that the `PageviewCapturedHandler` writes to **when** the handler completes before the request scope is disposed — which is the *rare* case given Customizer's fire-and-forget dispatch. Most reads of `CurrentRequestReceipt` from slice 002 code return `null`; this is documented as expected behaviour and tested in US3 acceptance scenario 1.

**Rationale**: The state provider is introduced now to lock in the contract surface for slice 003+ (sessions, custom events, content app) and the pinning baseline (FR-009). Slice-002 doesn't have a strong consumer; the realistic readers arrive later. The "rarely populated for pageviews" caveat is honest about the dispatch model — Customizer's notification is fire-and-forget on a `Task.Run` thread, so the handler may complete after the request thread has already produced the response. For *in-request* sources (custom events fired by client JS hitting an Analyzer endpoint during the request — slice 004) the provider will populate reliably because the dispatch is in-request, not post-request.

**Threading subtlety**: `AsyncLocal<T>` does **not** flow across the `Task.Run` boundary Customizer uses for fire-and-forget dispatch (the new task captures a copy of the ambient `ExecutionContext` at dispatch time, but mutations inside the task don't propagate back to the request thread). The state-store approach (scoped DI + mutable field on the store) is the correct shape because:

1. The handler resolves its dependencies through `IServiceProvider`, NOT through the request scope (Customizer's `PageviewCapturedNotifier` uses the root service provider for the publish). So the handler must explicitly resolve the scope it wants to write into.
2. The handler can obtain the request scope via `IHttpContextAccessor` IF the request hasn't ended; otherwise the access throws and the handler swallows. This is the rare case where the handler completes fast enough to update the request-scoped store.

Implementation detail for `/speckit-tasks`: the handler ctor takes `IServiceScopeFactory` (not `IServiceProvider`), creates a fresh scope for repository writes (so the receipt write doesn't depend on the request scope being alive), AND opportunistically resolves the request's `AnalyticsEventStateStore` via `IHttpContextAccessor.HttpContext?.RequestServices.GetService<AnalyticsEventStateStore>()` to update the in-flight state — swallowing if the accessor returns null (request already ended).

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `AsyncLocal<AnalyticsEventReceipt?>` | Doesn't flow back through `Task.Run` boundaries; the handler's mutation would be invisible to the request thread anyway. |
| Singleton dictionary keyed by `Pageview.Key` | Cross-request memory growth, no obvious eviction policy, race condition on read between request threads. |
| Defer the contract to slice 003 (sessions) | Principle X says pinning must land before "announced as stable"; the slice-001 deferral already explicitly named slice 002 as the pinning landing slice. Deferring further would re-open the pinning question. |

---

## §6 — Public-surface pinning approach

**Decision**: `Analyzer.Tests.PublicSurface.PublicSurfacePinningTests` follows the shape of Customizer's `PublicSurfacePinningTests` (`src/Customizer.Tests/Unit/SegmentRules/PublicSurfacePinningTests.cs`). Reflection over `Analyzer.dll`, list public types under the **pinned namespaces**:

- `Analyzer.Analytics` — hosts `IAnalyticsEventStateProvider`.
- `Analyzer.Features.Visitors.Application.Contracts` — hosts `IVisitorIdentifier`, `BaseVisitorIdentifier`, `VisitorIdentity` (from slice 001).

Produce canonical-form serialisation (namespace + type kind + member signatures, sorted, normalised whitespace). Byte-compare against `src/Analyzer.Tests/PublicSurface/Baselines/Analyzer-public-surface.txt`. Diff fails the test; intentional updates regenerate the baseline AND require a justification line in the slice's spec or release notes.

**Rationale**: Customizer's pattern works, is field-tested across 10+ slices, and the symmetry between products is itself a value (operator/developer familiarity). Narrow namespace list (per Clarifications Q3) keeps internal refactors free.

Excluded by Clarifications Q3:
- `Analyzer.Features.Events.*` — implementation namespace; receipt entity, handler, queue, dispatcher all internal.
- `Analyzer.Migrations.*` — schema implementation; never consumed.
- `Analyzer.Composers.*` — Umbraco-discovered, not consumer-imported.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| `PublicApiGenerator` NuGet | Extra dependency; Customizer doesn't use; rejected for diff-reduction with sibling. |
| All-namespaces pinning | Traps every internal refactor with a regen step; high-friction. Rejected via Clarifications Q3. |
| Skip pinning at slice 002, defer to a later slice | Violates FR-009 and slice-001's deferral commitment; Principle X requires it before "announced as stable." |

---

## §7 — Integration-test substrate

**Decision**: integration tests target the **Aspire AppHost's persistent SQL Server container** (slice-001 lesson #19). The test base class (`AnalyzerIntegrationTestBase`) reads the connection string from environment / configuration emitted by the AppHost, provisions a per-class schema (`Analyzer_Test_<ClassName>`), runs the Umbraco install + Analyzer's migration plan once, and tears the schema down at class-fixture disposal.

For CI runs where the AppHost isn't pre-running, the test base falls back to **Testcontainers.MsSql** to spin up an ephemeral container — same DTOs, same migrations, same scope semantics. This is the only place Testcontainers is added (NuGet `Testcontainers.MsSql` in `Analyzer.Tests.csproj`); no other source code knows it exists.

**Rationale**: Slice-001 lesson #19 establishes the Aspire persistent volume as the local-dev substrate; SC-006 requires real SQL Server (not SQLite) because the cascade-step's atomic-rollback semantic under Customizer's outer `IScopeProvider` scope cannot be faithfully reproduced on SQLite. Local dev reuses the running container for speed (seconds, not minutes); CI gets isolation via Testcontainers.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| SQLite seam only | Cascade-step's outer-scope rollback semantic doesn't reliably reproduce; SC-006 explicit. |
| Testcontainers everywhere (local + CI) | Slow local dev (minutes per test class spin-up); Aspire AppHost already provides a faster local substrate. |
| Skip integration tests; unit-only | Constitution Principle VI requires integration coverage of every public extension contract — non-negotiable. |

---

## §8 — Idempotency enforcement

**Decision**: Unique index `IDX_AnalyzerEventReceipt_PageviewKey_Unique` on `analyzerEventReceipt(PageviewKey)`. The repository's `InsertAsync` catches the resulting unique-violation exception (`Microsoft.Data.SqlClient.SqlException` with `Number == 2627 || Number == 2601` on SQL Server) and treats it as a successful no-op, emitting `LogDebug("Duplicate dispatch tolerated for PageviewKey={PageviewKey}", op.PageviewKey)` for high-cardinality observability without warning-level noise.

**Rationale**: The unique-index-plus-catch idiom is the standard at-most-once enforcement; cheaper than a read-before-insert (no race window) and cheaper than `MERGE` semantics that differ subtly between providers. `Pageview.Key` is generated once per capture by Customizer's middleware (per Customizer's slice-003 `FR-006`), so it is a safe natural idempotency key.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Read-before-insert | TOCTOU race window between read and write; idempotency not actually enforced. |
| `MERGE` / `INSERT … ON CONFLICT` | Cross-provider grammar differences; complicates the repository contract. |
| Database-level `IDENTITY` PK on `PageviewKey` | Forces composite-key complexity downstream; the table's natural PK is a separate opaque Guid `Id` for symmetry with Customizer's tables (which all carry an `Id` distinct from natural-key columns). |

---

## §9 — Logging conventions

**Decision**: Match Customizer's `PageviewCapturedNotifier` log shape — structured logging with `VisitorKey` + `PageviewKey` as named properties.

- `LogWarning` — bounded-queue drop ("queue at capacity, dropping receipt"), unexpected handler exception swallow.
- `LogDebug` — back-pressure pageview row absent (`PageviewCaptured` fired but `customizerPageview` was dropped under FR-025), duplicate dispatch tolerated.
- `LogInformation` — dispatcher start / stop, migration applied (the Umbraco framework already logs migration steps, so Analyzer's own info-level message is just a "Analyzer event-receipt dispatcher started" boundary marker).
- `LogError` — dispatcher tick failure (per Customizer's `VisitorWriteDispatcher` precedent); never propagates out of the dispatcher loop.

**Rationale**: Operators tailing logs across both products see the same property names (`VisitorKey`, `PageviewKey`) and severity conventions, simplifying troubleshooting. Customizer's existing logs are the binding precedent.

**Alternatives considered**: none worth listing — log-shape divergence between sibling products is a clear anti-pattern.

---

## §10 — Performance smoke test

**Decision**: a single `Analyzer.Tests.Perf.ThroughputSmokeTests` xUnit test marked `[Trait("Category", "Perf")]`. Runs 1000 publishers/second of synthetic `PageviewCaptured` notifications into the live `IEventAggregator` for 60 seconds. Asserts:

1. ≥ 99% of dispatched notifications result in a receipt row at end-of-test (allows the ≤ 1% back-pressure-drop budget from SC-002).
2. p95 latency on the publisher thread is within 2 ms of a baseline measurement collected on the same machine in the same test run with Analyzer subscriber temporarily unregistered.
3. No publisher thread observed to block (sampled by timestamping every TryWrite).

CI invokes this test only when the `Perf` trait is requested (`dotnet test --filter "Category=Perf"`); regular CI runs skip it to keep PR turnaround under the SC-002 5-min target.

**Rationale**: SC-002 is a CI gate; the test is the gate's mechanism. Trait-based opt-in keeps perf flake out of the PR feedback loop while still providing the published-envelope measurement on demand.

**Alternatives considered**:

| Alternative | Why rejected |
|---|---|
| Full HTTP load against a real Umbraco host | More realistic but multi-minute setup; overkill for in-process subscriber characterisation. |
| `BenchmarkDotNet` | Designed for nanosecond-level micro-benchmarks; throughput-over-time isn't its shape. |
| No perf test (rely on production observability) | SC-002 wouldn't have a verifiable success criterion at slice-time. |

---

## §11 — Reference inventory

Source files in `../customizer/src/Customizer/` referenced by this research:

| Path | Purpose in this slice |
|---|---|
| `Features/Visitors/Application/Contracts/PageviewCaptured.cs` | Notification record + subscriber contract docstring (§1, §3). |
| `Features/Visitors/Application/PageviewCapturedNotifier.cs` | Fire-and-forget publish + swallow-and-log pattern (§1, §9). |
| `Middleware/PageviewCaptureMiddleware.cs` | Pageview capture call site; confirms `VisitorProfileKey` is resolved before notification. |
| `Features/Visitors/Dispatcher/VisitorWriteQueue.cs` | Bounded-channel `Wait` + `TryWrite` pattern (§2). |
| `Features/Visitors/Dispatcher/VisitorWriteDispatcher.cs` | `BackgroundService` drain + batch pattern (§2). |
| `Features/Visitors/Application/Contracts/Anonymization/IAnonymizationCascadeStep.cs` | Cascade-step contract (§3). |
| `Features/Visitors/Application/Commands/AnonymizeVisitorProfileCommand.cs` | Outer-scope orchestration; confirms throw-rolls-back semantics (§3). |
| `Features/Goals/Application/Anonymization/GoalReachedCascadeStep.cs` | Hard-delete cascade-step precedent (§3). |
| `Migrations/M0009_AddDocumentTypeSegmentationTables.cs` | `AsyncMigrationBase` + `TableExists` + NPoco DTO pattern (§4). |
| `Composers/VisitorAnalyticsComposer.cs` | Composer wiring of queue + hosted dispatcher (§2, §4). |
| `Tests/Unit/SegmentRules/PublicSurfacePinningTests.cs` | Pinning shape + pinned-namespace list pattern (§6). |

No Customizer file is modified by this slice. All references are read-only.

---

## §12 — Open items deferred from research to `/speckit-tasks`

These are implementation details that the task generation phase will pin down concretely; none affects the plan's gate status or the data model.

- Exact `WriteQueueCapacity` default value (10 000 proposed; tune against perf-smoke results during slice impl).
- `FlushBatchSize` default (100 proposed; matches Customizer's default).
- `FlushIntervalMs` default (250 proposed).
- Whether the perf-smoke test should also run a hostile-load test at 5000 pv/s peak (SC-002 only requires 1000 pv/s sustained; peak is non-load-bearing for the success criterion).
- Whether to add a `dotnet-counters`-friendly `EventCounter` for queue-drop and queue-depth (deferred; observability is the slice's later concern).
