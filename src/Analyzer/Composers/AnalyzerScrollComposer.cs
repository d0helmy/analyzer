using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Application.Anonymization;
using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Slice 006 — Scroll-feature DI registrations. Runs after
/// <see cref="AnalyzerComposer"/> (and harmlessly alongside
/// <see cref="AnalyzerFormsComposer"/>) so it can pile on the
/// additive sixth <c>IAnonymizationCascadeStep</c> registration
/// without touching slice-005's composition.
/// </summary>
/// <remarks>
/// Kept as a separate composer (vs. piling into
/// <see cref="AnalyzerComposer"/>) so the slice's surface area is
/// reviewable independently of slice-001/002/003/004/005 wiring. The
/// management controller is auto-discovered by Umbraco's
/// management-API pipeline — no explicit registration needed.
/// </remarks>
[ComposeAfter(typeof(AnalyzerComposer))]
public sealed class AnalyzerScrollComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Repository: scoped (matches slice 002/003/004/005 repo lifetime).
        builder.Services.AddScoped<IAnalyzerScrollSampleRepository, AnalyzerScrollSampleRepository>();

        // Auditor: scoped — each capture is logged independently.
        builder.Services.AddScoped<IAnalyzerScrollEventAuditor, AnalyzerScrollEventAuditor>();

        // Handler: scoped (depends on scoped state-store + repository +
        // resolver). The management controller resolves this per request.
        builder.Services.AddScoped<IAnalyzerScrollEventCaptureHandler, AnalyzerScrollEventCaptureHandler>();

        // Cascade step: sixth IAnonymizationCascadeStep registration
        // alongside slice 002/003/004/005 steps. Hard-deletes the
        // visitor's analyzerScrollSample rows inside Customizer's
        // outer NPoco scope. Scoped — matches slice-005 precedent.
        builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerScrollSampleCascadeStep>();
    }
}
