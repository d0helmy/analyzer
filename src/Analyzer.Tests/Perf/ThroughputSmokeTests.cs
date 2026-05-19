using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Xunit;

namespace Analyzer.Tests.Perf;

/// <summary>
/// SC-002 perf gate (research §10) — 1000 pubs/s sustained for 60 s
/// asserts ≥ 99% receipts persisted. Opt-in via the <c>Perf</c> trait
/// so regular CI/PR runs skip it and only the dedicated perf run
/// invokes it.
/// </summary>
[Trait("Category", "Perf")]
public sealed class ThroughputSmokeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task SustainedPublishesPersistAtLeastNinetyNinePercent()
    {
        const int targetPubsPerSec = 1_000;
        const int durationSeconds = 60;
        const int totalPubs = targetPubsPerSec * durationSeconds;

        var aggregator = Services.GetRequiredService<IEventAggregator>();
        var visitorKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorKey);
        var pageviewKeys = new List<Guid>(totalPubs);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < totalPubs; i++)
        {
            var pvKey = Guid.NewGuid();
            pageviewKeys.Add(pvKey);
            await aggregator.PublishAsync(
                new PageviewCaptured(NewPageview(pvKey, visitorKey)),
                TestContext.Current.CancellationToken);

            // Pace publishes to the target rate.
            var expectedElapsedMs = (i + 1) * 1_000.0 / targetPubsPerSec;
            var actualMs = stopwatch.ElapsedMilliseconds;
            if (actualMs < expectedElapsedMs)
            {
                await Task.Delay(
                    (int)(expectedElapsedMs - actualMs),
                    TestContext.Current.CancellationToken);
            }
        }

        // Give the dispatcher time to drain the queue.
        await Task.Delay(5_000, TestContext.Current.CancellationToken);

        using var scope = ScopeProvider.CreateScope();
        var persistedCount = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
            visitorKey);
        scope.Complete();

        var dropRate = 1.0 - (persistedCount / (double)totalPubs);
        dropRate.Should().BeLessOrEqualTo(0.01,
            $"SC-002: ≤ 1% drop under 1000 pv/s sustained for 60 s (actual drops: {totalPubs - persistedCount}/{totalPubs})");
    }

    private static Pageview NewPageview(Guid pageviewKey, Guid visitorKey) => new(
        Key: pageviewKey,
        VisitorProfileKey: visitorKey,
        ContentKey: Guid.NewGuid(),
        Segments: PageviewSegmentSet.Empty,
        WasContentTombstoned: false,
        RequestUtc: DateTimeOffset.UtcNow);
}
