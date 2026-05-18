using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Xunit;

namespace Analyzer.Tests.Integration.PageviewSubscription;

/// <summary>
/// US1 AS1 + AS3 + SC-001 + SC-004 — published <c>PageviewCaptured</c>
/// notifications produce exactly one row per unique <c>Pageview.Key</c>
/// within the spec's 1 s budget; duplicate publishes are deduplicated.
/// </summary>
[Trait("Category", "Integration")]
public sealed class EndToEndCaptureTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task PublishCausesReceiptRowInDb_WithinOneSecond()
    {
        var pageview = NewPageview(Guid.NewGuid(), Guid.NewGuid());
        var aggregator = Services.GetRequiredService<IEventAggregator>();

        await aggregator.PublishAsync(new PageviewCaptured(pageview), TestContext.Current.CancellationToken);

        await WaitForRowAsync(pageview.Key, expected: 1, deadlineMs: 1_000);
    }

    [Fact]
    public async Task DuplicatePublishesProduceSingleRow()
    {
        var pageview = NewPageview(Guid.NewGuid(), Guid.NewGuid());
        var aggregator = Services.GetRequiredService<IEventAggregator>();

        await aggregator.PublishAsync(new PageviewCaptured(pageview), TestContext.Current.CancellationToken);
        await aggregator.PublishAsync(new PageviewCaptured(pageview), TestContext.Current.CancellationToken);

        await WaitForRowAsync(pageview.Key, expected: 1, deadlineMs: 2_000);
    }

    [Fact]
    public async Task MultipleVisitorsProduceCorrectReceiptCounts()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var aggregator = Services.GetRequiredService<IEventAggregator>();

        var ct = TestContext.Current.CancellationToken;
        await aggregator.PublishAsync(new PageviewCaptured(NewPageview(Guid.NewGuid(), visitorA)), ct);
        await aggregator.PublishAsync(new PageviewCaptured(NewPageview(Guid.NewGuid(), visitorA)), ct);
        await aggregator.PublishAsync(new PageviewCaptured(NewPageview(Guid.NewGuid(), visitorB)), ct);

        await WaitForVisitorCountAsync(visitorA, expected: 2, deadlineMs: 2_000);
        await WaitForVisitorCountAsync(visitorB, expected: 1, deadlineMs: 2_000);
    }

    private async Task WaitForRowAsync(Guid pageviewKey, int expected, int deadlineMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < deadlineMs)
        {
            using var scope = ScopeProvider.CreateScope();
            var count = scope.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE pageviewKey = @0",
                pageviewKey);
            scope.Complete();
            if (count == expected)
            {
                return;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        // Final assertion to produce a useful failure message.
        using var finalScope = ScopeProvider.CreateScope();
        var actual = finalScope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE pageviewKey = @0",
            pageviewKey);
        actual.Should().Be(expected, "the dispatcher should have flushed the receipt within the deadline");
    }

    private async Task WaitForVisitorCountAsync(Guid visitorProfileKey, int expected, int deadlineMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < deadlineMs)
        {
            using var scope = ScopeProvider.CreateScope();
            var count = scope.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
                visitorProfileKey);
            scope.Complete();
            if (count == expected)
            {
                return;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        using var finalScope = ScopeProvider.CreateScope();
        var actual = finalScope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        actual.Should().Be(expected);
    }

    private static Pageview NewPageview(Guid pageviewKey, Guid visitorKey) => new(
        Key: pageviewKey,
        VisitorProfileKey: visitorKey,
        ContentKey: Guid.NewGuid(),
        Segments: PageviewSegmentSet.Empty,
        WasContentTombstoned: false,
        RequestUtc: DateTimeOffset.UtcNow);
}
