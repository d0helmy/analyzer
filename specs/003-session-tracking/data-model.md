# Phase 1 Data Model: Sessions

**Slice**: 003 — session tracking
**Date**: 2026-05-18
**Constitution**: v1.1.1
**Reference**: [`spec.md`](spec.md) FR-003/004/006/008/011, Key Entities; [`plan.md`](plan.md) §Storage; [`research.md`](research.md) §1, §4, §7, §11.

This document fixes the concrete shape of every persisted and in-memory entity slice 003 introduces or modifies. Column names, types, constraints, and indexes are normative — the tasks phase generates code from this; the migration class encodes it as the schema.

---

## §1 — Persisted entity: `analyzerSession` (NEW)

One row per session. A bounded sequence of pageviews by one visitor on one device within the configured inactivity timeout. The second Analyzer-owned table (after `analyzerEventReceipt`).

### Columns

| Column | Type (SQL Server) | NPoco-DTO type | Null | Default | Purpose |
|---|---|---|---|---|---|
| `id` | `uniqueidentifier` | `Guid` | NOT NULL | set by app | Opaque primary key. Symmetry with Customizer's per-table `Id` convention; never used as a public identifier. |
| `sessionKey` | `uniqueidentifier` | `Guid` | NOT NULL | set by app | Publicly-exposed stable alternate key (the FK target from `analyzerEventReceipt.sessionKey`; the value returned by `IAnalyzerSessionResolver.ResolveAsync`; exposed on the public `AnalyticsSession` record). Unique non-clustered index. |
| `visitorProfileKey` | `uniqueidentifier` | `Guid` | NOT NULL | — | Hard FK to `customizerVisitorProfile.key`. Customizer's middleware never publishes `PageviewCaptured` with an empty visitor key; the resolver inherits that guarantee. |
| `deviceKey` | `nvarchar(64)` | `string` | NOT NULL | empty-string after anonymisation | Server-side hash of `User-Agent` (16 hex chars truncated SHA-256; see `research.md` §5). Not on the public surface. Cleared (set to empty string, NOT null) by the cascade step on anonymisation. |
| `startUtc` | `datetimeoffset(7)` | `DateTimeOffset` | NOT NULL | — | When the session opened. Set once at insert; never updated. |
| `lastActivityUtc` | `datetimeoffset(7)` | `DateTimeOffset` | NOT NULL | — | When the most recent attached pageview was observed. Advances on every `Extend` operation. |
| `endUtc` | `datetimeoffset(7)` | `DateTimeOffset?` | NULL | NULL | Logical close time. Set to `lastActivityUtc + inactivityTimeout` by either the lazy-close branch (US1 AS3) or the sweeper (US3 AS1). Never `now`. |
| `pageviewCount` | `int` | `int` | NOT NULL | 1 | Number of pageviews attached to the session so far. Incremented atomically on every `Extend` operation (`pageviewCount = pageviewCount + 1`). |
| `isActive` | `bit` | `bool` | NOT NULL | 1 | True while the session is open; false once closed (lazy-close or sweeper). Partial-unique-index predicate. |
| `anonymizedUtc` | `datetimeoffset(7)` | `DateTimeOffset?` | NULL | NULL | When the cascade step soft-anonymised the session. Null for non-anonymised rows. `WHERE anonymizedUtc IS NULL` is the cascade step's idempotency predicate. |

No columns are added in slice 003 beyond this set. Future slices append columns additively per Constitution Principle X.

### Constraints

