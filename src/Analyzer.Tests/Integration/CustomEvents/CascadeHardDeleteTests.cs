using System.Diagnostics;
using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T038 (US2 AS1, SC-004) — running the cascade step
/// hard-deletes the visitor's custom events without touching other
/// visitors. Latency assertion (A3): seeding 1 000 rows and timing the
/// DELETE confirms the SC-004 budget (&lt;= 200 ms) via the indexed
/// <c>visitorProfileKey</c> predicate.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeHardDeleteTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Cascade_deletes_visitor_rows_leaves_other_visitors_intact()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitorA, count: 3, ct);
        await SeedAsync(visitorB, count: 2, ct);

        using (var scope = Services.CreateScope())
        {
            var cascade = ResolveCustomEventCascadeStep(scope.ServiceProvider);
            await cascade.ExecuteAsync(visitorA, ct);
        }

        Count(visitorA).Should().Be(0);
        Count(visitorB).Should().Be(2);
    }

    [Fact]
    public async Task Cascade_completes_under_two_hundred_ms_for_one_thousand_rows()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitor, count: 1_000, ct);

        using var scope = Services.CreateScope();
        var cascade = ResolveCustomEventCascadeStep(scope.ServiceProvider);

        var sw = Stopwatch.StartNew();
        await cascade.ExecuteAsync(visitor, ct);
        sw.Stop();

        Count(visitor).Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(200,
            "SC-004 budget: hard-delete of 1 000 rows via the indexed visitorProfileKey predicate must complete within 200 ms");
    }

    [Fact]
    public async Task Cascade_is_zero_row_noop_for_visitor_with_no_events()
    {
        var visitor = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        using var scope = Services.CreateScope();
        var cascade = ResolveCustomEventCascadeStep(scope.ServiceProvider);

        await cascade.ExecuteAsync(visitor, ct);

        Count(visitor).Should().Be(0);
    }

    private static IAnonymizationCascadeStep ResolveCustomEventCascadeStep(IServiceProvider sp)
    {
        var step = sp.GetServices<IAnonymizationCascadeStep>()
            .Single(s => s.GetType().Name == "AnalyzerCustomEventCascadeStep");
        return step;
    }

    private async Task SeedAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(new CustomEventCapture(
                    Actor: actor,
                    Category: "engagement",
                    Action: "click",
                    Label: $"l-{i}",
                    Value: null,
                    UserAgent: "UA/test",
                    ReceivedUtc: DateTimeOffset.UtcNow.AddMilliseconds(i)),
                    ct);
        }
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerCustomEvent} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
