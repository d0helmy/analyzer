using Analyzer.Features.Visitors.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Customizer.Composers;
using Customizer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

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
        => ConfigureServices(builder.Services);

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

        // FR-003 + Constitution Principle X: scoped per spec
        // Clarification Q3 (HttpContext-aligned lifetime).
        services.AddScoped<IVisitorIdentifier, VisitorIdentifier>();
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
