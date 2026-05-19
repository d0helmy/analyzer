using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Search.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerSearchEvent</c> table — slice 007's
/// per-pageview search-submission row (data-model §1.1). One row per
/// accepted intranet search submission, carrying the raw user-typed
/// query, the canonical normalised form (grouping key), and the
/// reported result count.
/// </summary>
/// <remarks>
/// <para>
/// Hard FKs to <c>customizerVisitorProfile(key)</c> and
/// <c>analyzerSession(sessionKey)</c>, plus three CHECK constraints
/// (<c>resultCount &gt;= 0</c>, length 1-256 on <c>rawQuery</c> +
/// <c>normalisedQuery</c>) are declared in
/// <see cref="Migrations.M0007_AddAnalyzerSearchEventTable"/>'s
/// migration body via raw SQL — NPoco's <c>[Index]</c> attribute is
/// single-column, and importing Customizer's <c>CustomerPageviewDto</c>
/// or <c>VisitorProfileDto</c> would breach Principle III.
/// </para>
/// <para>
/// <b>PII notice (FR-SRC-04)</b>: <c>RawQuery</c> and
/// <c>NormalisedQuery</c> are potentially personal data. Never logged
/// via the structured-log substrate (the DB row is the canonical,
/// role-gated record); the cascade step hard-deletes rows on visitor
/// anonymisation rather than re-keying.
/// </para>
/// <para>
/// SQLite skips the FK + CHECK declarations (slice-002 lesson #39);
/// application-layer guarantees plus the single-instance dev path
/// suffice. CI runs against SQL Server via Testcontainers, where the
/// FK + CHECK defence-in-depth fully applies.
/// </para>
/// </remarks>
[TableName(Constants.Database.AnalyzerSearchEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerSearchEventDto
{
    [Column("id")]
    public Guid Id { get; set; }

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
