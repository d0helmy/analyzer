using Microsoft.Extensions.Options;

namespace Analyzer.Features.Reporting.Application;

/// <summary>
/// Defaults the role-gate alias to <c>"Analytics.IndividualData"</c>
/// when the host has not supplied an explicit value (null, empty, or
/// whitespace). Prevents a configuration omission from silently
/// authorising every backoffice user against the future per-visitor
/// drill-down endpoint variant.
/// </summary>
internal sealed class AnalyzerReportingOptionsPostConfigurator
    : IPostConfigureOptions<AnalyzerReportingOptions>
{
    internal const string FallbackGroupAlias = "Analytics.IndividualData";

    public void PostConfigure(string? name, AnalyzerReportingOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.IndividualDataUserGroupAlias))
        {
            options.IndividualDataUserGroupAlias = FallbackGroupAlias;
        }
    }
}
