using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerFormEvent</c> table — slice 005's
/// per-form lifecycle row (data-model §1.1). One row per
/// <c>(visitorKey, formKey, sessionKey, eventType)</c>, where
/// <c>eventType</c> is <c>Impression</c> / <c>Start</c> / <c>Success</c> /
/// <c>Abandon</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hard FK to <c>customizerVisitorProfile(key)</c> and the composite
/// lifecycle index <c>(visitorProfileKey, formKey, sessionKey, eventType)</c>
/// are declared in <see cref="Migrations.M0004_AddAnalyzerFormEventTable"/>'s
/// migration body via raw SQL — NPoco's <c>[Index]</c> attribute is
/// single-column, and importing Customizer's <c>VisitorProfileDto</c>
/// would breach Principle III.
/// </para>
/// <para>
/// SQLite skips the FK declaration (slice-002 lesson #39) — application-
/// layer guarantees plus the single-instance dev path suffice; CI runs
/// against SQL Server via Testcontainers.
/// </para>
/// </remarks>
[TableName(Constants.Database.AnalyzerFormEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerFormEventDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("eventKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerFormEvent_eventKey")]
    public Guid EventKey { get; set; }

    [Column("visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("sessionKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_sessionKey")]
    public Guid? SessionKey { get; set; }

    [Column("formKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_formKey")]
    public Guid FormKey { get; set; }

    [Column("contentKey")]
    public Guid ContentKey { get; set; }

    [Column("eventType")]
    public byte EventType { get; set; }

    [Column("elapsedMsFromImpression")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? ElapsedMsFromImpression { get; set; }

    [Column("elapsedMsFromStart")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public int? ElapsedMsFromStart { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormEvent_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
