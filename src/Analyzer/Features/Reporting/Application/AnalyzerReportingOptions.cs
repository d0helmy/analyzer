namespace Analyzer.Features.Reporting.Application;

/// <summary>
/// Slice 008 — host-overridable configuration bound from
/// <c>Analyzer:Reporting</c>. Defaults are applied by
/// <see cref="AnalyzerReportingOptionsPostConfigurator"/> so the
/// host's <c>appsettings.json</c> only needs an entry when an
/// operator wants to deviate from the conventional defaults.
/// </summary>
internal sealed class AnalyzerReportingOptions
{
    /// <summary>
    /// Umbraco user-group alias that grants access to per-visitor
    /// (individual-level) data. When <c>null</c>, empty, or
    /// whitespace, the post-configurator substitutes the
    /// conventional <c>"Analytics.IndividualData"</c> alias.
    /// </summary>
    /// <remarks>
    /// MVP has no per-visitor fields to gate — this option exists so
    /// the future per-visitor drill-down slice can ship its UI
    /// without re-plumbing the role-gate primitive. See
    /// <c>contracts/IIndividualDataAccessCheck.md</c>.
    /// </remarks>
    public string? IndividualDataUserGroupAlias { get; set; }
}
