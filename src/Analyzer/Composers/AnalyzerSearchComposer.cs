using Analyzer.Analytics;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Application.Anonymization;
using Analyzer.Features.Search.Application.Normalisation;
using Analyzer.Features.Search.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Slice 007 — Search-feature DI registrations. Runs after
/// <see cref="AnalyzerComposer"/> so it can pile on the additive
/// seventh <c>IAnonymizationCascadeStep</c> registration plus the new
/// <see cref="IAnalyzerSearchQueryNormaliser"/> public extension point
/// without touching slice 002-006 composition.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a separate composer (vs. piling into
/// <see cref="AnalyzerComposer"/>) so the slice's surface area is
/// reviewable independently of slice 001-006 wiring. The management
/// controller is auto-discovered by Umbraco's management-API
/// pipeline — no explicit registration needed.
/// </para>
/// <para>
/// <see cref="IAnalyzerSearchQueryNormaliser"/> is registered via
/// plain <c>AddScoped</c> (NOT <c>TryAddScoped</c>) so a host composer
/// that runs later can override the default via the
/// last-registration-wins convention (research §R5).
/// </para>
/// </remarks>
[ComposeAfter(typeof(AnalyzerComposer))]
public sealed class AnalyzerSearchComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Default normaliser — Scoped lifetime per research §R5;
        // future-proofs hosts that want to consult per-request state
        // (e.g. locale claim). Last-registration-wins replacement
        // convention.
        builder.Services.AddScoped<IAnalyzerSearchQueryNormaliser, DefaultAnalyzerSearchQueryNormaliser>();

        // Repository: scoped (matches slice 002/003/004/005/006 repo lifetime).
        builder.Services.AddScoped<IAnalyzerSearchEventRepository, AnalyzerSearchEventRepository>();

        // Auditor: scoped — each capture is logged independently.
        builder.Services.AddScoped<IAnalyzerSearchEventAuditor, AnalyzerSearchEventAuditor>();

        // Handler: scoped (depends on scoped state-store + repository +
        // resolver + normaliser + auditor). The management controller
        // resolves this per request.
        builder.Services.AddScoped<IAnalyzerSearchEventCaptureHandler, AnalyzerSearchEventCaptureHandler>();

        // Cascade step: seventh IAnonymizationCascadeStep registration
        // alongside slice 002/003/004/005/006 steps. Hard-deletes the
        // visitor's analyzerSearchEvent rows inside Customizer's outer
        // NPoco scope (PII per FR-SRC-04 — diverges from contract D8;
        // see spec Clarifications §2). Scoped — matches slice-006
        // precedent.
        builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerSearchEventCascadeStep>();
    }
}
