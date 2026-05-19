using Analyzer.Analytics;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Application.Anonymization;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Scroll;

/// <summary>
/// Slice 006 / T038 — Analyzer's scroll-event cascade DELETE must
/// participate in the ambient outer
/// <see cref="Umbraco.Cms.Infrastructure.Scoping.IScopeProvider"/>
/// transaction. When the outer scope does NOT call <c>Complete()</c>
/// (simulating a downstream cascade-step throwing), the DELETE rolls
/// back atomically. Mirrors slice-002 + slice-004 + slice-005
/// precedent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeRollbackTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Throw_after_scroll_step_rolls_back_the_delete()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;
        await SeedScrollEventsAsync(visitor, count: 3, ct);
        Count(visitor).Should().Be(3);

        using (var outerScope = ScopeProvider.CreateScope())
        {
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerScrollSampleCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitor, ct);

            // outerScope disposes without Complete() — DELETE rolls back.
        }

        Count(visitor).Should().Be(3,
            "Analyzer's DELETE participates in the outer NPoco scope; without scope.Complete() it rolls back atomically");
    }

    private async Task SeedScrollEventsAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        var buckets = new[] {
            AnalyzerScrollBucket.Quarter,
            AnalyzerScrollBucket.Half,
            AnalyzerScrollBucket.ThreeQuarters,
            AnalyzerScrollBucket.Full,
        };
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalyzerScrollEventCaptureHandler>()
                .HandleAsync(
                    new AnalyzerScrollEventCapture(
                        Actor: actor,
                        PageviewKey: Guid.NewGuid(),
                        ContentKey: contentKey,
                        Bucket: buckets[i % 4],
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(i)),
                    ct);
        }
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerScrollSample} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