- **Primary key**: `PK_analyzerSession (id)`.
- **Unique index**: `UX_analyzerSession_sessionKey (sessionKey)` — exposes a stable alternate key; allows `analyzerEventReceipt.sessionKey` to point at it without coupling to the opaque `id`.
- **Partial unique index** (SQL Server only — emitted by raw SQL): `UX_analyzerSession_active_visitor_device (visitorProfileKey, deviceKey) WHERE isActive = 1` — enforces the "exactly one active session per `(visitor, device)`" invariant (FR-003; race-safety per `research.md` §4). Skipped on SQLite (lesson #39); application-layer single-instance dev path is sufficient there.
- **Foreign key** (SQL Server only — emitted by raw SQL, skipped on SQLite per lesson #39): `FK_analyzerSession_VisitorProfile (visitorProfileKey) REFERENCES customizerVisitorProfile(key)`. No cascade rule — Customizer's anonymisation keeps the same `Key` on the visitor row (it only overwrites `IdentityRef`); Analyzer-side cascade is handled by `AnalyzerSessionCascadeStep`.
- **Non-unique index**: `IDX_analyzerSession_sweep (isActive, lastActivityUtc)` — supports the sweeper's predicate `WHERE isActive = 1 AND lastActivityUtc < @cutoff` efficiently.
- **Non-unique index**: `IDX_analyzerSession_visitorProfileKey (visitorProfileKey)` — supports the cascade-step's `UPDATE … WHERE visitorProfileKey = @key` (SC-004 200 ms / 1 000 rows budget).

### NPoco DTO shape (`Analyzer.Features.Sessions.Infrastructure.Persistence.AnalyzerSessionDto`)

```csharp
[TableName(Constants.Database.AnalyzerSession)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerSessionDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("sessionKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerSession_sessionKey")]
    public Guid SessionKey { get; set; }

    [Column("visitorProfileKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerSession_visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("deviceKey")]
    [Length(64)]
    public string DeviceKey { get; set; } = string.Empty;

    [Column("startUtc")]
    public DateTimeOffset StartUtc { get; set; }

    [Column("lastActivityUtc")]
    public DateTimeOffset LastActivityUtc { get; set; }

    [Column("endUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTimeOffset? EndUtc { get; set; }

    [Column("pageviewCount")]
    public int PageviewCount { get; set; }

    [Column("isActive")]
    public bool IsActive { get; set; }

    [Column("anonymizedUtc")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public DateTimeOffset? AnonymizedUtc { get; set; }
}
```

The partial unique index (`UX_analyzerSession_active_visitor_device`), the FK (`FK_analyzerSession_VisitorProfile`), and the sweep index (`IDX_analyzerSession_sweep`) are declared in the migration body via raw SQL, NOT via NPoco attributes — NPoco's `[Index]` attribute doesn't model `WHERE` clauses, and the FK declaration follows the slice-002 precedent of avoiding a Customizer-internal-DTO import (Principle III; `research.md` §7).

### Estimated volume

At ~10 000 distinct visitors per organisation × ~5 sessions per visitor per day = ~50 000 new session rows/day. Active-row subset (sweepable predicate): tens of thousands at peak, dropping to near-zero overnight as the sweeper closes inactivity-exceeded rows. The partial unique index applies only to the active subset, so its size is bounded by the active session count, not the historical session count.

Long-tail historical retention is out of scope for slice 003; the future pruning slice (slice 015 or wherever) will introduce a retention policy. The `IDX_analyzerSession_sweep` index supports the eventual pruning query as a side benefit.

---

## §2 — Persisted entity modification: `analyzerEventReceipt.sessionKey` (NEW COLUMN)

Slice 002's `analyzerEventReceipt` table gains one nullable column.

### Column added

| Column | Type (SQL Server) | NPoco-DTO type | Null | Default | Purpose |
|---|---|---|---|---|---|
| `sessionKey` | `uniqueidentifier` | `Guid?` | NULL | NULL | Soft FK to `analyzerSession.sessionKey`. Slice-003-and-later receipts have it populated by the resolver; slice-002 receipts persisted before slice 003 deployment carry `null` (no back-fill — pre-sessions cohort per FR-004). |

### Constraints added

- **Non-unique index**: `IDX_analyzerEventReceipt_sessionKey (sessionKey)` — supports cross-table joins `analyzerEventReceipt JOIN analyzerSession ON sessionKey` efficiently. Slice 005's content app + slice 010's reports both project from this join.
- **No FK constraint**. Per FR-004 + spec edge case "Customizer drops the parent customizerPageview row under back-pressure" — the parent session row is always durable for slice-003-and-later receipts (resolver writes synchronously to the handler thread), but a future anonymisation cascade can soft-anonymise the session row without deleting it, AND a future pruning slice may delete old session rows independently of receipts; soft FK retains the analytical value of the historical pointer without the constraint failing in those cases.

### DTO change

The existing `AnalyzerEventReceiptDto` gains:

```csharp
[Column("sessionKey")]
[NullSetting(NullSetting = NullSettings.Null)]
[Index(IndexTypes.NonClustered, Name = "IDX_analyzerEventReceipt_sessionKey")]
public Guid? SessionKey { get; set; }
```

The repository's `InsertAsync` maps `AnalyticsEventReceipt.SessionKey` (the new init-only property; §4 below) to `AnalyzerEventReceiptDto.SessionKey`. The `DeleteByVisitorKeyAsync` cascade is unchanged (still hard-deletes by `visitorProfileKey`).

---

## §3 — Migration: `M0002_AddAnalyzerSessionTableAndReceiptSessionKey` (NEW)

Bundles both schema changes (new table + new column) into one `AsyncMigrationBase` migration. Idempotent via `TableExists` + `ColumnExists` guards.

```csharp
public sealed class M0002_AddAnalyzerSessionTableAndReceiptSessionKey : AsyncMigrationBase
{
    public M0002_AddAnalyzerSessionTableAndReceiptSessionKey(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        // 1) Create analyzerSession table (NPoco-driven via DTO).
        if (TableExists(Constants.Database.AnalyzerSession) is false)
        {
            Create.Table<AnalyzerSessionDto>().Do();

            // SQL Server only: FK + partial unique index + sweep index.
            // SQLite skip per lesson #39.
            if (!isSqlite)
            {
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSession}] " +
                    $"ADD CONSTRAINT [FK_analyzerSession_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                Database.Execute(
                    "CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerSession_active_visitor_device] " +
                    $"ON [{Constants.Database.AnalyzerSession}] ([visitorProfileKey], [deviceKey]) " +
                    "WHERE [isActive] = 1");

                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerSession_sweep] " +
                    $"ON [{Constants.Database.AnalyzerSession}] ([isActive], [lastActivityUtc])");
            }
        }

        // 2) Add sessionKey column to analyzerEventReceipt (additive).
        if (ColumnExists(Constants.Database.AnalyzerEventReceipt, "sessionKey") is false)
        {
            Alter.Table(Constants.Database.AnalyzerEventReceipt)
                 .AddColumn("sessionKey").AsGuid().Nullable().Do();

            Create.Index("IDX_analyzerEventReceipt_sessionKey")
                  .OnTable(Constants.Database.AnalyzerEventReceipt)
                  .OnColumn("sessionKey")
                  .Ascending()
                  .Do();
        }

        return Task.CompletedTask;
    }
}
```

Plan chain in `AnalyzerMigrationPlan`:

```csharp
From(string.Empty)
    .To<M0001_AddAnalyzerEventReceiptTable>("0001-AddAnalyzerEventReceiptTable")
    .To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey");
```

---

## §4 — Public record modification: `AnalyticsEventReceipt` (additive)

The slice-002 `Analyzer.Analytics.AnalyticsEventReceipt` record gains one init-only property — non-breaking per `research.md` §11.

```csharp
namespace Analyzer.Analytics;

public sealed record AnalyticsEventReceipt(
    Guid Id,
    Guid PageviewKey,
    Guid VisitorProfileKey,
    DateTimeOffset ReceivedUtc)
{
    /// <summary>
    /// Soft FK to <c>analyzerSession.sessionKey</c>. Populated by
    /// slice-003's session resolver before the receipt is enqueued.
    /// Null for receipts persisted by slice-002 deployments — the
    /// pre-sessions cohort, not back-filled by <c>M0002</c> per
    /// FR-004.
    /// </summary>
    public Guid? SessionKey { get; init; }
}
```

The slice-002 positional constructor `(Id, PageviewKey, VisitorProfileKey, ReceivedUtc)` is preserved — binary-compatible. New code constructs with the `with`-expression: `new AnalyticsEventReceipt(id, pvKey, visKey, utc) with { SessionKey = sessionKey }`.

Pinning baseline picks up the new property line. Documented as additive in `spec.md` Assumptions (pinning regen).

---

## §5 — Public record: `AnalyticsSession` (NEW)

The consumer-facing immutable projection of a session row. Lives in `Analyzer.Analytics` alongside `AnalyticsEventReceipt` (pinned namespace per slice-002 Clarifications Q3).

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// A bounded sequence of pageviews by one visitor on one device within
/// the configured inactivity timeout. The consumer-facing projection of
/// an <c>analyzerSession</c> row.
/// </summary>
/// <remarks>
/// <para>
/// Public + pinned. The internal <c>deviceKey</c> column on the row is
/// intentionally NOT exposed on this record — it's a server-side
/// resolution artefact, not a public device dimension. Consumers
/// attributing sessions to devices should derive from a future receipt
/// row's <c>UserAgent</c> column when slice ~006 surfaces it.
/// </para>
/// <para>
/// Constructed once by <c>AnalyzerSessionResolver</c> and surfaced
/// through <see cref="IAnalyticsEventStateProvider.CurrentSession"/>
/// for in-process consumers. Breaking changes are PROHIBITED outside a
/// MAJOR release (Constitution Principle X).
/// </para>
/// </remarks>
/// <param name="SessionKey">
/// Publicly-exposed stable identifier; matches
/// <c>analyzerEventReceipt.sessionKey</c> on the receipt rows attributed
/// to this session.
/// </param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>.
/// </param>
/// <param name="StartUtc">When the session opened. Set once at insert.</param>
/// <param name="LastActivityUtc">
/// When the most recent attached pageview was observed. Advances on
/// every <c>Extend</c> operation.
/// </param>
/// <param name="EndUtc">
/// Logical close time — <c>lastActivityUtc + inactivityTimeout</c>.
/// Null while the session is still active.
/// </param>
/// <param name="PageviewCount">
/// Number of pageviews attached to this session so far. May exceed
/// <c>COUNT(*) FROM analyzerEventReceipt WHERE sessionKey = …</c> under
/// slice-002 back-pressure drops (spec edge case explicit).
/// </param>
/// <param name="IsActive">
/// True while the session is open; false once closed.
/// </param>
public sealed record AnalyticsSession(
    Guid SessionKey,
    Guid VisitorProfileKey,
    DateTimeOffset StartUtc,
    DateTimeOffset LastActivityUtc,
    DateTimeOffset? EndUtc,
    int PageviewCount,
    bool IsActive);
```

Lives in `Analyzer.Analytics`. Pinned via the existing `PublicSurfacePinningTests` baseline (regenerated as part of this slice; spec Assumptions documents the regen as additive MINOR-level change per Principle X).

### Why `deviceKey` is NOT on the public record

The `deviceKey` column is a server-side resolution artefact — a truncated SHA-256 of `User-Agent`. It is not a stable public identifier; it is not human-readable; it carries no analytic value to a consumer. Exposing it on the public record would:

1. **Couple consumers to the hash algorithm** — changing the hash (e.g., increasing the truncation, switching to a stronger family) would break the public contract.
2. **Imply that `deviceKey` is queryable from the public surface** — but the only consumer reading `CurrentSession` is in-process, where the `User-Agent` is already available via `HttpContext.Request.Headers.UserAgent`.
3. **Surface a privacy-adjacent value through the contract** — even though it's a server-side hash, a future audit might raise concerns. Keeping it off the public surface is the conservative posture.

When a future slice (proposed slice ~006) introduces a richer device dimension (UA-parser output, browser+OS+device family), it will land on the receipt row, not retroactively on the session row.

---

## §6 — Internal record: `AnalyticsSessionCacheEntry`

The cache store's value type. Internal to Analyzer; not part of the pinned surface.

```csharp
namespace Analyzer.Features.Sessions.Application;

internal sealed record AnalyticsSessionCacheEntry(
    Guid SessionKey,
    DateTimeOffset LastActivityUtc,
    DateTimeOffset OpenedUtc);
```

Stored in `AnalyzerSessionCacheStore`'s `MemoryCache` keyed by `string` of shape `$"{visitorProfileKey:N}|{deviceKey}"`. Size: 1 per entry; capacity bounded by `MemoryCacheOptions.SizeLimit = options.CacheCapacity`.

---

## §7 — Configuration: `AnalyzerSessionOptions` (NEW)

Tunables for the session subsystem. Bound from `appsettings.json` under `Analyzer:Session` via `IOptionsMonitor<T>` (runtime-reloadable per FR-008).

```csharp
namespace Analyzer.Features.Sessions.Infrastructure.Configuration;

internal sealed class AnalyzerSessionOptions
{
    /// <summary>
    /// Inactivity timeout in minutes. A session whose
    /// <c>lastActivityUtc + InactivityTimeoutMinutes</c> is in the past
    /// is closed by either the lazy-close branch (US1 AS3) or the
    /// sweeper (US3 AS1).
    /// </summary>
    public int InactivityTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Sweeper tick interval in seconds. Smaller intervals close
    /// inactive sessions faster, at the cost of more frequent DB scans.
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum sessions closed per sweeper tick. Bounds the per-tick
    /// UPDATE statement size; keeps individual statements well below
    /// any reasonable SQL timeout.
    /// </summary>
    public int SweepBatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum entries held in the in-memory active-session cache. LRU
    /// eviction once exceeded.
    /// </summary>
    public int CacheCapacity { get; set; } = 10_000;
}
```

Sample `appsettings.json` block:

```json
{
  "Analyzer": {
    "Session": {
      "InactivityTimeoutMinutes": 30,
      "SweepIntervalSeconds": 60,
      "SweepBatchSize": 1000,
      "CacheCapacity": 10000
    }
  }
}
```

`InactivityTimeoutMinutes`, `SweepIntervalSeconds`, `SweepBatchSize` reload at runtime; `CacheCapacity` is captured at first `AnalyzerSessionCacheStore` construction (the `MemoryCache.SizeLimit` is fixed for the lifetime of the cache; rebuilding it would lose all entries). A capacity change requires a host restart — acceptable operational tradeoff, documented in the contract.

---

## §8 — Repository contract: `IAnalyzerSessionRepository` (NEW)

```csharp
namespace Analyzer.Features.Sessions.Infrastructure.Persistence;

internal interface IAnalyzerSessionRepository
{
    /// <summary>
    /// Return the most-recent active session for
    /// <paramref name="visitorProfileKey"/> + <paramref name="deviceKey"/>,
    /// or null if none. Used by the resolver on cache miss + by the
    /// race-collision retry path.
    /// </summary>
    Task<AnalyticsSession?> GetLatestActiveAsync(
        Guid visitorProfileKey,
        string deviceKey,
        CancellationToken ct);

    /// <summary>
    /// Open a new session row. Throws DbException (unique-violation) if
    /// the partial unique index <c>UX_analyzerSession_active_visitor_device</c>
    /// catches a race. The resolver catches and re-reads on collision.
    /// </summary>
    Task InsertAsync(AnalyzerSessionDto session, CancellationToken ct);

    /// <summary>
    /// UPDATE <c>lastActivityUtc</c> + increment <c>pageviewCount</c>
    /// atomically for the active row keyed by <paramref name="sessionKey"/>.
    /// </summary>
    Task ExtendAsync(Guid sessionKey, DateTimeOffset newLastActivityUtc, CancellationToken ct);

    /// <summary>
    /// UPDATE <c>isActive = false, endUtc = logicalCloseUtc</c> on the
    /// row keyed by <paramref name="sessionKey"/>. Idempotent (UPDATE
    /// against an already-closed row is a no-op).
    /// </summary>
    Task CloseAsync(Guid sessionKey, DateTimeOffset logicalCloseUtc, CancellationToken ct);

    /// <summary>
    /// UPDATE TOP(@batchSize) active rows whose
    /// <c>lastActivityUtc + inactivityTimeout &lt; now</c>; set
    /// <c>isActive = false, endUtc = lastActivityUtc + inactivityTimeout</c>.
    /// Returns the affected row count + the sessionKeys closed (so the
    /// sweeper can invalidate cache entries).
    /// </summary>
    Task<IReadOnlyList<Guid>> SweepEligibleAsync(
        DateTimeOffset cutoff,
        TimeSpan inactivityTimeout,
        int batchSize,
        CancellationToken ct);

    /// <summary>
    /// UPDATE all rows where <c>visitorProfileKey = @key AND
    /// anonymizedUtc IS NULL</c>; set <c>anonymizedUtc = now,
    /// deviceKey = ''</c>. Idempotent re-runs return zero rows affected.
    /// Returns the sessionKeys soft-anonymised so the cascade step can
    /// invalidate cache entries.
    /// </summary>
    Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(
        Guid visitorProfileKey,
        CancellationToken ct);
}
```

Implementation (`AnalyzerSessionRepository`) opens nested `IScopeProvider.CreateScope()` per call — when the caller has already opened an outer scope (e.g., `AnonymizeVisitorProfileHandler`), the nested scope enlists in the outer transaction and rolls back atomically on a throw (slice-002 pattern; `research.md` §3).

---

## §9 — Cache store contract: `AnalyzerSessionCacheStore` (NEW)

```csharp
namespace Analyzer.Features.Sessions.Application;

internal sealed class AnalyzerSessionCacheStore : IDisposable
{
    public AnalyzerSessionCacheStore(
        IOptionsMonitor<AnalyzerSessionOptions> options,
        TimeProvider timeProvider);

    public bool TryGet(
        Guid visitorProfileKey,
        string deviceKey,
        out AnalyticsSessionCacheEntry entry);

    public void UpdateActivity(
        Guid visitorProfileKey,
        string deviceKey,
        Guid sessionKey,
        DateTimeOffset lastActivityUtc);

    public void Invalidate(Guid visitorProfileKey, string deviceKey);

    public void InvalidateBySessionKey(Guid sessionKey);

    public void InvalidateByVisitorKey(Guid visitorProfileKey);

    public void Dispose();
}
```

DI lifetime: **Singleton** (one cache per host instance; the cache spans request scopes by definition). The store internally constructs a `MemoryCache` with `SizeLimit = options.CurrentValue.CacheCapacity` and `Size = 1` per entry. `Dispose` disposes the `MemoryCache`.

Cross-instance coherence: NONE (spec Assumption explicit). Two Umbraco instances behind a load balancer hold independent caches.

---

## §10 — State store extension: `AnalyticsEventStateStore` (MODIFIED)

Slice-002's request-scoped backing store gains a parallel field for the session reference.

```csharp
internal sealed class AnalyticsEventStateStore
{
    private AnalyticsEventReceipt? _currentReceipt;
    private AnalyticsSession? _currentSession;     // NEW

    public AnalyticsEventReceipt? CurrentRequestReceipt => _currentReceipt;
    public AnalyticsSession? CurrentSession => _currentSession;   // NEW

    public void SetCurrentReceipt(AnalyticsEventReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _currentReceipt = receipt;
    }

    public void SetCurrentSession(AnalyticsSession session)        // NEW
    {
        ArgumentNullException.ThrowIfNull(session);
        _currentSession = session;
    }
}
```

The slice-002 handler's `TryUpdateInFlightStateStore(receipt)` becomes `TryUpdateInFlightStateStore(receipt, session)` (private helper inside `PageviewCapturedHandler`) — calls both setters when the request scope is reachable. Slice-002 swallow-disposed-scope semantics unchanged.

Concurrency: single writer (the handler runs once per notification); multiple readers (in-process consumers). The slice-002 Thread-Visibility-via-Scope-Construction-Barrier argument continues to hold.

---

## §11 — Cascade-step contract surface (already public on Customizer's side)

Analyzer's session cascade step implements Customizer's `IAnonymizationCascadeStep` directly; the interface itself is not re-declared. The step's external observable contract:

```csharp
namespace Analyzer.Features.Sessions.Application.Anonymization;

internal sealed class AnalyzerSessionCascadeStep : IAnonymizationCascadeStep
{
    // Signature (from Customizer):
    //   Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct);
    //
    // Semantics for analyzerSession (soft-anonymise):
    //   UPDATE analyzerSession
    //   SET anonymizedUtc = SYSUTCDATETIME(), deviceKey = ''
    //   WHERE visitorProfileKey = @visitorProfileKey
    //     AND anonymizedUtc IS NULL;
    //
    // Then invalidate cache entries for the visitor's affected sessions.
    //
    // Runs inside Customizer's outer NPoco scope; nested scope enlists
    // transparently. Throw rolls outer back unconditionally — cache
    // invalidation runs only AFTER successful repository return.
}
```

No new data model on Analyzer's side beyond the repository write — the cascade step is a thin orchestrator over `IAnalyzerSessionRepository.SoftAnonymizeByVisitorKeyAsync` + `AnalyzerSessionCacheStore.InvalidateByVisitorKey`.

---

## §12 — Constants (modification)

A new entry in `Analyzer.Constants.Database`:

```csharp
namespace Analyzer;

public static class Constants
{
    public static class Database
    {
        public const string AnalyzerEventReceipt = "analyzerEventReceipt";  // slice 002 — unchanged
        public const string AnalyzerSession = "analyzerSession";             // slice 003 — NEW
    }
}
```

The string literal `"analyzerSession"` is the only canonical occurrence; the DTO's `[TableName]`, the migration's `TableExists` guard, the cascade-step's UPDATE, the sweeper's UPDATE all reference the constant.

---

## §13 — State transitions

`analyzerSession` rows have four states in their lifecycle:

| State | Trigger | Effect |
|---|---|---|
| **Created (active)** | `AnalyzerSessionResolver.ResolveAsync` opens a session on cache-miss-no-row OR after the lazy-close branch closes a stale predecessor. | Row appears with `isActive = 1, endUtc = null, anonymizedUtc = null, pageviewCount = 1`. Partial unique index rejects concurrent duplicate-open attempts at the DB layer; resolver catches and re-reads. |
| **Extended** | Resolver attaches a new pageview to an existing active session within the inactivity window. | UPDATE: `lastActivityUtc` advances, `pageviewCount` increments by 1. State unchanged (`isActive = 1`, `endUtc = null`). |
| **Closed** | Either (a) US1 AS3 lazy-close runs after resolver observes a stale session, OR (b) US3 sweeper runs against eligible rows. | UPDATE: `isActive = 0, endUtc = lastActivityUtc + inactivityTimeout`. State terminal — no further updates expected except cascade-anonymise. |
| **Anonymised** | `AnalyzerSessionCascadeStep.ExecuteAsync` runs as part of `AnonymizeVisitorProfileCommand`. | UPDATE: `anonymizedUtc = now, deviceKey = ''`. May happen on active or closed rows (the cascade does NOT close active rows — leaves their `isActive` flag intact; the sweeper handles closure separately). |

There is no "deleted" transition. Sessions are soft-anonymised, never hard-deleted (Principle IV v1.1.1 per-table choice; spec Assumption #2).

The state-store's `AnalyticsSession` projection has a corresponding lifecycle:

| State | Trigger | Effect |
|---|---|---|
| **Unset (null)** | Default state of every request scope. | `IAnalyticsEventStateProvider.CurrentSession` returns `null`. |
| **Set** | Handler completes before the request scope ends AND successfully resolves the scope via `IHttpContextAccessor`. | `CurrentSession` returns the active session projection. Same caveats as slice-002's `CurrentRequestReceipt` — typically null on a pageview request itself; reliable on in-request dispatches at later slices. |

---

## §14 — Validation rules

| Source field | Validation | Action if violated |
|---|---|---|
| `Pageview.VisitorProfileKey` | Non-empty Guid | Slice-002 handler skips with warning log; resolver never reached. |
| `notification.HttpContext.Request.Headers.UserAgent` | Any string (including null/whitespace) | Hash to deterministic empty-string sentinel deviceKey; session resolution proceeds normally. |
| `Pageview.Key` | Non-empty Guid | Slice-002 handler skips with debug log; resolver never reached. |
| `AnalyzerSessionOptions.InactivityTimeoutMinutes` | Must be positive | Min-clamp to 1 minute inside resolver + sweeper; log warning at composition if configured ≤ 0. |
| `AnalyzerSessionOptions.SweepIntervalSeconds` | Must be positive | Min-clamp to 1 second; log warning at composition if configured ≤ 0. |
| `AnalyzerSessionOptions.SweepBatchSize` | Must be positive | Min-clamp to 1; log warning at composition if configured ≤ 0. |
| `AnalyzerSessionOptions.CacheCapacity` | Must be positive | Default to 10 000 if configured ≤ 0; log warning. |

The `ReceivedUtc` source value (the time the handler computed) is sourced from the injected `TimeProvider.System` per slice-002 precedent — testable via `FakeTimeProvider`.

---

## §15 — Open questions

None — all data-model decisions are bound. The remaining tunables (cache sliding-expiration multiplier, perf-smoke lazy-close coverage) are pinned in `/speckit-tasks`.
