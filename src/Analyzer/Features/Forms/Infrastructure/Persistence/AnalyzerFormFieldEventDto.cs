using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerFormFieldEvent</c> table — slice
/// 005's per-field interaction row (data-model §1.2). One row per
/// <c>FieldFocus</c> / <c>FieldUnfocus</c>; <see cref="HadValue"/> is
/// populated only on <c>FieldUnfocus</c>.
/// </summary>
/// <remarks>
/// <para>
/// Privacy invariant: there is no column intended to hold field
/// content. <see cref="HadValue"/> is a single bit derived from
/// <c>element.value.length &gt; 0</c> on the client; values themselves
/// are never transmitted.
/// </para>
/// <para>
/// Composite indexes <c>(formKey, fieldKey, eventType)</c> and
/// <c>(visitorProfileKey, formKey, sessionKey)</c> (cascade probe)
/// are declared in <see cref="Migrations.M0005_AddAnalyzerFormFieldEventTable"/>'s
/// migration body via raw SQL.
/// </para>
/// </remarks>
[TableName(Constants.Database.AnalyzerFormFieldEvent)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerFormFieldEventDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("eventKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerFormFieldEvent_eventKey")]
    public Guid EventKey { get; set; }

    [Column("visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("sessionKey")]
    [NullSetting(NullSetting = NullSettings.Null)]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormFieldEvent_sessionKey")]
    public Guid? SessionKey { get; set; }

    [Column("formKey")]
    public Guid FormKey { get; set; }

    [Column("fieldKey")]
    public Guid FieldKey { get; set; }

    [Column("eventType")]
    public byte EventType { get; set; }

    [Column("hadValue")]
    [NullSetting(NullSetting = NullSettings.Null)]
    public bool? HadValue { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerFormFieldEvent_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
