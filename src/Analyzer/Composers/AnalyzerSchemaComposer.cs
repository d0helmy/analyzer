using Analyzer.Migrations;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Registers Analyzer's <see cref="AnalyzerMigrationComponent"/> so the
/// migration plan executes on Umbraco boot. Separate from
/// <see cref="AnalyzerComposer"/> by convention (Customizer mirrors
/// this split) — the service-graph composer runs first; this composer
/// appends the migration component to the Umbraco runtime's component
/// pipeline.
/// </summary>
[ComposeAfter(typeof(AnalyzerComposer))]
public sealed class AnalyzerSchemaComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Components().Append<AnalyzerMigrationComponent>();
    }
}
