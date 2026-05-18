using Analyzer.Analytics;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.StateProvider;

/// <summary>
/// US3 AS1 — <see cref="IAnalyticsEventStateProvider"/> is registered
/// scoped: same instance within a scope, different across scopes;
/// fresh scope yields <c>null</c> until the handler writes.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ScopedLifetimeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public void ResolutionReturnsSameInstanceWithinScope()
    {
        using var scope = Services.CreateScope();

        var a = scope.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();
        var b = scope.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        b.Should().BeSameAs(a);
    }

    [Fact]
    public void ResolutionReturnsDifferentInstanceAcrossScopes()
    {
        using var scopeOne = Services.CreateScope();
        using var scopeTwo = Services.CreateScope();

        var a = scopeOne.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();
        var b = scopeTwo.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        b.Should().NotBeSameAs(a);
    }

    [Fact]
    public void CurrentReceiptIsNullBeforeHandlerWrites()
    {
        using var scope = Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        provider.CurrentRequestReceipt.Should().BeNull();
    }

    /// <summary>
    /// Slice 004 / T022 (US1 AS4) — fresh scopes return an empty list
    /// for <c>CurrentRequestCustomEvents</c> (never null). Read-only
    /// view is identity-stable across multiple reads inside one scope.
    /// </summary>
    [Fact]
    public void CurrentRequestCustomEventsIsEmptyInFreshScope()
    {
        using var scope = Services.CreateScope();

        var provider = scope.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        provider.CurrentRequestCustomEvents.Should().NotBeNull();
        provider.CurrentRequestCustomEvents.Should().BeEmpty();
    }

    [Fact]
    public void CurrentRequestCustomEventsIsIsolatedAcrossScopes()
    {
        using var scopeOne = Services.CreateScope();
        using var scopeTwo = Services.CreateScope();

        var providerOne = scopeOne.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();
        var providerTwo = scopeTwo.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        providerOne.CurrentRequestCustomEvents.Should().BeEmpty();
        providerTwo.CurrentRequestCustomEvents.Should().BeEmpty();
    }
}
