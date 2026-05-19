using System.Diagnostics;
using Analyzer.Analytics;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Perf;

/// <summary>
/// Slice 006 / T045 + T046 (SC-001 + SC-004) — synthetic perf-smoke
/// for the scroll-milestone capture + cascade-delete paths. Mirrors
/// slice-005's <see cref="FormsThroughputSmokeTests"/> shape:
/// scaled-down envelope for fast feedback. Opt-in via
/// <c>Category=Perf</c>.
/// </summary>
[Trait("Category", "Perf")]
public sealed class ScrollThroughputSmokeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task SustainedRate_one_hundred_milestones_persist_within_1s_each()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        const int cycles = 100;
        var underBudget = 0;
        var buckets = new[] {
            AnalyzerScrollBucket.Quarter,
            AnalyzerScrollBucket.Half,
            AnalyzerScrollBucket.ThreeQuarters,
            AnalyzerScrollBucket.Full,
        };
        for (int i = 0; i < cycles; i++)
        {
            // Rotate pageviewKey every 4 to avoid the unique-index
            // rejection; each pageview gets all 4 buckets.
            var pageview = new Guid(i / 4, 0, 0, new byte[8]);
            var sw = Stopwatch.StartNew();
            await DispatchAsync(actor, pageview, contentKey,
                buckets[i % 4], t0.AddMilliseconds(i), ct);
            sw.Stop();
            if (sw.ElapsedMilliseconds <= 1_000)
            {
                underBudget++;
            }
        }

        underBudget.Should().BeGreaterOrEqualTo(99,
            "SC-001: ≥99% of dispatches must persist within 1 s at sustained rate");
    }

    [Fact]
    public async Task Cascade_one_thousand_rows_under_two_hundred_ms()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        const int rows = 1_000;
        var buckets = new[] {
            AnalyzerScrollBucket.Quarter,
            AnalyzerScrollBucket.Half,
            AnalyzerScrollBucket.ThreeQuarters,
            AnalyzerScrollBucket.Full,
        };
        for (int i = 0; i < rows; i++)
        {
            var pageview = new Guid(i / 4, 0, 0, new byte[8]);
            await DispatchAsync(actor, pageview, contentKey,
                buckets[i % 4], t0.AddMilliseconds(i), ct);
        }

        // Verify seed completed.
        using (var s = ScopeProvider.CreateScope())
        {
            var seeded = s.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerScrollSample} WHERE visitorProfileKey = @0",
                visitor);
            seeded.Should().Be(rows);
            s.Complete();
        }

        // Time the cascade.
        using var scope = Services.CreateScope();
        var cascade = scope.ServiceProvider
            .GetServices<IAnonymizationCascadeStep>()
            .Single(s => s.GetType().Name == "AnalyzerScrollSampleCascadeStep");

        var sw = Stopwatch.StartNew();
        await cascade.ExecuteAsync(visitor, ct);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(200,
            "SC-004: cascade hard-delete of 1 000 rows via the indexed visitorProfileKey predicate must complete within 200 ms");
    }

    private async Task DispatchAsync(
        VisitorIdentity actor,
        Guid pageviewKey,
        Guid contentKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerScrollEventCaptureHandler>();
        await handler.HandleAsync(
            new AnalyzerScrollEventCapture(
                Actor: actor,
                PageviewKey: pageviewKey,
                ContentKey: contentKey,
                Bucket: bucket,
                UserAgent: "UA/test",
                ReceivedUtc: receivedUtc),
            ct);
    }
}
