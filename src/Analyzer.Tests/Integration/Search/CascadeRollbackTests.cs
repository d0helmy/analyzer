using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Application.Anonymization;
using Analyzer.Features.Search.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T040 — Analyzer's search-event cascade DELETE must
/// participate in the ambient outer
/// <see cref="Umbraco.Cms.Infrastructure.Scoping.IScopeProvider"/>
/// transaction. When the outer scope does NOT call <c>Complete()</c>
/// (simulating a downstream cascade-step throwing), the DELETE rolls
/// back atomically. Mirrors slice-002/004/005/006 precedent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeRollbackTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task Throw_after_search_step_rolls_back_the_delete()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;
        await SeedSearchEventsAsync(visitor, pageviewKey, count: 3, ct);
        Count(visitor).Should().Be(3);

        using (var outerScope = ScopeProvider.CreateScope())
        {
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerSearchEventCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitor, ct);

            // outerScope disposes without Complete() — DELETE rolls back.
        }

        Count(visitor).Should().Be(3,
            "Analyzer's DELETE participates in the outer NPoco scope; without scope.Complete() it rolls back atomically");
    }

    private async Task SeedSearchEventsAsync(Guid visitor, Guid pageviewKey, int count, CancellationToken ct)
    {
        var actor = NewIdentity(visitor);
        var t0 = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalyzerSearchEventCaptureHandler>()
                .HandleAsync(
                    new AnalyzerSearchEventCapture(
                        Actor: actor,
                        PageviewKey: pageviewKey,
                        ContentKey: Guid.Empty,
                        RawQuery: $"query-{i}",
                        ResultCount: 1,
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(i)),
                    ct);
        }
    }
}
