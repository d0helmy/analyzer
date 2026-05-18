using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.CustomEvents.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerCustomEvent</c> table — Analyzer's
/// third owned table (data-model §1). One row per
/// <c>analyzer.send(...)</c> POST successfully processed by the
/// management endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Hard FKs to <c>analyzerSession(sessionKey)</c> (first Analyzer-to-Analyzer
/// hard FK) and to <c>customizerVisitorProfile(key)</c>, plus the
/// composite <c>(category, action)</c> index, are declared in
/// <c>M0003</c>'s migration body via raw SQL: NPoco's <c>[Index]</c>
/// attribute is single-column and importing Customizer's
/// <c>VisitorProfileDto</c> would breach Principle III.
/// </para>
/// <para>
/// SQLite skips the FK declarations (lesson #39). The
/// application-layer guarantee + the single-instance dev path suffice.
/// </para>
/// </remarks>
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

    // Column precision (decimal(18,4)) set in M0003 via raw SQL — neither
    // NPoco nor Umbraco's database annotations carry a precision attribute,
    // and the default NPoco mapping for `decimal?` varies by provider.
    [Column("value")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public decimal? Value { get; set; }

    [Column("receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
