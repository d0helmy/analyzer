using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Analyzer's migration plan. Slice 002 introduced
/// <see cref="M0001_AddAnalyzerEventReceiptTable"/>; slice 003 adds
/// <see cref="M0002_AddAnalyzerSessionTableAndReceiptSessionKey"/>.
/// Future slices append <c>M0003</c>, <c>M0004</c>, etc. Plan-name
/// <c>"Analyzer"</c> keys the row in Umbraco's <c>umbracoKeyValue</c>
/// migration-history table; it must remain stable across versions.
/// </summary>
public sealed class AnalyzerMigrationPlan : MigrationPlan
{
    public const string PlanName = "Analyzer";

    public AnalyzerMigrationPlan() : base(PlanName)
    {
        From(string.Empty)
            .To<M0001_AddAnalyzerEventReceiptTable>("0001-AddAnalyzerEventReceiptTable")
            .To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey");
    }
}
