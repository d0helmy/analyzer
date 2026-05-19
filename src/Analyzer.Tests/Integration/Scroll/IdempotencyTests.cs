using Analyzer.Analytics;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Scroll;

/// <summary>
/// Slice 006 / T036 (US1 AS2 + SC-002) — DB-enforced idempotency
/// via the unique index <c>UX_analyzerScrollSample_pageviewBucket</c>.
/// A second POST for the same <c>(pageviewKey, bucket)</c> tuple
/// raises <see cref="ScrollSampleDuplicateException"/>; the handler
/// re-throws to the controller which maps to HTTP 409. Only one row
/// ever lands.
/// </summary>
[Trait("Category", "Integration")]
public sealed class IdempotencyTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Same_pageview_bucket_tuple_rejected_with_ScrollSampleDuplicateException()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        // First insert succeeds.
        await DispatchAsync(visitor, pageviewKey, contentKey,
            AnalyzerScrollBucket.Half, t0, ct);

        // Second insert for the same (pageviewKey, bucket) tuple fails.
        var act = async () => await DispatchAsync(
            visitor, pageviewKey, contentKey,
            AnalyzerScrollBucket.Half, t0.AddSeconds(1), ct);

        await act.Should().ThrowAsync<ScrollSampleDuplicateException>();

        Count(visitor).Should().Be(1, "exactly one row per (pageviewKey, bucket) tuple");
    }

    [Fact]
    public async Task Different_buckets_for_same_pageview_all_persist()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        foreach (var bucket in new[] {
            AnalyzerScrollBucket.Quarter,
            AnalyzerScrollBucket.Half,
            AnalyzerScrollBucket.ThreeQuarters,
            AnalyzerScrollBucket.Full,
        })
        {
            await DispatchAsync(visitor, pageviewKey, contentKey, bucket, t0, ct);
        }

        Count(visitor).Should().Be(4);
    }

    [Fact]
    public async Task Same_bucket_for_different_pageviews_both_persist()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var pageviewA = Guid.NewGuid();
        var pageviewB = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(visitor, pageviewA, contentKey, AnalyzerScrollBucket.Quarter, t0, ct);
        await DispatchAsync(visitor, pageviewB, contentKey, AnalyzerScrollBucket.Quarter, t0, ct);

        Count(visitor).Should().Be(2,
            "the unique index is scoped to (pageviewKey, bucket); different pageviews are independent");
    }

    private async Task DispatchAsync(
        Guid visitor,
        Guid pageviewKey,
        Guid contentKey,
        AnalyzerScrollBucket bucket,
        DateTimeOffset receivedUtc,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerScrollEventCaptureHandler>();
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
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
