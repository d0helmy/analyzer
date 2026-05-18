# Phase 1 Data Model: Custom Events

**Slice**: 004 — custom events
**Date**: 2026-05-18
**Constitution**: v1.1.1
**Reference**: [`spec.md`](spec.md) FR-003/005/009/011; [`plan.md`](plan.md) §Storage; [`research.md`](research.md) §3, §4, §7.

This document fixes the concrete shape of every persisted and in-memory entity slice 004 introduces or modifies.

---

## §1 — Persisted entity: `analyzerCustomEvent` (NEW)

One row per `analyzer.send()` call successfully processed by the management endpoint. Third Analyzer-owned table (after `analyzerEventReceipt` + `analyzerSession`).

### Columns

| Column | Type (SQL Server) | NPoco type | Null | Default | Purpose |
|---|---|---|---|---|---|
| `id` | `uniqueidentifier` | `Guid` | NOT NULL | set by app | Opaque PK. |
| `eventKey` | `uniqueidentifier` | `Guid` | NOT NULL | set by app | Publicly-exposed stable identifier (returned by HTTP 202). Unique. |
| `sessionKey` | `uniqueidentifier` | `Guid` | NOT NULL | — | Hard FK to `analyzerSession.sessionKey`. First Analyzer-to-Analyzer hard FK. |
| `visitorProfileKey` | `uniqueidentifier` | `Guid` | NOT NULL | — | Hard FK to `customizerVisitorProfile.key`. |
| `receiptKey` | `uniqueidentifier` | `Guid?` | NULL | NULL | Soft FK to `analyzerEventReceipt.id`. Populated only on the rare in-request co-capture case. |
| `category` | `nvarchar(64)` | `string` | NOT NULL | — | Operator-defined. Validated `1..64` chars, non-whitespace-only. |
| `action` | `nvarchar(64)` | `string` | NOT NULL | — | Operator-defined. Validated `1..64` chars, non-whitespace-only. |
| `label` | `nvarchar(256)` | `string?` | NULL | NULL | Operator-defined. Validated `<=256` chars when present. |
| `value` | `decimal(18,4)` | `decimal?` | NULL | NULL | Operator-defined numeric. Validated non-NaN / non-Infinity (decimal type itself doesn't have NaN/Infinity; JSON deserialiser rejects). |
| `receivedUtc` | `datetimeoffset(7)` | `DateTimeOffset` | NOT NULL | — | When the endpoint observed the request. Sourced from injected `TimeProvider`. |

### Constraints

- **Primary key**: `PK_analyzerCustomEvent (id)`.
- **Unique index**: `UX_analyzerCustomEvent_eventKey (eventKey)`.
- **Foreign key** (SQL Server; SQLite skip): `FK_analyzerCustomEvent_VisitorProfile (visitorProfileKey) REFERENCES customizerVisitorProfile(key)`.
- **Foreign key** (SQL Server; SQLite skip): `FK_analyzerCustomEvent_Session (sessionKey) REFERENCES analyzerSession(sessionKey)`. First Analyzer-to-Analyzer hard FK.
- **No FK on receiptKey** — soft pointer per spec FR-003.
- **Non-clustered index**: `IDX_analyzerCustomEvent_sessionKey (sessionKey)` — session-scoped reports (slice 010+).
- **Non-clustered index**: `IDX_analyzerCustomEvent_visitorProfileKey (visitorProfileKey)` — cascade-step's `DELETE … WHERE visitorProfileKey = @key` (SC-004).
- **Non-clustered index**: `IDX_analyzerCustomEvent_receiptKey (receiptKey)` — for the rare receipt-correlation read; small index (mostly null).
- **Non-clustered composite index**: `IDX_analyzerCustomEvent_category_action (category, action)` — slice-010+ "events by category" aggregation.

### NPoco DTO shape (`Analyzer.Features.CustomEvents.Infrastructure.Persistence.AnalyzerCustomEventDto`)

```csharp
[TableName(Constants.Database.AnalyzerCustomEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerCustomEventDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("eventKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerCustomEvent_eventKey")]
    public Guid EventKey { get; set; }

    [Column("sessionKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerCustomEvent_sessionKey")]
    public Guid SessionKey { get; set; }

    [Column("visitorProfileKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerCustomEvent_visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("receiptKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerCustomEvent_receiptKey")]
    public Guid? ReceiptKey { get; set; }

    [Column("category")]
    [Length(64)]
    public string Category { get; set; } = string.Empty;

    [Column("action")]
    [Length(64)]
    public string Action { get; set; } = string.Empty;

    [Column("label")]
    [Length(256)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public string? Label { get; set; }

    [Column("value")]
    [Length(0)]  // decimal precision via Precision attribute below
    [Decimal(18, 4)]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? Value { get; set; }

    [Column("receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
```

The composite `(category, action)` index is declared in the migration body via raw SQL (NPoco's `[Index]` attribute is single-column; matches slice-003 partial-unique-index precedent).

### Estimated volume

Custom events are operator-controlled (page scripts decide when to fire). Realistic volume: 10–100 events per active visitor per day (clicks, video-play, search-submit, etc.) → 100k–1M rows/day at ~10k daily active visitors. Pruning beyond 90 days (or whatever retention policy lands later) is out of scope here.

---

## §2 — Public record: `AnalyticsCustomEvent` (NEW)

Consumer-facing immutable projection. Lives in `Analyzer.Analytics` (pinned namespace).

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// One operator-defined engagement event recorded by a client-side
/// `analyzer.send(...)` call. The consumer-facing projection of an
/// `analyzerCustomEvent` row, surfaced through
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestCustomEvents"/>
/// for in-process consumers within the same request scope.
/// </summary>
/// <remarks>
/// Public + pinned. Breaking changes are PROHIBITED outside a MAJOR
/// release (Constitution Principle X).
/// </remarks>
/// <param name="EventKey">Publicly-exposed stable identifier; matches the DB row's eventKey.</param>
/// <param name="SessionKey">Hard FK to <see cref="AnalyticsSession.SessionKey"/>.</param>
/// <param name="VisitorProfileKey">Hard FK to <c>customizerVisitorProfile.Key</c>.</param>
/// <param name="ReceiptKey">
/// Soft FK to <see cref="AnalyticsEventReceipt.Id"/>. Null in 99% of real flows
/// (page-script POST is a separate HTTP request from the page render);
/// populated only in the rare in-request co-capture case.
/// </param>
/// <param name="Category">Operator-defined. 1..64 chars, non-whitespace-only.</param>
/// <param name="Action">Operator-defined. 1..64 chars, non-whitespace-only.</param>
/// <param name="Label">Operator-defined. Up to 256 chars when present.</param>
/// <param name="Value">Operator-defined numeric. Up to decimal(18,4) precision.</param>
/// <param name="ReceivedUtc">When the endpoint observed the request.</param>
public sealed record AnalyticsCustomEvent(
    Guid EventKey,
    Guid SessionKey,
    Guid VisitorProfileKey,
    Guid? ReceiptKey,
    string Category,
    string Action,
    string? Label,
    decimal? Value,
    DateTimeOffset ReceivedUtc);
```

Pinned via existing `PublicSurfacePinningTests`; baseline regen captures it. 9 properties; matches the FR-011 shape verbatim.

---

## §3 — `IAnalyticsEventStateProvider` extension (modified — additive)

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }       // slice 002 (unchanged)
    AnalyticsSession? CurrentSession { get; }                    // slice 003 (unchanged)
    IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }  // slice 004 — NEW
}
```

`CurrentRequestCustomEvents` returns an empty `IReadOnlyList<T>` when no custom events have been captured this request — never null (FR-005). Within a single request scope the list grows as page scripts make multiple `analyzer.send(...)` calls (US1 AS5).

---

## §4 — `AnalyticsEventStateStore` extension (modified — additive)

```csharp
internal sealed class AnalyticsEventStateStore
{
    private AnalyticsEventReceipt? _currentReceipt;      // slice 002
    private AnalyticsSession? _currentSession;           // slice 003
    private readonly List<AnalyticsCustomEvent> _currentCustomEvents = new();  // slice 004

    public AnalyticsEventReceipt? CurrentRequestReceipt => _currentReceipt;
    public AnalyticsSession? CurrentSession => _currentSession;
    public IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents =>
        _currentCustomEvents.AsReadOnly();

    public void SetCurrentReceipt(AnalyticsEventReceipt receipt) { … }     // slice 002 (unchanged)
    public void SetCurrentSession(AnalyticsSession session) { … }           // slice 003 (unchanged)

    public void AppendCustomEvent(AnalyticsCustomEvent customEvent)         // slice 004 — NEW
    {
        ArgumentNullException.ThrowIfNull(customEvent);
        _currentCustomEvents.Add(customEvent);
    }
}
```

Concurrency: scoped per request; the controller runs on a single thread (ASP.NET request thread). Multi-thread mutation within a single scope isn't a real scenario for slice 004.

---

## §5 — Configuration

No new `IOptions<T>`-bound configuration for slice 004. The existing `AnalyzerSessionOptions` covers session-side tunables (inactivity timeout, sweep interval, etc.). Slice 004 doesn't introduce throttling, batch size, or rate-limit knobs (per spec Assumption "No throttling/rate-limiting at this layer").

---

## §6 — Request DTO: `CustomEventPayload`

Inbound JSON shape:

```json
{
  "category": "engagement",
  "action": "click",
  "label": "header-cta",
  "value": 42.5
}
```

C# DTO with DataAnnotations validation (per `research.md` §6):

```csharp
namespace Analyzer.Features.CustomEvents.Web;

public sealed class CustomEventPayload
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Category { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    [StringLength(64, MinimumLength = 1)]
    public string Action { get; init; } = string.Empty;

    [StringLength(256)]
    public string? Label { get; init; }

    public decimal? Value { get; init; }
}
```

Additional manual checks in the action body: `Category.Trim()` and `Action.Trim()` non-empty (DataAnnotations doesn't reject pure-whitespace strings).

---

## §7 — Response DTO: `CustomEventResponse`

Outbound JSON shape (HTTP 202 body):

```json
{ "eventKey": "1a2b3c4d-..." }
```

C#:

```csharp
public sealed class CustomEventResponse
{
    public Guid EventKey { get; init; }
}
```

Aligns with Clarification §2's `Promise<{ eventKey: string }>` JS-side return shape.

---

## §8 — In-process command DTO: `CustomEventCapture`

Internal command passed from controller to handler — keeps the controller thin (parse request, build command, return ActionResult; the handler owns the resolver + repository + state-store + audit orchestration).

```csharp
namespace Analyzer.Features.CustomEvents.Application;

internal sealed record CustomEventCapture(
    VisitorIdentity Actor,
    string Category,
    string Action,
    string? Label,
    decimal? Value,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
```

`VisitorIdentity` comes from slice-001 `IVisitorIdentifier.GetCurrent()`; carries `(Oid, Upn, Key)` per slice-001 contract. The controller resolves identity once + builds the command.

---

## §9 — Migration: `M0003_AddAnalyzerCustomEventTable` (NEW)

```csharp
public sealed class M0003_AddAnalyzerCustomEventTable : AsyncMigrationBase
{
    public M0003_AddAnalyzerCustomEventTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        var isSqlite = Database.DatabaseType.GetProviderName()?
            .Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerCustomEvent) is false)
        {
            Create.Table<AnalyzerCustomEventDto>().Do();

            if (!isSqlite)
            {
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerCustomEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerCustomEvent_Session] " +
                    $"FOREIGN KEY ([sessionKey]) " +
                    $"REFERENCES [{Constants.Database.AnalyzerSession}]([sessionKey])");

                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerCustomEvent_category_action] " +
                    $"ON [{Constants.Database.AnalyzerCustomEvent}] ([category], [action])");
            }
        }

        return Task.CompletedTask;
    }
}
```

Plan chain in `AnalyzerMigrationPlan`:

```csharp
.To<M0001_AddAnalyzerEventReceiptTable>("0001-AddAnalyzerEventReceiptTable")
.To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey")
.To<M0003_AddAnalyzerCustomEventTable>("0003-AddAnalyzerCustomEventTable");
```

Idempotent + SQLite-skip per slice-002/003 precedent.

---

## §10 — Repository contracts

### `IAnalyzerCustomEventRepository` (NEW)

```csharp
namespace Analyzer.Features.CustomEvents.Infrastructure.Persistence;

internal interface IAnalyzerCustomEventRepository
{
    /// <summary>
    /// Insert one custom-event row. Throws on FK constraint violation
    /// (session no longer active — sweeper closed it between resolver
    /// + insert; rare).
    /// </summary>
    Task InsertAsync(AnalyzerCustomEventDto dto, CancellationToken ct);

    /// <summary>
    /// DELETE all rows for <paramref name="visitorProfileKey"/>. Used
    /// by the cascade step inside Customizer's outer NPoco scope.
    /// </summary>
    Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct);
}
```

Implementation: NPoco-backed; nested `IScopeProvider.CreateScope()` per call; matches slice-002 receipt-repo pattern.

### `IAnalyzerSessionRepository` extension (modified — additive)

```csharp
internal interface IAnalyzerSessionRepository
{
    // ... existing slice-003 members ...

    /// <summary>
    /// Slice 004 — advance lastActivityUtc on the session WITHOUT
    /// incrementing pageviewCount. Used by the custom-event capture
    /// path (Clarification §1). 1 indexed UPDATE.
    /// </summary>
    Task TouchAsync(
        Guid sessionKey,
        DateTimeOffset newLastActivityUtc,
        CancellationToken ct);
}
```

### `IAnalyzerSessionResolver` extension (modified — additive parameter)

```csharp
internal enum SessionActivityKind
{
    Pageview,      // slice 002 / 003 — handler calls Extend (increments pageviewCount)
    CustomEvent,   // slice 004 — controller calls Touch (no pageviewCount change)
}

internal interface IAnalyzerSessionResolver
{
    ValueTask<SessionResolutionResult> ResolveAsync(
        Guid visitorProfileKey,
        string? userAgent,
        DateTimeOffset receivedUtc,
        SessionActivityKind activityKind,    // NEW
        CancellationToken ct);
}
```

Slice-003 `PageviewCapturedHandler` call sites update to pass `SessionActivityKind.Pageview` (compile-time change; no behavior change).

---

## §11 — Constants (modification)

```csharp
public static class Constants
{
    public static class Database
    {
        public const string AnalyzerEventReceipt = "analyzerEventReceipt";  // slice 002
        public const string AnalyzerSession = "analyzerSession";             // slice 003
        public const string AnalyzerCustomEvent = "analyzerCustomEvent";    // slice 004 — NEW
    }

    public static class AuditLog                                              // NEW namespace
    {
        public const string CustomEventCapture = "custom-event-capture";
    }
}
```

---

## §12 — State transitions

`analyzerCustomEvent` rows have two states:

| State | Trigger | Effect |
|---|---|---|
| **Created** | controller's `Capture` action succeeds | Row inserted; visible to operator-side SQL queries. |
| **Deleted** | cascade step runs for the visitor | Row hard-deleted. No tombstone. |

No "updated" transition — custom events are append-only-then-deleted (slice-002 receipt pattern).

The state-store's `_currentCustomEvents` list has its own lifecycle:

| State | Trigger | Effect |
|---|---|---|
| **Empty** | Scope start | `CurrentRequestCustomEvents` returns `[]`. |
| **Populated** | `AppendCustomEvent(...)` invoked by the handler within the same scope | List grows by one. |
| **Disposed** | Scope ends | List GC'd with the scope. |

---

## §13 — Validation rules

| Source field | Validation | Action if violated |
|---|---|---|
| `Category` | Required, length 1..64, non-whitespace-only | HTTP 400 with ProblemDetails naming the field. |
| `Action` | Required, length 1..64, non-whitespace-only | HTTP 400 with ProblemDetails naming the field. |
| `Label` | Optional; length <= 256 when present | HTTP 400 if oversized. |
| `Value` | Optional; decimal precision <= (18,4) | HTTP 400 if exceeded. JSON deserialiser rejects NaN/Infinity. |
| Anti-forgery token | Required (cookie + header pair) | HTTP 400 / 403 per Umbraco convention. |
| Authentication | Required (Umbraco backoffice session) | HTTP 401. |
| `VisitorProfileKey` (post-auth resolution) | Non-empty Guid | Reject with HTTP 401 (defensive — should not happen for an authenticated request). |

---

## §14 — Open questions

None — all data-model decisions are bound. Implementation-level open items (exact route prefix, exact JSON deserialiser config, exact Umbraco `[Authorize]` policy name) are listed in `research.md` §12 for `/speckit-tasks` to pin.
