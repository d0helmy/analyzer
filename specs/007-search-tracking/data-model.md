# Data Model: Internal Search-Tracking Capture

**Slice**: 007-search-tracking
**Phase**: 1 (Design)
**Date**: 2026-05-19

## §1 — Table

### §1.1 `analyzerSearchEvent` (NEW)

One row per accepted intranet search submission. There is **no uniqueness invariant** on `(pageviewKey, normalisedQuery)` — re-running the same query is a distinct engagement signal (Spec Edge Case "Concurrent same-query submissions").

| Column                       | Type                | Null | Notes |
|------------------------------|---------------------|------|-------|
| `id`                         | `uniqueidentifier`  | NO   | PK (non-autoincrement; client-supplied Guid is **not** used here — server generates per row, matches slice 005's `analyzerFormEvent` precedent). |
| `eventKey`                   | `uniqueidentifier`  | NO   | UX index `UX_analyzerSearchEvent_eventKey`. Public-surface identity (`AnalyticsSearchEvent.EventKey`). Returned in the response body. |
| `visitorProfileKey`          | `uniqueidentifier`  | NO   | Hard FK → `customizerVisitorProfile(key)` (raw-SQL declaration per slice-002 precedent — Principle III: do not import `Customizer.Features.Visitors.Persistence.VisitorProfileDto`). IDX `IDX_analyzerSearchEvent_visitor` — drives the cascade-DELETE 200 ms / 1 000-row budget (SC-004). |
| `sessionKey`                 | `uniqueidentifier`  | NO   | Hard FK → `analyzerSession(sessionKey)`. NOT NULL (search events resolve a session synchronously per slice-003 contract; the back-pressure-drop posture that allows NULL `sessionKey` in slice-002/005/006 does not apply here — see R7). |
| `pageviewKey`                | `uniqueidentifier`  | NO   | Soft FK → `customizerPageview(key)` (tombstone tolerance per slice-002/006 precedent). Visitor-bound at the controller layer (R3); not a DB-level constraint. |
| `contentKey`                 | `uniqueidentifier`  | NO   | Umbraco content node the visitor was on (denormalised from `customizerPageview` at write time). Non-FK (tombstone tolerance — content nodes may be deleted between capture and read). |
| `rawQuery`                   | `nvarchar(256)`     | NO   | Pre-normalisation user-typed string. PII-sensitive per FR-SRC-04 — never logged (FR-009 + SC-006); only retrievable via the role-gated DB row. |
| `normalisedQuery`            | `nvarchar(256)`     | NO   | Output of `IAnalyzerSearchQueryNormaliser` at capture time. The grouping key for "top queries" aggregations. IDX `IDX_analyzerSearchEvent_normalisedQuery` powers the aggregation. |
| `resultCount`                | `int`               | NO   | Non-negative integer. `0` is the "no-results" derived view (Spec Clarifications §1). CHECK `>= 0`. |
| `receivedUtc`                | `datetimeoffset(7)` | NO   | IDX `IDX_analyzerSearchEvent_receivedUtc` — supports time-range reports in the eventual read-side slice. |

**Constraints**:
- FK `FK_analyzerSearchEvent_VisitorProfile` (raw SQL → `customizerVisitorProfile(key)`).
- FK `FK_analyzerSearchEvent_Session` (raw SQL → `analyzerSession(sessionKey)`).
- CHECK `CK_analyzerSearchEvent_resultCount CHECK (resultCount >= 0)` — defence in depth against a buggy handler bypassing model validation.
- CHECK `CK_analyzerSearchEvent_rawQueryLength CHECK (LEN(rawQuery) BETWEEN 1 AND 256)` — defence in depth against a buggy handler bypassing the controller validation.
- CHECK `CK_analyzerSearchEvent_normalisedQueryLength CHECK (LEN(normalisedQuery) BETWEEN 1 AND 256)` — defence in depth; the default normaliser cannot produce an empty output for a valid input, but a custom normaliser could; the row-write should fail rather than persist a useless row.
- **No idempotency unique index** — by design (R7).

**Indexes** (locked):
- PK: `id` (Guid, non-autoincrement).
- UX: `UX_analyzerSearchEvent_eventKey` on `eventKey`.
- IX: `IDX_analyzerSearchEvent_visitor` on `visitorProfileKey` — cascade-DELETE probe.
- IX: `IDX_analyzerSearchEvent_normalisedQuery` on `normalisedQuery` — top-queries aggregation.
- IX: `IDX_analyzerSearchEvent_pageview` on `pageviewKey` — per-pageview lookup; supports the eventual click-through join (deferred to the read-side slice that lights up `FR-SRC-03`).
- IX: `IDX_analyzerSearchEvent_receivedUtc` on `receivedUtc`.

## §2 — DTO (NPoco)

### §2.1 `AnalyzerSearchEventDto` — `src/Analyzer/Features/Search/Infrastructure/Persistence/AnalyzerSearchEventDto.cs`

```csharp
[TableName(Constants.Database.AnalyzerSearchEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerSearchEventDto
{
    [Column("id")] public Guid Id { get; set; }

    [Column("eventKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerSearchEvent_eventKey")]
    public Guid EventKey { get; set; }

    [Column("visitorProfileKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerSearchEvent_visitor")]
    public Guid VisitorProfileKey { get; set; }

    [Column("sessionKey")]
    public Guid SessionKey { get; set; }

    [Column("pageviewKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerSearchEvent_pageview")]
    public Guid PageviewKey { get; set; }

    [Column("contentKey")]
    public Guid ContentKey { get; set; }

    [Column("rawQuery")]
    [Length(256)]
    public string RawQuery { get; set; } = string.Empty;

    [Column("normalisedQuery")]
    [Length(256)]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerSearchEvent_normalisedQuery")]
    public string NormalisedQuery { get; set; } = string.Empty;

    [Column("resultCount")]
    public int ResultCount { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerSearchEvent_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
```

## §3 — Migration

### §3.1 `M0007_AddAnalyzerSearchEventTable` — `src/Analyzer/Migrations/M0007_AddAnalyzerSearchEventTable.cs`

Idempotent via `TableExists` guard. SQL Server only; SQLite branch creates only the table (no FK / no extra indexes — matches slices 002/004/005/006 SQLite behaviour for in-memory unit tests).

SQL Server branch additionally:
- Declares FK to `customizerVisitorProfile(key)`.
- Declares FK to `analyzerSession(sessionKey)`.
- Declares CHECK constraints `CK_analyzerSearchEvent_resultCount`, `CK_analyzerSearchEvent_rawQueryLength`, `CK_analyzerSearchEvent_normalisedQueryLength`.

The migration is chained after `M0006` in `AnalyzerMigrationPlan` (slice-006's most recent step).

```text
M0007 sequence (SQL Server):
1. if !TableExists(analyzerSearchEvent): Create.Table<AnalyzerSearchEventDto>()
2. ALTER TABLE ADD CONSTRAINT FK_analyzerSearchEvent_VisitorProfile FOREIGN KEY (visitorProfileKey) REFERENCES customizerVisitorProfile(key)
3. ALTER TABLE ADD CONSTRAINT FK_analyzerSearchEvent_Session FOREIGN KEY (sessionKey) REFERENCES analyzerSession(sessionKey)
4. ALTER TABLE ADD CONSTRAINT CK_analyzerSearchEvent_resultCount CHECK (resultCount >= 0)
5. ALTER TABLE ADD CONSTRAINT CK_analyzerSearchEvent_rawQueryLength CHECK (LEN(rawQuery) BETWEEN 1 AND 256)
6. ALTER TABLE ADD CONSTRAINT CK_analyzerSearchEvent_normalisedQueryLength CHECK (LEN(normalisedQuery) BETWEEN 1 AND 256)
```

(NPoco's `[Index]` attributes handle the four single-column indexes declared on the DTO; only the FKs + CHECK constraints need the raw-SQL block.)

## §4 — Public records

### §4.1 `AnalyticsSearchEvent` — `src/Analyzer/Analytics/AnalyticsSearchEvent.cs`

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Public read-side record for one accepted intranet-search submission.
/// Returned via <see cref="IAnalyticsEventStateProvider.CurrentRequestSearchEvents"/>
/// and through the eventual read-side reporting API.
/// </summary>
/// <remarks>
/// <para><b>PII notice (FR-SRC-04)</b>: <see cref="RawQuery"/> and
/// <see cref="NormalisedQuery"/> are potentially personal data (e.g. names
/// of colleagues searched for). Read-side surfaces exposing these fields
/// MUST be role-gated per NFR-SEC-05.</para>
/// </remarks>
public sealed record AnalyticsSearchEvent
{
    public required Guid EventKey { get; init; }
    public required Guid VisitorProfileKey { get; init; }
    public required Guid SessionKey { get; init; }
    public required Guid PageviewKey { get; init; }
    public required Guid ContentKey { get; init; }
    public required string RawQuery { get; init; }
    public required string NormalisedQuery { get; init; }
    public required int ResultCount { get; init; }
    public required DateTimeOffset ReceivedUtc { get; init; }
}
```

### §4.2 `IAnalyzerSearchQueryNormaliser` — `src/Analyzer/Analytics/IAnalyzerSearchQueryNormaliser.cs`

```csharp
namespace Analyzer.Analytics;

/// <summary>
/// Converts a raw user-typed search query into a canonical form used as
/// the grouping key for "top queries" aggregation reports.
/// </summary>
/// <remarks>
/// <para>The default implementation
/// (<c>Analyzer.Features.Search.Application.Normalisation.DefaultAnalyzerSearchQueryNormaliser</c>)
/// applies in order: <c>Trim</c> → <c>NFKC</c> →
/// <c>ToLower(CultureInfo.InvariantCulture)</c> → internal-whitespace-run
/// collapse to a single space character.</para>
/// <para>Hosts with multilingual search MAY replace the default via a
/// single composer registration; per Umbraco DI conventions, the last
/// <c>AddScoped&lt;IAnalyzerSearchQueryNormaliser, ...&gt;</c> call wins.</para>
/// <para>Implementations MUST be culture-stable across hosts (no
/// <c>CurrentCulture</c> dependency) and MUST produce a non-empty output
/// for any non-empty trimmed input; an empty output is treated as a
/// validation failure at the capture endpoint.</para>
/// <para>Registered as <c>Scoped</c> (per-request lifetime; matches
/// slice-001's <c>IVisitorIdentifier</c> convention).</para>
/// </remarks>
public interface IAnalyzerSearchQueryNormaliser
{
    /// <summary>
    /// Compute the canonical grouping key for <paramref name="rawQuery"/>.
    /// </summary>
    /// <param name="rawQuery">The user-typed query, post-trim of outer whitespace.</param>
    /// <returns>The canonical key. MUST be non-empty for non-empty input.</returns>
    string Normalise(string rawQuery);
}
```

### §4.3 `IAnalyticsEventStateProvider` additive member

```csharp
namespace Analyzer.Analytics;

public interface IAnalyticsEventStateProvider
{
    // ... existing members from slices 002-006 ...

    /// <summary>
    /// Search events accepted during the current request. Empty when none
    /// captured; never null. Slice 007.
    /// </summary>
    IReadOnlyList<AnalyticsSearchEvent> CurrentRequestSearchEvents { get; }
}
```

`AnalyticsEventStateStore` (the in-request backing store) gains a parallel list field + `AppendSearchEvent(AnalyticsSearchEvent)` mutator invoked by the capture handler on a successful insert.

## §5 — Capture command

`AnalyzerSearchEventCapture` (Application layer command record) — `src/Analyzer/Features/Search/Domain/AnalyzerSearchEventCapture.cs`:

```csharp
internal sealed record AnalyzerSearchEventCapture(
    VisitorIdentity Actor,
    Guid SessionKey,
    Guid PageviewKey,
    Guid ContentKey,
    string RawQuery,
    int ResultCount,
    DateTimeOffset ReceivedUtc);
```

`VisitorIdentity` is the existing slice-002 identity-projection type returned by `IVisitorIdentifier.Resolve()` (carries `Key`, `Upn`, `Oid`, `IsAvailable`). Mirrors slice-006's `AnalyzerScrollEventCapture` shape — the `Actor` field carries the resolved identity into the domain layer so the handler can run the identity gate (step 2 below) and the auditor can log `ActorUpn` + `ActorOid` without a second identity round-trip.

The handler:
1. Receives the command.
2. **Identity gate**: throws `UnauthorizedAccessException` if `command.Actor.IsAvailable == false` OR `command.Actor.Key == Guid.Empty`. Controller maps to 401/403 (slice-006 precedent — `AnalyzerScrollEventCaptureHandler` does the same).
3. Invokes `IAnalyzerSearchQueryNormaliser.Normalise(command.RawQuery)`; rejects with `AnalyzerSearchPayloadValidationException` if the result is empty (defence in depth — controller-layer validation already enforces this, but the domain MUST NOT trust upstream validation per Principle VII).
4. Visitor-bound pageview check: rejects with `AnalyzerSearchPayloadValidationException` if `repo.ResolvePageviewVisitorBindingAsync(command.PageviewKey)` does not return `command.Actor.Key` (per research §R3).
5. Issues `IAnalyzerSessionRepository.TouchAsync(command.SessionKey, command.ReceivedUtc)` (no `pageviewCount` increment).
6. Issues `IAnalyzerSearchEventRepository.InsertAsync(new AnalyzerSearchEventDto { VisitorProfileKey = command.Actor.Key, ... })`.
7. Invokes `AppendSearchEvent(...)` on the in-request state store.
8. Invokes `AnalyzerSearchEventAuditor.AuditCaptureAsync(command.Actor.Upn, command.Actor.Oid, eventKey, command.PageviewKey, command.ResultCount, command.ReceivedUtc)` (logs `EventKey`, `PageviewKey`, `ResultCount`, `ActorUpn`, `ActorOid`, `ReceivedUtc` — never queries; see R6).
9. Returns the inserted `AnalyticsSearchEvent` to the controller for the response body.

## §6 — Cascade-step contract

`AnalyzerSearchEventCascadeStep` implements `IAnonymizationCascadeStep` (Customizer-pinned interface). Pattern:

```csharp
public sealed class AnalyzerSearchEventCascadeStep : IAnonymizationCascadeStep
{
    public int Order => /* next available after slice-006 cascade step */;

    public string Description => "Hard-deletes analyzerSearchEvent rows for the anonymised visitor (PII per FR-SRC-04).";

    public async Task ExecuteAsync(
        AnonymizationContext context,
        CancellationToken cancellationToken)
    {
        // Uses the ambient outer NPoco scope from `context` — does NOT open a new scope.
        await _repository.DeleteByVisitorAsync(context.VisitorProfileKey, cancellationToken);
    }
}
```

The repo's `DeleteByVisitorAsync` issues `DELETE FROM analyzerSearchEvent WHERE visitorProfileKey = @0` against the ambient scope's NPoco database. SC-004 budget: 1 000 rows in ≤ 200 ms via `IDX_analyzerSearchEvent_visitor`.

Registration: `AnalyzerSearchComposer` invokes `builder.WithCollectionBuilder<AnonymizationCascadeStepCollectionBuilder>().Append<AnalyzerSearchEventCascadeStep>()` (the orchestrator picks it up via DI scan — no Customizer source change).

**Cascade-disposition divergence from contract D8**: contract D8 lists this table with disposition "re-key"; this slice ships hard-delete. The divergence is intentional, documented in Spec Clarifications §2, and flagged in the PR description for a contract D8 amendment after this slice merges. Principle IV v1.1.1's participation-pattern menu (delete / soft-delete / re-projection) explicitly authorises per-table choice.

## §7 — Constitution Check (post-design re-evaluation)

All 10 principles re-evaluated against the Phase 1 design surface above:

| # | Principle | Pre-design | Post-design | Notes |
|---|-----------|------------|-------------|-------|
| I | EntraID-Only Identity | ✅ | ✅ | Handler resolves identity via `IVisitorIdentifier`; no anonymous path. |
| II | Spec-Grounded Scope | ✅ | ✅ | DTO/DDL cite only `FR-SRC-01`/`02`/`04` + `FR-COL-*` columns; no FR-DEP/DIM-03/DIM-04 leakage. `FR-SRC-03` not cited as satisfied (deferred to read-side). |
| III | Customizer Substrate | ✅ | ✅ | All FKs declared via raw SQL; no `Customizer.*` DTO imports. Pageview-key read via the pinned `IAnalyticsStateProvider.CurrentRequest.PageviewKey` + the new visitor-bound pageview lookup at the Analyzer repo layer (no new Customizer API consumed). Cascade-disposition divergence from contract D8 is documented and contract-amendment-flagged (§6). |
| IV | Additive-Only Storage, Cascade-Step | ✅ | ✅ | New table + new cascade step (§6); hard-FK to `customizerVisitorProfile` + `analyzerSession`; no row of existing tables modified. Hard-delete participation pattern justified under Principle IV v1.1.1 + FR-SRC-04. |
| V | Slice-Driven Delivery | ✅ | ✅ | All work specked + planned; no direct-to-main bypass. |
| VI | Software Engineering Excellence | ✅ | ✅ | Vertical-slice layout; unit + integration tests planned (see plan.md "Structure"). 100-pair normaliser fixture is an additive test asset. |
| VII | Security by Design | ✅ | ✅ | Four-corner gate at the controller; visitor-bound `pageviewKey` check (R3) strengthens defence vs slice 006; CHECK constraints add DB-layer defence; PII-redacted audit log (R6 + SC-006) strengthens posture vs slice 004's custom-event auditor. |
| VIII | Performance & Scalability | ✅ | ✅ | Fire-and-forget helper; in-process normalisation; indexed cascade probe; indexed top-queries aggregation key; no global locks. |
| IX | Umbraco-Native & Operator-First | ✅ | ✅ | `analyzer.sendSearch(...)` mirrors `analyzer.send(...)` shape; reuses operator-known `analyzer-no-tracking` attribute. |
| X | Extensibility by Design | ✅ | ✅ | One additive `IAnalyticsEventStateProvider` member; one new public extension point (`IAnalyzerSearchQueryNormaliser`) with default impl, `Scoped` lifetime per R5, last-registration-wins replacement convention. Pinning baseline regenerated additively. |

**Post-design verdict: 10 / 10 PASS.** No new design choice introduced a violation. Plan-time Constitution Check stands.
