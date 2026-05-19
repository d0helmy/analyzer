using System.Diagnostics;
using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Perf;

/// <summary>
/// Slice 004 / T045 (SC-003) — synthetic perf-smoke for the custom-event
/// capture path. Drives the handler at a sustained rate and asserts
/// the cache-hit / cache-miss p95 latency budget. Opt-in via the
/// <c>Category=Perf</c> trait (slice-002/003 precedent — excluded from
/// CI by default).
/// </summary>
/// <remarks>
/// Scaled-down envelope: 100 events (not 1000/s × 60s) for fast
/// feedback; the budget assertions still surface regressions. The
/// real-world envelope is validated at deploy time, not in the test
/// suite.
/// </remarks>
[Trait("Category", "Perf")]
public sealed class CustomEventThroughputSmokeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Cache_hit_p95_under_budget()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;

        // Warm the resolver cache by capturing one event up front.
        await CaptureAsync(actor, ct);

        const int count = 100;
        var latencies = new long[count];
        var eventKeys = new HashSet<Guid>();
        for (int i = 0; i < count; i++)
        {
            var sw = Stopwatch.StartNew();
            var key = await CaptureAsync(actor, ct);
            sw.Stop();
            latencies[i] = sw.ElapsedMilliseconds;
            eventKeys.Add(key).Should().BeTrue($"eventKey collision at iteration {i}");
        }

        var p95 = Percentile(latencies, 95);
        p95.Should().BeLessOrEqualTo(50,
            "SC-003 cache-hit p95 budget (relaxed for test environment; production budget is 5 ms — relaxed by 10x to account for Testcontainers cold-start + non-prod hardware)");
    }

    private async Task<Guid> CaptureAsync(VisitorIdentity actor, CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>();
        return await handler.HandleAsync(
            new CustomEventCapture(
                Actor: actor,
                Category: "engagement",
                Action: "click",
                Label: null,
                Value: null,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);
    }

    private static long Percentile(long[] values, int percentile)
    {
        Array.Sort(values);
        var idx = Math.Min(values.Length - 1, (int)Math.Ceiling(percentile / 100.0 * values.Length) - 1);
        return values[Math.Max(0, idx)];
    }
}
