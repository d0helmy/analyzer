using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Application.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Slice 008 — Reporting-feature DI registrations for the
/// per-content-node Analytics content app. Runs after
/// <see cref="AnalyzerComposer"/> so it can layer registrations on
/// top of slice 001-007 wiring without intermixing concerns.
/// </summary>
/// <remarks>
/// <para>
/// Slice 008 Phase 2 wires only the foundational primitives — the
/// <see cref="IIndividualDataAccessCheck"/> role-gate and its
/// <see cref="AnalyzerReportingOptions"/> binding. Phase 3a layers
/// the read-side repository / tombstone-probe / query service
/// registrations on top via a partial extension. The management
/// controller is auto-discovered by Umbraco's management-API
/// pipeline.
/// </para>
/// <para>
/// Lifetimes mirror slice 002-007 precedent: scoped for anything
/// that transitively depends on <c>IScopeProvider</c>, singleton for
/// the stateless role-gate.
/// </para>
/// </remarks>
[ComposeAfter(typeof(AnalyzerComposer))]
public sealed partial class AnalyzerReportingComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddOptions<AnalyzerReportingOptions>()
            .Bind(builder.Config.GetSection(Constants.Configuration.ReportingSection));
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IPostConfigureOptions<AnalyzerReportingOptions>, AnalyzerReportingOptionsPostConfigurator>());

        builder.Services.AddSingleton<IIndividualDataAccessCheck, DefaultIndividualDataAccessCheck>();

        ComposeReadSide(builder);
    }

    partial void ComposeReadSide(IUmbracoBuilder builder);
}
