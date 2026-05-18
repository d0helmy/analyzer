# Phase 1 Data Model: Pageview Subscription + Analytics-Event State Provider

**Slice**: 002 — pageview subscription
**Date**: 2026-05-18
**Constitution**: v1.1.0
**Reference**: [`spec.md`](spec.md) FR-002/003/004/006/008, Key Entities; [`plan.md`](plan.md) §Storage; [`research.md`](research.md) §3, §4, §5.

This document fixes the concrete shape of every persisted and in-memory entity slice 002 introduces. Column names, types, constraints, and indexes are normative — the tasks phase generates code from this; the migration class encodes it as the schema.

---

## §1 — Persisted entity: `analyzerEventReceipt`

One row per `PageviewCaptured` notification successfully processed by Analyzer's subscriber. The first Analyzer-owned table.

### Columns

| Column | Type (SQL Server) | NPoco-DTO type | Null | Default | Purpose |
|---|---|---|---|---|---|
| `id` | `uniqueidentifier` | `Guid` | NOT NULL | `NEWID()`-equivalent (set by application code, not DB) | Opaque primary key. Symmetry with Customizer's per-table `Id` convention; never re-used as a public identifier. |
| `pageviewKey` | `uniqueidentifier` | `Guid` | NOT NULL | — | Soft reference to `customizerPageview.key`. Indexed and unique (idempotency). **No DB-level FK constraint** — Customizer may drop the parent row under back-pressure (`FR-025`), so a hard FK would fail in those cases (Clarifications Q2; FR-002). |
| `visitorProfileKey` | `uniqueidentifier` | `Guid` | NOT NULL | — | Hard FK to `customizerVisitorProfile.key`. The visitor profile is guaranteed to exist by the time `PageviewCaptured` fires (`plan.md` Constraints; FR-002). |
| `receivedUtc` | `datetimeoffset(7)` | `DateTimeOffset` | NOT NULL | — | Capture timestamp recorded by the subscriber (NOT `Pageview.RequestUtc`; that's Customizer's capture time, and may differ slightly from when Analyzer's handler observes the notification). Used by a future slice's pruning job (Clarifications Q1). |

No columns are added in slice 002 beyond this set. Future slices append columns additively per Constitution Principle X.

### Constraints

- **Primary key**: `PK_analyzerEventReceipt (id)`.
- **Unique index**: `UX_analyzerEventReceipt_pageviewKey (pageviewKey)` — enforces idempotency (FR-004; `research.md` §8). Insert collisions throw a unique-violation that the repository catches and treats as success.
- **Foreign key**: `FK_analyzerEventReceipt_VisitorProfile (visitorProfileKey) REFERENCES customizerVisitorProfile(key)`. No cascade rules on this FK — Customizer's `AnonymizeVisitorProfileHandler` keeps the same `Key` (it only overwrites `IdentityRef`), so the FK target never disappears; the Analyzer-side cascade-step (`AnalyzerEventReceiptCascadeStep`) is what deletes child rows inside the same outer scope (`research.md` §3).
- **Non-unique index**: `IDX_analyzerEventReceipt_receivedUtc (receivedUtc)` — supports the future date-range pruning job's `WHERE receivedUtc < @cutoff` predicate efficiently (Clarifications Q1; FR-003).
- **Non-unique index**: `IDX_analyzerEventReceipt_visitorProfileKey (visitorProfileKey)` — supports the cascade-step's `DELETE … WHERE visitorProfileKey = @key` (FR-006; SC-003 200 ms / 10 k rows budget).

### NPoco DTO shape (`Analyzer.Features.Events.Infrastructure.Persistence.AnalyzerEventReceiptDto`)

```csharp
[TableName(Constants.Database.AnalyzerEventReceipt)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
public sealed class AnalyzerEventReceiptDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("pageviewKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerEventReceipt_pageviewKey")]
    public Guid PageviewKey { get; set; }

    [Column("visitorProfileKey")]
    [ForeignKey(typeof(VisitorProfileDto), Column = "key", Name = "FK_analyzerEventReceipt_VisitorProfile")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerEventReceipt_visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerEventReceipt_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
```

The `[ForeignKey(typeof(VisitorProfileDto), …)]` reference depends on the Customizer-side DTO being visible to Analyzer. Two options at task time:

1. Import Customizer's `VisitorProfileDto` directly (it lives in `Customizer.Features.Visitors.Persistence`; reachable via the existing `ProjectReference`).
2. Declare the FK via raw SQL in the migration body (decoupled from Customizer's internal DTO surface).

**Pinned decision**: option 2 — declare the FK in the migration's `MigrateAsync` body via `Database.Execute("ALTER TABLE … ADD CONSTRAINT FK_… FOREIGN KEY … REFERENCES customizerVisitorProfile(key)")`. Reason: option 1 imports a Customizer *internal* type into Analyzer code (Customizer's persistence DTOs are not part of the pinned public surface), which is a Principle III adjacency we should avoid. Raw-SQL FK declaration also matches Customizer's own `M0009` SQLite-override precedent for non-trivial schema declarations.

### Estimated volume

At 1000 pv/s sustained per FR-010 ⇒ ~86 M rows/day. Per Clarifications Q1, slice 002 ships no pruning; a deploying organisation reaching this volume needs the future pruning slice before production rollout. The `IDX_receivedUtc` index is the only pruning-supporting affordance in slice 002.

---

## §2 — Domain record: `AnalyticsEventReceipt`

The immutable in-memory projection consumed by `IAnalyticsEventStateProvider.CurrentRequestReceipt`. Distinct from the DTO so callers never accidentally couple to NPoco attributes.

```csharp
namespace Analyzer.Features.Events.Domain;

/// <summary>
/// One captured pageview as Analyzer observed it. Immutable; constructed
/// once by <c>PageviewCapturedHandler</c> at notification time and
/// surfaced through <see cref="IAnalyticsEventStateProvider.CurrentRequestReceipt"/>
/// for the rare case the handler completes before the request scope
/// is disposed (typically null on the pageview request itself; reliably
/// populated by slice 004's in-request custom-event dispatches).
/// </summary>
/// <param name="Id">Opaque receipt identifier (per-row PK).</param>
/// <param name="PageviewKey">Soft pointer to <c>customizerPageview.Key</c>.</param>
/// <param name="VisitorProfileKey">Hard FK to <c>customizerVisitorProfile.Key</c>.</param>
/// <param name="ReceivedUtc">When Analyzer observed the notification.</param>
public sealed record AnalyticsEventReceipt(
    Guid Id,
    Guid PageviewKey,
    Guid VisitorProfileKey,
    DateTimeOffset ReceivedUtc);
```

Visibility: `public` — referenced by the public `IAnalyticsEventStateProvider` member, so it must be public too. Pinned via `PublicSurfacePinningTests` (FR-009 + SC-005). Lives in `Analyzer.Analytics` (alongside `IAnalyticsEventStateProvider`) — pre-decided by `/speckit-analyze` finding U2 to eliminate the transitive-pinning empirical branch; the record sits inside the pinned namespace list so the pinning baseline captures it directly.

The record's namespace is the consumer-facing root (`Analyzer.Analytics`), not the implementation-internal `Analyzer.Features.Events.Domain`, because (a) it is exposed on a public interface, (b) the pinning baseline scope per Clarifications Q3 already pins `Analyzer.Analytics`, and (c) symmetry with Customizer's `Customizer.Analytics`-rooted consumer-facing types.

---

## §3 — Request-scoped state store: `AnalyticsEventStateStore`

The mutable backing store the handler writes to and the state provider reads from. Internal to Analyzer; not part of the pinned surface.

```csharp
namespace Analyzer.Features.Events.Application;

/// <summary>
/// Scoped backing store for <see cref="IAnalyticsEventStateProvider"/>.
/// One instance per request scope; the <c>PageviewCapturedHandler</c>
/// writes the receipt here opportunistically (best-effort), and the
/// state provider exposes it to in-process consumers. Internal: only
/// reachable through the public <see cref="IAnalyticsEventStateProvider"/>
/// surface.
/// </summary>
internal sealed class AnalyticsEventStateStore
{
    private AnalyticsEventReceipt? _current;

    public AnalyticsEventReceipt? CurrentRequestReceipt => _current;

    public void SetCurrentReceipt(AnalyticsEventReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _current = receipt;
    }
}
```

DI lifetime: **Scoped** (per-request). One instance lives for the duration of a request; both `IAnalyticsEventStateProvider` and `PageviewCapturedHandler`-via-`IHttpContextAccessor` resolve it through the same request `IServiceProvider`.

### Concurrency model

- Single writer per request: the handler runs once per `PageviewCaptured` notification, and at most one notification per request hits any given scope. No locking required.
- Multiple readers per request: the in-process consumers (later slices) all run on the request thread; reads are sequenced by .NET's normal thread visibility. The single-write-one-or-zero-reader case at slice 002 doesn't stress this; future slices may need to upgrade to `Interlocked.CompareExchange` if a multi-write contention emerges. Slice 002 doesn't preempt that.

---

## §4 — Configuration: `AnalyzerWriteQueueOptions`

Tunables for the bounded queue + dispatcher. Bound from `appsettings.json` under the `Analyzer:WriteQueue` section via `IOptions<T>`.

```csharp
namespace Analyzer.Features.Events.Infrastructure.Dispatcher;

internal sealed class AnalyzerWriteQueueOptions
{
    /// <summary>
    /// Maximum pending receipts in the bounded queue. When full,
    /// further TryEnqueue calls return false and the caller logs the
    /// drop (at-most-once delivery; Clarifications Q2).
    /// </summary>
    public int WriteQueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// Maximum receipts per batch flushed to the DB by the dispatcher.
    /// </summary>
    public int FlushBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time the dispatcher waits before flushing a partial
    /// batch. Smaller intervals trade DB round-trip frequency for
    /// in-memory residency time.
    /// </summary>
    public int FlushIntervalMs { get; set; } = 250;
}
```

Defaults match the `research.md` §2 proposed values. Operators can tune via `appsettings.json` without rebuilding.

---

## §5 — Cascade-step contract surface (already public on Customizer's side)

Analyzer's cascade-step implements Customizer's `IAnonymizationCascadeStep` directly; the interface itself is not re-declared. The step's external observable contract:

```csharp
namespace Analyzer.Features.Events.Application.Anonymization;

internal sealed class AnalyzerEventReceiptCascadeStep : IAnonymizationCascadeStep
{
    // Signature (from Customizer):
    //   Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct);
    //
    // Semantics for Analyzer's table:
    //   DELETE FROM analyzerEventReceipt WHERE visitorProfileKey = @visitorProfileKey;
    //
    // Runs inside Customizer's outer NPoco scope; nested scope enlists
    // transparently. Throw rolls outer back unconditionally.
}
```

No data model on Analyzer's side — the cascade step is a pure operation on `analyzerEventReceipt` rows keyed by `visitorProfileKey`. The repository's `DeleteByVisitorKeyAsync(visitorProfileKey, ct)` is the bound write.

---

## §6 — Constants

A single new entry in `Analyzer.Constants.Database`:

```csharp
namespace Analyzer;

public static class Constants
{
    public static class Database
    {
        // Slice 001 introduced no Analyzer-owned tables; this constant
        // arrives with slice 002. Future Analyzer tables (sessions,
        // custom events, etc.) add their own constants here.
        public const string AnalyzerEventReceipt = "analyzerEventReceipt";
    }
}
```

The string literal `"analyzerEventReceipt"` is the only canonical occurrence; the DTO's `[TableName]` attribute, the migration's `TableExists` guard, the cascade-step's DELETE, and any future raw-SQL queries all reference this constant.

---

## §7 — State transitions

`analyzerEventReceipt` rows have only two states in their lifecycle:

| State | Trigger | Effect |
|---|---|---|
| **Created** | `PageviewCapturedHandler` enqueues a write op; dispatcher flushes a batch containing it. | Row appears in the table. Unique-index on `pageviewKey` rejects duplicate-creation attempts at the DB layer; the repository catches and logs at debug level. |
| **Deleted** | `AnalyzerEventReceiptCascadeStep.ExecuteAsync` runs as part of `AnonymizeVisitorProfileCommand`. | Row is hard-deleted. No tombstone, no soft-delete flag (Clarifications-corrected; see `research.md` §3). |

There is no "updated" transition. Receipts are append-only-then-deleted.

The state-store's `AnalyticsEventReceipt` projection (`§2`) has a corresponding lifecycle:

| State | Trigger | Effect |
|---|---|---|
| **Unset (null)** | Default state of every request scope. | `IAnalyticsEventStateProvider.CurrentRequestReceipt` returns `null`. |
| **Set** | Handler completes before the request scope ends AND successfully resolves the scope via `IHttpContextAccessor`. | `CurrentRequestReceipt` returns the receipt. Rare on the pageview request itself; reliable on in-request dispatches at later slices. |

Slice 002 doesn't model an "anonymised" state on the in-memory projection — once a request scope ends, its `AnalyticsEventStateStore` is disposed regardless.

---

## §8 — Validation rules

| Source field | Validation | Action if violated |
|---|---|---|
| `Pageview.Key` | Non-empty Guid | Skip with debug log ("malformed PageviewCaptured: empty Pageview.Key"); FR-005 swallow path. Never reaches the queue. |
| `Pageview.VisitorProfileKey` | Non-empty Guid | Skip with warning log ("config-error: VisitorProfileKey empty"); FR-012 cites FR-ID-05. Never reaches the queue. |
| `Pageview.RequestUtc` | Reasonable timestamp (informational only) | Not validated. We record `DateTimeOffset.UtcNow` from the handler's perspective via `TimeProvider`, not Customizer's capture time. |

The Analyzer-side `ReceivedUtc` is sourced from an injected `TimeProvider.System` per Customizer's slice-007 precedent — testable via `FakeTimeProvider` in unit tests.

---

## §9 — Open questions

None — all data-model decisions are bound. The only refinement still open is the canonical-form pinning behaviour for `AnalyticsEventReceipt` (§2 refinement note), which is verified empirically by the pinning test in `/speckit-tasks` and adjusted by moving the record namespace if needed.
