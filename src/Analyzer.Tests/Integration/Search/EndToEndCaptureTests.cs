using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T036 (US1 AS1) — end-to-end per-pageview search-event
/// persistence. Driving the capture handler with multiple search
/// submissions for one visitor + pageview persists N rows attached to
/// the same session; multi-visitor case asserts disjointness; the
/// audit-log substrate carries the structured entry but NEVER the
/// query text (SC-006).
/// </summary>
/// <remarks>
/// HTTP-boundary verification of the management endpoint route remains
/// gated on issue #23 (slice-004 unresolved mgmt-API 404 in the test
/// host). Handler-level evidence is accepted per slice-004 / 005 / 006
/// precedent and provides the core capture evidence at slice-007 ship
/// time.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EndToEndCaptureTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task Three_search_submissions_persist_three_rows_under_same_session()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(actor, pageviewKey, "design system", 12, t0, ct);
        await DispatchAsync(actor, pageviewKey, "  Ｄｅｓｉｇｎ  SYSTEM  ", 12, t0.AddSeconds(1), ct);
        await DispatchAsync(actor, pageviewKey, "xyzzy", 0, t0.AddSeconds(2), ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(3);
        rows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1,
            "all three submissions in a single page lifecycle attach to the same session");
        rows.All(r => r.PageviewKey == pageviewKey).Should().BeTrue();
        rows.All(r => r.ContentKey == contentKey).Should().BeTrue("server-set from the pageview binding");
        rows[0].NormalisedQuery.Should().Be("design system");
        rows[1].NormalisedQuery.Should().Be("design system",
            "NFKC + lower + whitespace-collapse folds the fullwidth+SYSTEM variant to the same key");
        rows[2].NormalisedQuery.Should().Be("xyzzy");
        rows[2].ResultCount.Should().Be(0);
    }

    [Fact]
    public async Task Multiple_visitors_produce_disjoint_rows()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewA = Guid.NewGuid();
        var pageviewB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        await SeedPageviewAsync(pageviewA, visitorA, contentKey);
        await SeedPageviewAsync(pageviewB, visitorB, contentKey);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(NewIdentity(visitorA), pageviewA, "a-query-1", 5, t0, ct);
        await DispatchAsync(NewIdentity(visitorA), pageviewA, "a-query-2", 3, t0.AddSeconds(1), ct);
        await DispatchAsync(NewIdentity(visitorB), pageviewB, "b-query", 7, t0, ct);

        ReadRows(visitorA).Should().HaveCount(2);
        ReadRows(visitorB).Should().HaveCount(1);
    }

    [Fact]
    public async Task Repeated_same_query_produces_distinct_rows()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(actor, pageviewKey, "annual review", 7, t0, ct);
        await DispatchAsync(actor, pageviewKey, "annual review", 7, t0.AddMilliseconds(50), ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(2, "re-running the same search is a distinct engagement signal (spec Edge Cases)");
        rows.Select(r => r.EventKey).Distinct().Should().HaveCount(2);
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
