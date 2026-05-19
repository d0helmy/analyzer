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
/// Slice 006 / T035 (US1 AS1) — end-to-end per-pageview scroll-
/// milestone persistence. Driving the capture handler with four
/// bucket commands for one visitor + pageview persists 4 rows
/// attached to the same session; multi-visitor case asserts
/// disjointness.
/// </summary>
/// <remarks>
/// HTTP-boundary verification of the management endpoint route
/// remains gated on issue #23 (slice-004 unresolved mgmt-API 404 in
/// the test host). Handler-level evidence is accepted per slice-004
/// / slice-005 precedent and provides the core capture evidence at
/// slice-006 ship time.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EndToEndCaptureTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Four_milestones_persist_four_rows_under_same_session()
    {
        var visitor = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(actor, pageviewKey, contentKey, AnalyzerScrollBucket.Quarter, t0, ct);
        await DispatchAsync(actor, pageviewKey, contentKey, AnalyzerScrollBucket.Half, t0.AddSeconds(1), ct);
        await DispatchAsync(actor, pageviewKey, contentKey, AnalyzerScrollBucket.ThreeQuarters, t0.AddSeconds(2), ct);
        await DispatchAsync(actor, pageviewKey, contentKey, AnalyzerScrollBucket.Full, t0.AddSeconds(3), ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(4);
        rows.Select(r => r.Bucket).Should().BeEquivalentTo(new byte[] { 25, 50, 75, 100 },
            options => options.WithStrictOrdering());
        rows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1,
            "all four milestones in a single page lifecycle attach to the same session");
        rows.All(r => r.PageviewKey == pageviewKey).Should().BeTrue();
        rows.All(r => r.ContentKey == contentKey).Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_visitors_produce_disjoint_rows()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var ct = TestContext.Current.CancellationToken;
        var pageviewA = Guid.NewGuid();
        var pageviewB = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(NewIdentity(visitorA), pageviewA, contentKey, AnalyzerScrollBucket.Quarter, t0, ct);
        await DispatchAsync(NewIdentity(visitorA), pageviewA, contentKey, AnalyzerScrollBucket.Half, t0.AddSeconds(1), ct);
        await DispatchAsync(NewIdentity(visitorB), pageviewB, contentKey, AnalyzerScrollBucket.Quarter, t0, ct);

        ReadRows(visitorA).Should().HaveCount(2);
        ReadRows(visitorB).Should().HaveCount(1);
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

    private List<RowProjection> ReadRows(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<RowProjection>(
            $"SELECT eventKey AS EventKey, sessionKey AS SessionKey, " +
            $"       pageviewKey AS PageviewKey, contentKey AS ContentKey, " +
            $"       bucket AS Bucket, receivedUtc AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerScrollSample} " +
            $"WHERE visitorProfileKey = @0 ORDER BY receivedUtc",
            visitor);
        scope.Complete();
        return rows;
    }

    private static VisitorIdentity NewIdentity(Guid key) => new(
        IsAvailable: true,
        Key: key,
        Oid: "oid-1",
        Upn: "user@example.com",
        IsAnonymized: false);

    private sealed class RowProjection
    {
        public Guid EventKey { get; set; }
        public Guid? SessionKey { get; set; }
        public Guid PageviewKey { get; set; }
        public Guid ContentKey { get; set; }
        public byte Bucket { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }
}
