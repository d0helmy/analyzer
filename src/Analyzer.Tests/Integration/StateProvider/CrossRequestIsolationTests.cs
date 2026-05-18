using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.StateProvider;

/// <summary>
/// US3 AS2 — two concurrent scopes don't share state. Mutating one
/// scope's <see cref="AnalyticsEventStateStore"/> doesn't change the
/// other's <see cref="IAnalyticsEventStateProvider.CurrentRequestReceipt"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CrossRequestIsolationTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public void ConcurrentRequestsDoNotShareState()
    {
        using var scopeOne = Services.CreateScope();
        using var scopeTwo = Services.CreateScope();

        var storeOne = scopeOne.ServiceProvider.GetRequiredService<AnalyticsEventStateStore>();
        var providerTwo = scopeTwo.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();

        var receipt = new AnalyticsEventReceipt(
            Id: Guid.NewGuid(),
            PageviewKey: Guid.NewGuid(),
            VisitorProfileKey: Guid.NewGuid(),
            ReceivedUtc: DateTimeOffset.UtcNow);

        storeOne.SetCurrentReceipt(receipt);

        // Mutation in scopeOne must not surface in scopeTwo.
        providerTwo.CurrentRequestReceipt.Should().BeNull();

        // Sanity check: scopeOne's provider reads its own store's value.
        var providerOne = scopeOne.ServiceProvider.GetRequiredService<IAnalyticsEventStateProvider>();
        providerOne.CurrentRequestReceipt.Should().Be(receipt);
    }
}
