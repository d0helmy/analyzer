using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Analyzer's migration plan. Slice 002 introduced
/// <see cref="M0001_AddAnalyzerEventReceiptTable"/>; slice 003 added
/// <see cref="M0002_AddAnalyzerSessionTableAndReceiptSessionKey"/>;
/// slice 004 added <see cref="M0003_AddAnalyzerCustomEventTable"/>;
/// slice 005 appended
/// <see cref="M0004_AddAnalyzerFormEventTable"/> +
/// <see cref="M0005_AddAnalyzerFormFieldEventTable"/>; slice 006 appends
/// <see cref="M0006_AddAnalyzerScrollSampleTable"/>. Plan-name
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
            .To<M0002_AddAnalyzerSessionTableAndReceiptSessionKey>("0002-AddAnalyzerSessionTableAndReceiptSessionKey")
            .To<M0003_AddAnalyzerCustomEventTable>("0003-AddAnalyzerCustomEventTable")
            .To<M0004_AddAnalyzerFormEventTable>("0004-AddAnalyzerFormEventTable")
            .To<M0005_AddAnalyzerFormFieldEventTable>("0005-AddAnalyzerFormFieldEventTable")
            .To<M0006_AddAnalyzerScrollSampleTable>("0006-AddAnalyzerScrollSampleTable");
    }
}
