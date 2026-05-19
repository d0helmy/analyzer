using System.Diagnostics;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.Integration.Search;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Perf;

/// <summary>
/// Slice 007 / T047 + T048 (SC-001 + SC-004) — synthetic perf-smoke
/// for the search-event capture + cascade-delete paths. Mirrors
/// slice-006's <see cref="ScrollThroughputSmokeTests"/> shape:
/// scaled-down envelope for fast feedback. Opt-in via
/// <c>Category=Perf</c>.
/// </summary>
[Trait("Category", "Perf")]
public sealed class SearchThroughputSmokeTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task SustainedRate_one_hundred_searches_persist_within_1s_each()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        const int cycles = 100;
        var underBudget = 0;
        for (int i = 0; i < cycles; i++)
        {
            var sw = Stopwatch.StartNew();
            await DispatchAsync(actor, pageviewKey, $"query-{i:D5}", i % 50, t0.AddMilliseconds(i), ct);
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
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        const int rows = 1_000;
        for (int i = 0; i < rows; i++)
        {
            await DispatchAsync(actor, pageviewKey, $"query-{i:D5}", i % 50, t0.AddMilliseconds(i), ct);
        }

        Count(visitor).Should().Be(rows);

        using var scope = Services.CreateScope();
        var cascade = scope.ServiceProvider
            .GetServices<IAnonymizationCascadeStep>()
            .Single(s => s.GetType().Name == "AnalyzerSearchEventCascadeStep");

        var sw = Stopwatch.StartNew();
        await cascade.ExecuteAsync(visitor, ct);
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(200,
            "SC-004: cascade hard-delete of 1 000 rows via the indexed visitorProfileKey predicate must complete within 200 ms");
    }

    private async Task DispatchAsync(
        VisitorIdentity actor,
        Guid pageviewKey,
        string rawQuery,
        int resultCount,
        DateTimeOffset receivedUtc,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
        await handler.HandleAsync(
            new AnalyzerSearchEventCapture(
                Actor: actor,
                PageviewKey: pageviewKey,
                ContentKey: Guid.Empty,
                RawQuery: rawQuery,
                ResultCount: resultCount,
                UserAgent: "UA/test",
                ReceivedUtc: receivedUtc),
            ct);
    }
}
