using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Xunit;

namespace Analyzer.Tests.Integration.PageviewSubscription;

/// <summary>
/// US1 AS2 — when Customizer's <c>PageviewCaptured</c> notification
/// fires but the parent <c>customizerPageview</c> row was dropped under
/// back-pressure, the receipt still persists (soft FK on
/// <c>pageviewKey</c>). Simulated by publishing a notification directly
/// without going through <c>PageviewCaptureMiddleware</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class BackPressureDropTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task NotificationWithAbsentParentPageviewWritesReceipt()
    {
        var visitorKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorKey);
        var pageview = NewPageview(Guid.NewGuid(), visitorKey);
        var aggregator = Services.GetRequiredService<IEventAggregator>();

        // Publish directly — bypasses PageviewCaptureMiddleware, which
        // would also enqueue the parent pageview to Customizer's queue.
        // The parent row is absent on purpose.
        await aggregator.PublishAsync(new PageviewCaptured(pageview), TestContext.Current.CancellationToken);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2_000)
        {
            using var scope = ScopeProvider.CreateScope();
            var count = scope.Database.ExecuteScalar<int>(
                $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE pageviewKey = @0",
                pageview.Key);
            scope.Complete();
            if (count == 1)
            {
                return;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        using var finalScope = ScopeProvider.CreateScope();
        var actual = finalScope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE pageviewKey = @0",
            pageview.Key);
        actual.Should().Be(1,
            "the soft FK on pageviewKey means the receipt persists even when the parent pageview row is absent");
    }

    private static Pageview NewPageview(Guid pageviewKey, Guid visitorKey) => new(
        Key: pageviewKey,
        VisitorProfileKey: visitorKey,
        ContentKey: Guid.NewGuid(),
        Segments: PageviewSegmentSet.Empty,
        WasContentTombstoned: false,
        RequestUtc: DateTimeOffset.UtcNow);
}
