using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Sessions.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerSession</c> table — Analyzer's
/// second owned table (data-model §1). One row per session: a bounded
/// sequence of pageviews by one visitor on one device within the
/// configured inactivity timeout.
/// </summary>
/// <remarks>
/// <para>
/// FK to <c>customizerVisitorProfile(key)</c>, the partial unique
/// index on <c>(visitorProfileKey, deviceKey) WHERE isActive = 1</c>,
/// and the composite sweep index on <c>(isActive, lastActivityUtc)</c>
/// are declared in <c>M0002</c>'s migration body via raw SQL —
/// importing Customizer's internal <c>VisitorProfileDto</c> would
/// breach Principle III, and NPoco's <c>[Index]</c> attribute doesn't
/// model <c>WHERE</c> clauses.
/// </para>
/// <para>
/// SQLite skips the FK + partial unique index declarations (lesson #39);
/// the application-layer guarantee + the single-instance dev path
/// suffice.
/// </para>
/// </remarks>
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
