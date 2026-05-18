using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Analyzer's migration plan. Slice 002 introduces
/// <see cref="M0001_AddAnalyzerEventReceiptTable"/>; future slices
/// append <c>M0002</c>, <c>M0003</c>, etc. Plan-name <c>"Analyzer"</c>
/// keys the row in Umbraco's <c>umbracoKeyValue</c> migration-history
/// table; it must remain stable across versions.
/// </summary>
public sealed class AnalyzerMigrationPlan : MigrationPlan
{
    public const string PlanName = "Analyzer";

    public AnalyzerMigrationPlan() : base(PlanName)
    {
        From(string.Empty)
            .To<M0001_AddAnalyzerEventReceiptTable>("0001-AddAnalyzerEventReceiptTable");
    }
}
