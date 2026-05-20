using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Slice 008 / T021 — Phase 3a read-side DI registrations. Layered
/// on top of <see cref="AnalyzerReportingComposer"/>'s Phase 2
/// foundational wiring via the <c>partial</c> seam so the slice
/// stays incrementally compilable phase-by-phase.
/// </summary>
public sealed partial class AnalyzerReportingComposer
{
    partial void ComposeReadSide(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<IContentAnalyticsRepository, ContentAnalyticsRepository>();
        builder.Services.AddScoped<IPublishedContentTombstoneProbe, PublishedContentTombstoneProbe>();
        builder.Services.AddScoped<IContentAnalyticsQueryService, ContentAnalyticsQueryService>();
    }
}
