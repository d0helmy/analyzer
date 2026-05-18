using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Events.Application.Anonymization;
using Analyzer.Features.Events.Infrastructure.Dispatcher;
using Analyzer.Features.Events.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Analyzer.Features.Visitors.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Customizer.Composers;
using Customizer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;

namespace Analyzer.Composers;

/// <summary>
/// Wires the Analyzer service graph into the host Umbraco's DI
/// container. Runs after Customizer's <see cref="VisitorAnalyticsComposer"/>
/// so Customizer's <see cref="IPersonalizationProfile"/> registration
/// is present at probe time.
///
/// Constitution Principle III: Analyzer never modifies Customizer's
/// pinned surface; it only reads from it. Constitution Principle X:
/// <see cref="IVisitorIdentifier"/> is registered as scoped per spec
/// Clarification Q3 (HttpContext-lifetime alignment).
///
/// FR-002: if <see cref="IPersonalizationProfile"/> is not registered,
/// composition throws <see cref="AnalyzerCompositionException"/> and
/// no Analyzer services are added.
/// </summary>
[ComposeAfter(typeof(VisitorAnalyticsComposer))]
public sealed class AnalyzerComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.Configure<AnalyzerWriteQueueOptions>(
            builder.Config.GetSection("Analyzer:WriteQueue"));

        // Slice 003 — IOptionsMonitor-reloadable session tunables.
        builder.Services.Configure<AnalyzerSessionOptions>(
            builder.Config.GetSection("Analyzer:Session"));

        ConfigureServices(builder.Services);
    }

    /// <summary>
    /// Wire the Analyzer service graph into <paramref name="services"/>.
    /// Throws <see cref="AnalyzerCompositionException"/> if Customizer's
    /// <see cref="IPersonalizationProfile"/> is not registered.
    ///
    /// Exposed so unit tests can exercise the registration + fail-fast
    /// logic without constructing a full <c>IUmbracoBuilder</c>.
    /// </summary>
    internal static void ConfigureServices(IServiceCollection services)
    {
        if (!IsCustomizerRegistered(services))
        {
            throw new AnalyzerCompositionException(
                $"{Constants.PackageName} requires Customizer as a runtime prerequisite. " +
                $"No service descriptor for {nameof(IPersonalizationProfile)} was found in the host's " +
                $"DI container. Install the Customizer package and ensure its composer runs before " +
                $"{nameof(AnalyzerComposer)}. See docs/INTER-PRODUCT-CONTRACT.md §1.");
        }

        // IHttpContextAccessor is normally registered by ASP.NET Core,
        // but call AddHttpContextAccessor to be safe in minimal test hosts.
        services.AddHttpContextAccessor();

        // Slice-001 — FR-003 + Constitution Principle X: scoped per spec
        // Clarification Q3 (HttpContext-aligned lifetime).
        services.AddScoped<IVisitorIdentifier, VisitorIdentifier>();

        // Slice-002 — TimeProvider for the handler + dispatcher
        // (TryAdd so tests can override).
        services.TryAddSingleton(TimeProvider.System);

        // Slice-002 — bounded write queue (singleton; single instance
        // per host) + scoped repository + hosted dispatcher + transient
        // notification handler. Mirrors Customizer's
        // VisitorAnalyticsComposer wiring.
        services.AddSingleton<AnalyzerEventReceiptWriteQueue>();
        services.AddScoped<IAnalyzerEventReceiptRepository, AnalyzerEventReceiptRepository>();
        services.AddHostedService<AnalyzerEventReceiptWriteDispatcher>();
        services.AddTransient<
            INotificationAsyncHandler<PageviewCaptured>,
            PageviewCapturedHandler>();

        // Slice-002 US2 — receipt-deleting cascade step that
        // participates in Customizer's AnonymizeVisitorProfileHandler
        // outer scope. Scoped per IAnonymizationCascadeStep convention.
        services.AddScoped<IAnonymizationCascadeStep, AnalyzerEventReceiptCascadeStep>();

        // Slice-002 US3 — scoped state store + public state provider.
        // FR-007: scoped per Clarifications Q3 (request-aligned).
        // Slice-003 extends the state store + provider with CurrentSession
        // (additive; same scope; same lifetime).
        services.AddScoped<AnalyticsEventStateStore>();
        services.AddScoped<IAnalyticsEventStateProvider, AnalyticsEventStateProvider>();

        // Slice-003 — session subsystem.
        // - Cache: singleton (one per host; spans request scopes by definition).
        // - Repository: scoped (matches slice-002 receipt repo lifetime).
        // - Resolver: scoped (called synchronously from handler; transitive
        //   deps compose cleanly under scoped resolution).
        services.AddSingleton<AnalyzerSessionCacheStore>();
        services.AddScoped<IAnalyzerSessionRepository, AnalyzerSessionRepository>();
        services.AddScoped<IAnalyzerSessionResolver, AnalyzerSessionResolver>();
    }

    internal static bool IsCustomizerRegistered(IServiceCollection services)
    {
        for (int i = 0; i < services.Count; i++)
        {
            if (services[i].ServiceType == typeof(IPersonalizationProfile))
            {
                return true;
            }
        }

        return false;
    }
}
