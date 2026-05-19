# Data Model: Forms Tracking

**Slice**: 005-forms-tracking
**Phase**: 1 (Design)
**Date**: 2026-05-19

## §1 — Tables

### §1.1 `analyzerFormEvent` (NEW)

One row per lifecycle event (`Impression`, `Start`, `Success`, `Abandon`) per `(visitorKey, formKey, sessionKey)`.

| Column                       | Type            | Null | Notes |
|------------------------------|-----------------|------|-------|
| `id`                         | `uniqueidentifier` | NO   | PK (non-autoincrement; client-supplied Guid for idempotency precedent — matches slice 002's `AnalyzerEventReceiptDto`). |
| `eventKey`                   | `uniqueidentifier` | NO   | UX index `UX_analyzerFormEvent_eventKey`. Public-surface identity (`AnalyticsFormEvent.EventKey`). |
| `visitorProfileKey`          | `uniqueidentifier` | NO   | Hard FK → `customizerVisitorProfile(key)` (raw-SQL declaration per slice-002 precedent — Principle III: do not import `Customizer.Features.Visitors.Persistence.VisitorProfileDto`). |
| `sessionKey`                 | `uniqueidentifier` | YES  | Soft FK → `analyzerSession(sessionKey)` (NULL allowed: pre-sessions cohort + back-pressure-drop posture matches slice-002 receipt). IDX `IDX_analyzerFormEvent_sessionKey`. |
| `formKey`                    | `uniqueidentifier` | NO   | Umbraco Forms `Form.Id`. IDX `IDX_analyzerFormEvent_formKey`. |
| `contentKey`                 | `uniqueidentifier` | NO   | Umbraco content node hosting the form impression. Non-FK (tombstone tolerance per slice-002 precedent). |
| `eventType`                  | `tinyint`       | NO   | Maps to `AnalyzerFormEventType` enum: `0=Impression`, `1=Start`, `2=Success`, `3=Abandon`. Composite IDX `(visitorProfileKey, formKey, sessionKey, eventType)` — `IDX_analyzerFormEvent_lifecycle`. |
| `elapsedMsFromImpression`    | `int`           | YES  | Set on `Start` rows (ms since matching `Impression`); NULL on the other 3 types. |
| `elapsedMsFromStart`         | `int`           | YES  | Set on `Success` + `Abandon` rows (ms since matching `Start`); NULL on `Impression` + `Start`. |
| `receivedUtc`                | `datetimeoffset(7)` | NO   | IDX `IDX_analyzerFormEvent_receivedUtc` — supports time-range reports. |

**Constraints**:
- FK `FK_analyzerFormEvent_VisitorProfile` (raw SQL, like slice 002).
- CHECK `CK_analyzerFormEvent_eventType IN (0,1,2,3)`.
- CHECK `CK_analyzerFormEvent_elapsedMs_byEventType` — `elapsedMsFromImpression IS NOT NULL` only when `eventType=1`; `elapsedMsFromStart IS NOT NULL` only when `eventType IN (2,3)`. (Optional — research will decide whether to enforce at DB layer or only in handler.)

**Idempotency**: client-supplied `id` lets duplicate POSTs land safely (UX on `eventKey` rejects exact duplicates; `UniqueConstraintViolationDetector` from slice 003 distinguishes between the two PKs). Acceptable per slice-002 pattern.

### §1.2 `analyzerFormFieldEvent` (NEW)

One row per `FieldFocus` / `FieldUnfocus` per `(visitorKey, formKey, fieldKey, sessionKey)`.

| Column                       | Type            | Null | Notes |
|------------------------------|-----------------|------|-------|
| `id`                         | `uniqueidentifier` | NO   | PK. |
| `eventKey`                   | `uniqueidentifier` | NO   | UX index `UX_analyzerFormFieldEvent_eventKey`. |
| `visitorProfileKey`          | `uniqueidentifier` | NO   | Hard FK → `customizerVisitorProfile(key)`. |
| `sessionKey`                 | `uniqueidentifier` | YES  | Soft FK → `analyzerSession(sessionKey)`. IDX `IDX_analyzerFormFieldEvent_sessionKey`. |
| `formKey`                    | `uniqueidentifier` | NO   | (compound IDX below) |
| `fieldKey`                   | `uniqueidentifier` | NO   | Umbraco Forms `Field.Id`. Compound IDX `(formKey, fieldKey, eventType)` — `IDX_analyzerFormFieldEvent_perField`. |
| `eventType`                  | `tinyint`       | NO   | Maps to `AnalyzerFormFieldEventType` enum: `0=FieldFocus`, `1=FieldUnfocus`. |
| `hadValue`                   | `bit`           | YES  | Set on `FieldUnfocus` rows only; NULL on `FieldFocus`. CHECK `CK_analyzerFormFieldEvent_hadValue_byEventType`: `hadValue IS NOT NULL` only when `eventType=1`. |
| `receivedUtc`                | `datetimeoffset(7)` | NO   | IDX `IDX_analyzerFormFieldEvent_receivedUtc`. |

Composite cascade-DELETE index: `(visitorProfileKey, formKey, sessionKey)` — `IDX_analyzerFormFieldEvent_cascadeProbe`. Drives the 200ms cascade budget (SC-004).

**Privacy invariant**: there is no column intended to hold field content. `hadValue` is a single bit; field values are not transmitted from the client (handler-level validator rejects payloads carrying any property that looks like a value: name pattern `*Value`, `*Content`, `*Text` rejected at the validator boundary). Validation guarantees enforced both client-side (no field-value capture in `field-observer.ts`) AND server-side (model validation strips unknown properties, fail-closed).

## §2 — DTOs (NPoco)

### §2.1 `AnalyzerFormEventDto` — `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormEventDto.cs`

```csharp
[TableName(Constants.Database.AnalyzerFormEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerFormEventDto
{
    [Column("id")] public Guid Id { get; set; }
    [Column("eventKey")] [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerFormEvent_eventKey")] public Guid EventKey { get; set; }
    [Column("visitorProfileKey")] public Guid VisitorProfileKey { get; set; }
    [Column("sessionKey")] [NullSetting(NullSetting = NullSettings.Null)] [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_sessionKey")] public Guid? SessionKey { get; set; }
    [Column("formKey")] [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_formKey")] public Guid FormKey { get; set; }
    [Column("contentKey")] public Guid ContentKey { get; set; }
    [Column("eventType")] public byte EventType { get; set; }
    [Column("elapsedMsFromImpression")] [NullSetting(NullSetting = NullSettings.Null)] public int? ElapsedMsFromImpression { get; set; }
    [Column("elapsedMsFromStart")] [NullSetting(NullSetting = NullSettings.Null)] public int? ElapsedMsFromStart { get; set; }
    [Column("receivedUtc")] [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_receivedUtc")] public DateTimeOffset ReceivedUtc { get; set; }
}
```

Composite lifecycle index `IDX_analyzerFormEvent_lifecycle` over `(visitorProfileKey, formKey, sessionKey, eventType)` declared in the migration body (NPoco's `[Index]` attribute only supports single columns).

### §2.2 `AnalyzerFormFieldEventDto` — `src/Analyzer/Features/Forms/Infrastructure/Persistence/AnalyzerFormFieldEventDto.cs`

Analogous shape; two composite indexes (`IDX_analyzerFormFieldEvent_perField`, `IDX_analyzerFormFieldEvent_cascadeProbe`) declared in the migration body.

## §3 — Migrations

### §3.1 `M0004_AddAnalyzerFormEventTable`

```csharp
public sealed class M0004_AddAnalyzerFormEventTable : AsyncMigrationBase
{
    public M0004_AddAnalyzerFormEventTable(IMigrationContext context) : base(context) { }

    protected override Task MigrateAsync()
    {
        if (TableExists(Constants.Database.AnalyzerFormEvent) is false)
        {
            Create.Table<AnalyzerFormEventDto>().Do();

            var provider = Database.DatabaseType.GetProviderName();
            var isSqlite = provider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
            if (!isSqlite)
            {
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerFormEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerFormEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) REFERENCES [customizerVisitorProfile]([key])");

                Database.Execute(
                    $"CREATE INDEX [IDX_analyzerFormEvent_lifecycle] ON [{Constants.Database.AnalyzerFormEvent}] " +
                    $"([visitorProfileKey], [formKey], [sessionKey], [eventType])");
            }
        }
        return Task.CompletedTask;
    }
}
```

### §3.2 `M0005_AddAnalyzerFormFieldEventTable`

Analogous. Declares `IDX_analyzerFormFieldEvent_perField` over `(formKey, fieldKey, eventType)` and `IDX_analyzerFormFieldEvent_cascadeProbe` over `(visitorProfileKey, formKey, sessionKey)`.

Both migrations registered in `AnalyzerMigrationPlan` (slice 002's plan; chain `M0003 → M0004 → M0005`).

## §4 — Public records

### §4.1 `Analyzer.Analytics.AnalyticsFormEvent`

```csharp
public sealed record AnalyticsFormEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid ContentKey,
    AnalyzerFormEventType EventType,
    int? ElapsedMsFromImpression,
    int? ElapsedMsFromStart,
    DateTimeOffset ReceivedUtc);

public enum AnalyzerFormEventType : byte
{
    Impression = 0,
    Start = 1,
    Success = 2,
    Abandon = 3,
}
```

### §4.2 `Analyzer.Analytics.AnalyticsFormFieldEvent`

```csharp
public sealed record AnalyticsFormFieldEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid FieldKey,
    AnalyzerFormFieldEventType EventType,
    bool? HadValue,
    DateTimeOffset ReceivedUtc);

public enum AnalyzerFormFieldEventType : byte
{
    FieldFocus = 0,
    FieldUnfocus = 1,
}
```

### §4.3 `IAnalyticsEventStateProvider` (additive)

```csharp
public interface IAnalyticsEventStateProvider
{
    // existing members from slices 002/003/004 unchanged
    IReadOnlyList<AnalyticsFormEvent> CurrentRequestFormEvents { get; }
    IReadOnlyList<AnalyticsFormFieldEvent> CurrentRequestFormFieldEvents { get; }
}
```

Both return empty lists, never null.

## §5 — Commands (handler input records)

```csharp
public sealed record AnalyzerFormEventCapture(
    VisitorIdentity Actor,
    Guid FormKey,
    Guid ContentKey,
    AnalyzerFormEventType EventType,
    int? ElapsedMsFromImpression,
    int? ElapsedMsFromStart,
    string UserAgent,
    DateTimeOffset ReceivedUtc);

public sealed record AnalyzerFormFieldEventCapture(
    VisitorIdentity Actor,
    Guid FormKey,
    Guid FieldKey,
    AnalyzerFormFieldEventType EventType,
    bool? HadValue,
    string UserAgent,
    DateTimeOffset ReceivedUtc);
```

## §6 — Repositories

```csharp
internal interface IAnalyzerFormEventRepository
{
    Task<Guid> InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct);
    Task DeleteByVisitorKeyAsync(Guid visitorKey, CancellationToken ct);
    Task<IReadOnlyList<(Guid SessionKey, Guid FormKey)>> ListUnclosedStartsForSessionsAsync(IReadOnlyCollection<Guid> sessionKeys, CancellationToken ct);
    Task InsertAbandonsBulkAsync(IReadOnlyList<AnalyzerFormEventDto> abandons, CancellationToken ct);
}

internal interface IAnalyzerFormFieldEventRepository
{
    Task<Guid> InsertAsync(AnalyzerFormFieldEventDto dto, CancellationToken ct);
    Task DeleteByVisitorKeyAsync(Guid visitorKey, CancellationToken ct);
}
```

`ListUnclosedStartsForSessionsAsync` powers abandonment materialisation (R5). One query per sweeper batch.

## §7 — Constitution check re-evaluation post-design

| # | Principle | Re-check | Notes |
|---|-----------|----------|-------|
| I | EntraID-Only Identity | ✅ | Capture handlers require `Actor.IsAvailable=true` and `Actor.Key != Guid.Empty`. `AnalyzerVisitorIdField` writes `Guid.Empty` on misconfig (audited, not silent). |
| II | Spec-Grounded Scope | ✅ | No new FR references introduced in design. |
| III | Customizer Substrate, No Retrofit | ✅ | Raw-SQL FK on both tables; no import of `Customizer.Features.Visitors.Persistence.VisitorProfileDto`. `IVisitorIdentifier` is the only Customizer-owned surface read. |
| IV | Additive-Only Storage, Cascade-Step Anonymisation | ✅ | Both tables FK to `customizerVisitorProfile(key)`. Both register `IAnonymizationCascadeStep` (hard-delete). |
| V | Slice-Driven Delivery via Speckit | ✅ | Plan + research + data-model landed in `specs/005-forms-tracking/`. |
| VI | Software Engineering Excellence | ✅ | Vertical slice under `Features/Forms/`; unit + integration coverage planned in `tasks.md`. |
| VII | Security by Design | ✅ | Endpoint Principle-VII gate (auth + anti-forgery + validation + audit). Validator name-pattern rejection guards against field-value leakage. |
| VIII | Performance & Scalability First | ✅ | Fire-and-forget POSTs; sweeper batch query for abandonment; indexed cascade DELETE. |
| IX | Umbraco-Native & Operator-First | ✅ | Auto-attach client; Umbraco.Forms `FieldType` auto-discovery; no operator config required. |
| X | Extensibility by Design | ✅ | Additive `IAnalyticsEventStateProvider` members; new public records pinned via `PublicSurfacePinningTests`. |

**Verdict**: 10 / 10 PASS post-design. Plan is ready for `/speckit-tasks`.
