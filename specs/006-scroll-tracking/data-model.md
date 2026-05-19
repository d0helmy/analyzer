# Data Model: Scroll Tracking

**Slice**: 006-scroll-tracking
**Phase**: 1 (Design)
**Date**: 2026-05-19

## §1 — Table

### §1.1 `analyzerScrollSample` (NEW)

One row per accepted milestone crossing per `(visitorKey, pageviewKey, contentKey, bucket)` tuple. The `(pageviewKey, bucket)` pair is the database-enforced uniqueness key (FR-003); the `(visitorProfileKey)` index drives the SC-004 cascade-delete budget.

| Column                       | Type                | Null | Notes |
|------------------------------|---------------------|------|-------|
| `id`                         | `uniqueidentifier`  | NO   | PK (non-autoincrement; client-supplied Guid for retry idempotency — matches slice 002's `AnalyzerEventReceiptDto`). |
| `eventKey`                   | `uniqueidentifier`  | NO   | UX index `UX_analyzerScrollSample_eventKey`. Public-surface identity (`AnalyticsScrollSample.EventKey`). |
| `visitorProfileKey`          | `uniqueidentifier`  | NO   | Hard FK → `customizerVisitorProfile(key)` (raw-SQL declaration per slice-002 precedent — Principle III: do not import `Customizer.Features.Visitors.Persistence.VisitorProfileDto`). IDX `IDX_analyzerScrollSample_visitor` — drives the cascade-DELETE 200 ms / 1 000-row budget (SC-004). |
| `sessionKey`                 | `uniqueidentifier`  | YES  | Soft FK → `analyzerSession(sessionKey)`. NULL allowed (pre-sessions cohort + back-pressure-drop posture matches slice 002/005). |
| `pageviewKey`                | `uniqueidentifier`  | NO   | Soft FK → `customizerPageview(key)` (tombstone tolerance per slice-002 precedent — Customizer may anonymise the pageview row through its own cascade). Part of `UX_analyzerScrollSample_pageviewBucket`. |
| `contentKey`                 | `uniqueidentifier`  | NO   | Umbraco content node the visitor was on. Non-FK (tombstone tolerance — content nodes may be deleted between capture and read). |
| `bucket`                     | `tinyint`           | NO   | Maps to `AnalyzerScrollBucket` enum: `25=Quarter`, `50=Half`, `75=ThreeQuarters`, `100=Full`. Part of `UX_analyzerScrollSample_pageviewBucket`. |
| `receivedUtc`                | `datetimeoffset(7)` | NO   | IDX `IDX_analyzerScrollSample_receivedUtc` — supports time-range reports in the eventual read-side slice. |

**Constraints**:
- FK `FK_analyzerScrollSample_VisitorProfile` (raw SQL → `customizerVisitorProfile(key)`).
- CHECK `CK_analyzerScrollSample_bucket IN (25, 50, 75, 100)` — enforces enum values at the DB layer (defence in depth against a buggy handler bypassing model validation).
- **Idempotency**: UX `UX_analyzerScrollSample_pageviewBucket` on `(pageviewKey, bucket)` enforces "at most one row per `(pageview, bucket)`" — slice-003's `UniqueConstraintViolationDetector` discriminates this from generic SQL errors and maps to HTTP 409.

**Indexes** (locked):
- PK: `id` (Guid, non-autoincrement).
- UX: `UX_analyzerScrollSample_eventKey` on `eventKey`.
- UX: `UX_analyzerScrollSample_pageviewBucket` on `(pageviewKey, bucket)` — **idempotency invariant**.
- IX: `IDX_analyzerScrollSample_visitor` on `visitorProfileKey` — cascade-DELETE probe.
- IX: `IDX_analyzerScrollSample_receivedUtc` on `receivedUtc`.

## §2 — DTO (NPoco)

### §2.1 `AnalyzerScrollSampleDto` — `src/Analyzer/Features/Scroll/Infrastructure/Persistence/AnalyzerScrollSampleDto.cs`

```csharp
[TableName(Constants.Database.AnalyzerScrollSample)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerScrollSampleDto
{
    [Column("id")] public Guid Id { get; set; }

    [Column("eventKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerScrollSample_eventKey")]
    public Guid EventKey { get; set; }

    [Column("visitorProfileKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerScrollSample_visitor")]
    public Guid VisitorProfileKey { get; set; }

    [Column("sessionKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public Guid? SessionKey { get; set; }

    [Column("pageviewKey")]
    public Guid PageviewKey { get; set; }

    [Column("contentKey")]
    public Guid ContentKey { get; set; }

    [Column("bucket")]
    public byte Bucket { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerScrollSample_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
```

The composite UX `UX_analyzerScrollSample_pageviewBucket` over `(pageviewKey, bucket)` is declared in the migration body (NPoco's `[Index]` attribute only supports single columns).

## §3 — Migration

### §3.1 `M0006_AddAnalyzerScrollSampleTable` — `src/Analyzer/Migrations/M0006_AddAnalyzerScrollSampleTable.cs`

Idempotent via `TableExists` guard. SQL Server only; SQLite branch creates only the table (no FK / no extra indexes — matches slices 002/004/005 SQLite behaviour for in-memory unit tests).

SQL Server branch additionally:
- Declares FK to `customizerVisitorProfile(key)`.
- Declares CHECK constraint `CK_analyzerScrollSample_bucket IN (25, 50, 75, 100)`.
- Declares the composite UX `UX_analyzerScrollSample_pageviewBucket` on `(pageviewKey, bucket)` via raw SQL.

The migration is chained after `M0005` in `AnalyzerMigrationPlan` (slice-005's most recent step).

```text
M0006 sequence (SQL Server):
1. if !TableExists(analyzerScrollSample): Create.Table<AnalyzerScrollSampleDto>()
2. ALTER TABLE ADD CONSTRAINT FK_analyzerScrollSample_VisitorProfile FOREIGN KEY (visitorProfileKey) REFERENCES customizerVisitorProfile(key)
3. ALTER TABLE ADD CONSTRAINT CK_analyzerScrollSample_bucket CHECK (bucket IN (25, 50, 75, 100))
4. CREATE UNIQUE NONCLUSTERED INDEX UX_analyzerScrollSample_pageviewBucket ON analyzerScrollSample (pageviewKey, bucket)
```

## §4 — Public records

### §4.1 `AnalyzerScrollBucket` — `src/Analyzer/Analytics/AnalyzerScrollBucket.cs`

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Scroll-depth milestone bucket. Byte-backed for storage parity with
/// the database's <c>tinyint bucket</c> column.
/// </summary>
public enum AnalyzerScrollBucket : byte
{
    Quarter = 25,
    Half = 50,
    ThreeQuarters = 75,
    Full = 100,
}
```

### §4.2 `AnalyticsScrollSample` — `src/Analyzer/Analytics/AnalyticsScrollSample.cs`

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Public read-side record for one accepted scroll-milestone crossing.
/// Returned via <see cref="IAnalyticsEventStateProvider.CurrentRequestScrollEvents"/>
/// and through the eventual read-side reporting API.
/// </summary>
public sealed record AnalyticsScrollSample
{
    public required Guid EventKey { get; init; }
    public required Guid VisitorProfileKey { get; init; }
    public Guid? SessionKey { get; init; }
    public required Guid PageviewKey { get; init; }
    public required Guid ContentKey { get; init; }
    public required AnalyzerScrollBucket Bucket { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
}
```

### §4.3 `IAnalyticsEventStateProvider` additive member

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    // ... existing members from slices 002-005 ...

    /// <summary>
    /// Scroll-milestone events accepted during the current request.
    /// Empty when none captured; never null. Slice 006.
    /// </summary>
    IReadOnlyList<AnalyticsScrollSample> CurrentRequestScrollEvents { get; }
}
```

`AnalyticsEventStateStore` (the in-request backing store) gains a parallel list field + `AppendScrollEvent(AnalyticsScrollSample)` mutator invoked by the capture handler on a successful insert.

## §5 — Cascade-step contract

`AnalyzerScrollSampleCascadeStep` implements `IAnonymizationCascadeStep` (Customizer-pinned interface). Pattern:

```csharp
public sealed class AnalyzerScrollSampleCascadeStep : IAnonymizationCascadeStep
{
    public int Order => /* next available after slice-005 cascade steps */;

    public string Description => "Hard-deletes analyzerScrollSample rows for the anonymised visitor.";

    public async Task ExecuteAsync(
        AnonymizationContext context,
        CancellationToken cancellationToken)
    {
        // Uses the ambient outer NPoco scope from `context` — does NOT open a new scope.
        await _repository.DeleteByVisitorAsync(context.VisitorProfileKey, cancellationToken);
    }
}
```

The repo's `DeleteByVisitorAsync` issues `DELETE FROM analyzerScrollSample WHERE visitorProfileKey = @0` against the ambient scope's NPoco database. SC-004 budget: 1 000 rows in ≤ 200 ms via `IDX_analyzerScrollSample_visitor`.

Registration: `AnalyzerScrollComposer` invokes `builder.WithCollectionBuilder<AnonymizationCascadeStepCollectionBuilder>().Append<AnalyzerScrollSampleCascadeStep>()` (the orchestrator picks it up via DI scan — no Customizer source change).

## §6 — Constitution Check (post-design re-evaluation)

All 10 principles re-evaluated against the Phase 1 design surface above:

| # | Principle | Pre-design | Post-design | Notes |
|---|-----------|------------|-------------|-------|
| I | EntraID-Only Identity | ✅ | ✅ | Handler resolves identity via `IVisitorIdentifier`; no anonymous path. |
| II | Spec-Grounded Scope | ✅ | ✅ | DTO/DDL cite only `FR-COL-02`-pinned columns; no FR-DEP/DIM-03/DIM-04 leakage. |
| III | Customizer Substrate | ✅ | ✅ | All FKs declared via raw SQL; no `Customizer.*` DTO imports. Pageview-key read via the pinned `IAnalyticsStateProvider.CurrentRequest.PageviewKey`. |
| IV | Additive-Only Storage, Cascade-Step | ✅ | ✅ | New table + new cascade step (§5); hard-FK to `customizerVisitorProfile`; no row of existing tables modified. |
| V | Slice-Driven Delivery | ✅ | ✅ | All work specked + planned; no direct-to-main bypass. |
| VI | Software Engineering Excellence | ✅ | ✅ | Vertical-slice layout; unit + integration tests planned (see plan.md "Structure"). |
| VII | Security by Design | ✅ | ✅ | Four-corner gate at the controller; CHECK constraint on bucket adds DB-layer defence. |
| VIII | Performance & Scalability | ✅ | ✅ | Passive listener + rAF throttle; indexed cascade probe; no global locks. |
| IX | Umbraco-Native & Operator-First | ✅ | ✅ | Auto-attached client module; reuses operator-known `analyzer-no-tracking` attribute. |
| X | Extensibility by Design | ✅ | ✅ | One additive `IAnalyticsEventStateProvider` member; pinning baseline regenerated. |

**Post-design verdict: 10 / 10 PASS.** No new design choice introduced a violation. Plan-time Constitution Check stands.
