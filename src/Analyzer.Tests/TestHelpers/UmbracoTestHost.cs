using System.Security.Claims;
using Analyzer.Composers;
using Customizer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Analyzer.Tests.TestHelpers;

/// <summary>
/// Minimal service-collection bootstrapper for slice-001 integration
/// tests. Sets up enough plumbing for
/// <see cref="AnalyzerComposer.ConfigureServices"/> to run (with or
/// without a fake <see cref="IPersonalizationProfile"/> wired in) and
/// for <see cref="Analyzer.Features.Visitors.Application.Contracts.IVisitorIdentifier"/>
/// to resolve against a synthetic <see cref="HttpContext"/>.
///
/// This is intentionally lighter than booting a full Umbraco host —
/// slice 001's User Stories don't require an HTTP pipeline; they
/// require DI resolution and composer behavior verification only.
/// Per research R4, <c>AnalyzerComposer.ConfigureServices</c> is the
/// test seam (extracted from <c>Compose(IUmbracoBuilder)</c>) so
/// tests don't need to mock <c>IUmbracoBuilder</c>.
/// </summary>
public static class UmbracoTestHost
{
    /// <summary>
    /// Build an <see cref="IServiceProvider"/> with Customizer's
    /// <see cref="IPersonalizationProfile"/> registered (using
    /// <paramref name="profile"/> as the implementation), then run
    /// <see cref="AnalyzerComposer.ConfigureServices"/> on it.
    /// </summary>
    public static IServiceProvider BuildWithCustomizer(IPersonalizationProfile profile)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddSingleton(profile);

        AnalyzerComposer.ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Build an <see cref="IServiceCollection"/> that does NOT register
    /// <see cref="IPersonalizationProfile"/>, then return it so callers
    /// can invoke <see cref="AnalyzerComposer.ConfigureServices"/> and
    /// observe the resulting <see cref="AnalyzerCompositionException"/>
    /// (US1 acceptance scenario 2).
    /// </summary>
    public static IServiceCollection BuildWithoutCustomizer()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services;
    }

    /// <summary>
    /// Attach a synthetic <see cref="HttpContext"/> with the given
    /// EntraID claims so <see cref="Analyzer.Features.Visitors.Application.VisitorIdentifier"/>
    /// can read them via <see cref="IHttpContextAccessor"/>.
    /// </summary>
    public static void SetClaims(IServiceProvider provider, params Claim[] claims)
    {
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length > 0 ? "Test" : null)),
        };
        accessor.HttpContext = httpContext;
    }
}
