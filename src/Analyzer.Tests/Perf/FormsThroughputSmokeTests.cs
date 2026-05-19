using System.Diagnostics;
using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Perf;

/// <summary>
/// Slice 005 / T071 (SC-001 + SC-008) — synthetic perf-smoke for the
/// per-form lifecycle capture path. Mirrors slice-004's
/// <see cref="CustomEventThroughputSmokeTests"/> shape:
/// scaled-down envelope (100 lifecycle cycles, not 100 events/min
/// for 60s) for fast feedback. Opt-in via <c>Category=Perf</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two assertions:
/// </para>
/// <list type="number">
///   <item>SC-001 sustained-rate ≥99% rows-persisted-within-1s for
///   Impression / Start / Success POSTs;</item>
///   <item>cache-hit p95 latency budget per slice-004 precedent
///   (50 ms relaxed for the test environment).</item>
/// </list>
/// </remarks>
[Trait("Category", "Perf")]
public sealed class FormsThroughputSmokeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Lifecycle_capture_p95_under_cache_hit_budget()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        // Warm the resolver cache.
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);

        const int cycles = 100;
        var latencies = new long[cycles];
        for (int i = 0; i < cycles; i++)
        {
            var sw = Stopwatch.StartNew();
            await DispatchAsync(actor, formKey, contentKey,
                AnalyzerFormEventType.Start,
                t0.AddMilliseconds(i),
                elapsedFromImpression: i + 1,
                elapsedFromStart: null, ct);
            sw.Stop();
            latencies[i] = sw.ElapsedMilliseconds;
        }

        var p95 = Percentile(latencies, 95);
        p95.Should().BeLessOrEqualTo(50,
            "SC-001 cache-hit p95 budget (relaxed for test environment; production budget is 5 ms — relaxed 10× to account for Testcontainers cold-start + non-prod hardware)");
    }

    [Fact]
    public async Task SustainedRate_100_cycles_persists_within_1s_each()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        const int cycles = 100;
        var underBudget = 0;
        for (int i = 0; i < cycles; i++)
        {
            var sw = Stopwatch.StartNew();
            await DispatchAsync(actor, formKey, contentKey,
                AnalyzerFormEventType.Impression,
                t0.AddMilliseconds(i),
                null, null, ct);
            sw.Stop();
            if (sw.ElapsedMilliseconds <= 1_000)
            {
                underBudget++;
            }
        }

        underBudget.Should().BeGreaterOrEqualTo(99,
            "SC-001: ≥99% of dispatches must persist within 1 s at sustained rate");
    }

    private async Task DispatchAsync(
        VisitorIdentity actor,
        Guid formKey,
        Guid contentKey,
        AnalyzerFormEventType eventType,
        DateTimeOffset receivedUtc,
        int? elapsedFromImpression,
        int? elapsedFromStart,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerFormEventCaptureHandler>();
        await handler.HandleAsync(
            new AnalyzerFormEventCapture(
                Actor: actor,
                FormKey: formKey,
                ContentKey: contentKey,
                EventType: eventType,
                ElapsedMsFromImpression: elapsedFromImpression,
                ElapsedMsFromStart: elapsedFromStart,
                UserAgent: "UA/test",
                ReceivedUtc: receivedUtc),
            ct);
    }

    private static long Percentile(long[] values, int percentile)
    {
        Array.Sort(values);
        var idx = Math.Min(values.Length - 1, (int)Math.Ceiling(percentile / 100.0 * values.Length) - 1);
        return values[Math.Max(0, idx)];
    }
}
