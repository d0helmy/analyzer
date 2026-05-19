using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Scroll.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerScrollSample</c> table — slice 006's
/// per-pageview scroll-milestone row (data-model §1.1). One row per
/// accepted milestone crossing per <c>(visitorKey, pageviewKey,
/// contentKey, bucket)</c>, where <c>bucket</c> is the byte-encoded
/// <see cref="Analyzer.Analytics.AnalyzerScrollBucket"/> enum value
/// (25 / 50 / 75 / 100).
/// </summary>
/// <remarks>
/// <para>
/// Hard FK to <c>customizerVisitorProfile(key)</c>, the <c>CHECK</c>
/// constraint on <c>bucket IN (25, 50, 75, 100)</c>, and the composite
/// unique index <c>UX_analyzerScrollSample_pageviewBucket</c> on
/// <c>(pageviewKey, bucket)</c> are declared in
/// <see cref="Migrations.M0006_AddAnalyzerScrollSampleTable"/>'s
/// migration body via raw SQL — NPoco's <c>[Index]</c> attribute is
/// single-column, and importing Customizer's <c>CustomerPageviewDto</c>
/// or <c>VisitorProfileDto</c> would breach Principle III.
/// </para>
/// <para>
/// SQLite skips the FK + CHECK + composite-UX declarations (slice-002
/// lesson #39); application-layer guarantees plus the single-instance
/// dev path suffice. CI runs against SQL Server via Testcontainers,
/// where the unique-index defence-in-depth fully applies.
/// </para>
/// </remarks>
[TableName(Constants.Database.AnalyzerScrollSample)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerScrollSampleDto
{
    [Column("id")]
    public Guid Id { get; set; }

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
