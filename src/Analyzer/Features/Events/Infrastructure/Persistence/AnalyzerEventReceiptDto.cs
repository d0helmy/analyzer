using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Analyzer.Features.Events.Infrastructure.Persistence;

/// <summary>
/// NPoco DTO mapping the <c>analyzerEventReceipt</c> table — the first
/// Analyzer-owned table (data-model §1). One row per
/// <c>PageviewCaptured</c> notification successfully processed.
/// </summary>
/// <remarks>
/// FK to <c>customizerVisitorProfile(key)</c> is declared in the
/// migration body via raw SQL, NOT via <c>[ForeignKey]</c> here —
/// importing Customizer's internal <c>VisitorProfileDto</c> would
/// breach Principle III (Customizer's persistence DTOs are not part of
/// the pinned public surface). See data-model §1 pinned decision.
/// </remarks>
[TableName(Constants.Database.AnalyzerEventReceipt)]
[PrimaryKey(nameof(Id), AutoIncrement = false)]
[ExplicitColumns]
internal sealed class AnalyzerEventReceiptDto
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("pageviewKey")]
    [Index(IndexTypes.UniqueNonClustered, Name = "UX_analyzerEventReceipt_pageviewKey")]
    public Guid PageviewKey { get; set; }

    [Column("visitorProfileKey")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerEventReceipt_visitorProfileKey")]
    public Guid VisitorProfileKey { get; set; }

    [Column("receivedUtc")]
    [Index(IndexTypes.NonClustered, Name = "IDX_analyzerEventReceipt_receivedUtc")]
    public DateTimeOffset ReceivedUtc { get; set; }
}
