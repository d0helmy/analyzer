using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Application.Abandonment;
using Analyzer.Features.Forms.Application.Anonymization;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace Analyzer.Composers;

/// <summary>
/// Slice 005 — Forms-feature DI registrations. Runs after
/// <see cref="AnalyzerComposer"/> so it can pile on additive
/// extension-point registrations (the second-and-third
/// <c>IAnonymizationCascadeStep</c> for slice 005 — one per new
/// table) and the new
/// <see cref="IAnalyzerFormAbandonmentMaterialiser"/> consumed by
/// slice-003's <c>AnalyzerSessionSweeperService</c>.
/// </summary>
/// <remarks>
/// Kept as a separate composer (vs. piling into
/// <see cref="AnalyzerComposer"/>) so the slice's surface area is
/// reviewable independently of the slice-001/002/003/004 wiring. The
/// management controller is auto-discovered by Umbraco's
/// management-API pipeline — no explicit registration needed.
/// </remarks>
[ComposeAfter(typeof(AnalyzerComposer))]
public sealed class AnalyzerFormsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Repository: scoped (matches slice 002/003/004 repo lifetime).
        builder.Services.AddScoped<IAnalyzerFormEventRepository, AnalyzerFormEventRepository>();

        // Auditor: scoped so each captured event emits its own log entry.
        builder.Services.AddScoped<IAnalyzerFormEventAuditor, AnalyzerFormEventAuditor>();

        // Handler: scoped (depends on scoped state-store + repository +
        // resolver). The management controller resolves this per request.
        builder.Services.AddScoped<IAnalyzerFormEventCaptureHandler, AnalyzerFormEventCaptureHandler>();

        // Cascade step (lifecycle table): fourth IAnonymizationCascadeStep
        // registration alongside slice 002/003/004 steps. Hard-deletes
        // the visitor's analyzerFormEvent rows inside Customizer's
        // outer NPoco scope. Singleton would be acceptable (no per-request
        // state) but Scoped matches slice 002/004 precedent.
        builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerFormEventCascadeStep>();

        // Abandonment materialiser: scoped — resolved per sweeper pass
        // via IServiceScopeFactory in AnalyzerSessionSweeperService.
        builder.Services.AddScoped<
            IAnalyzerFormAbandonmentMaterialiser,
            AnalyzerFormAbandonmentMaterialiser>();

        // Slice 005 US2 — field-level events.
        builder.Services.AddScoped<IAnalyzerFormFieldEventRepository, AnalyzerFormFieldEventRepository>();
        builder.Services.AddScoped<IAnalyzerFormFieldEventAuditor, AnalyzerFormFieldEventAuditor>();
        builder.Services.AddScoped<IAnalyzerFormFieldEventCaptureHandler, AnalyzerFormFieldEventCaptureHandler>();

        // Fifth IAnonymizationCascadeStep registration.
        builder.Services.AddScoped<IAnonymizationCascadeStep, AnalyzerFormFieldEventCascadeStep>();
    }
}
