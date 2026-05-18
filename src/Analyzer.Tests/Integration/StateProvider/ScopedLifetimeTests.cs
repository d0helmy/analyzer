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
}
