using Analyzer.Features.Reporting.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Reporting;

/// <summary>
/// Slice 008 / T027–T029 — end-to-end query-service tests against
/// real persistence (Testcontainers MSSQL). Asserts window
/// monotonicity, cross-node isolation, and time-provider threading.
/// </summary>
/// <remarks>
/// <para>
/// HTTP-boundary verification of the management endpoint remains
/// gated on issue #23 (mgmt-API 404 in the test host). Query-service
/// boundary suffices per slice 004-007 precedent.
/// </para>
/// <para>
/// The tombstone-probe lookup is the production wiring (resolved
/// via <c>IUmbracoContextAccessor</c>) — there's no live published-
/// content cache in the test host, so the probe falls back to
/// "tombstoned" for all GUIDs. The query service still returns a
/// snapshot when any capture rows exist; assertions focus on the
/// counts rather than the tombstone flag.
/// </para>
/// </remarks>
[Trait("Category", "Integration")]
public sealed class ContentAnalyticsEndToEndTests : ReportingIntegrationTestBase
{
    [Fact]
    public async Task Returns_snapshot_with_monotonic_counts()
    {
        var contentKey = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await SeedSessionAsync(v1, now.AddDays(-30), now);
        await SeedSessionAsync(v2, now.AddDays(-30), now);
        await SeedSessionAsync(v3, now.AddDays(-30), now);

        // Spread pageviews: 1 in 24h, 1 in 24h-7d, 3 in 7d-30d.
        await SeedPageviewAsync(contentKey, v1, now.AddHours(-1));
        await SeedPageviewAsync(contentKey, v2, now.AddDays(-3));
        await SeedPageviewAsync(contentKey, v3, now.AddDays(-15));
        await SeedPageviewAsync(contentKey, v1, now.AddDays(-20));
        await SeedPageviewAsync(contentKey, v2, now.AddDays(-25));

        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();
        var snapshot = await query.GetAsync(contentKey, TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.Pageviews24h.Should().Be(1);
        snapshot.Pageviews7d.Should().BeGreaterThanOrEqualTo(snapshot.Pageviews24h);
        snapshot.Pageviews30d.Should().BeGreaterThanOrEqualTo(snapshot.Pageviews7d);
        snapshot.Pageviews30d.Should().Be(5);
        snapshot.UniqueVisitors30d.Should().Be(3);
    }

    [Fact]
    public async Task Returns_404_candidate_null_when_unknown_content_key_and_no_captures()
    {
        var unknown = Guid.NewGuid();
        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();

        var snapshot = await query.GetAsync(unknown, TestContext.Current.CancellationToken);

        snapshot.Should().BeNull(
            "no captures and no published content cache hit must surface as 404 (null projection)");
    }

    [Fact]
    public async Task Cross_node_pageviews_are_not_aggregated_together()
    {
        var nodeA = Guid.NewGuid();
        var nodeB = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await SeedSessionAsync(v1, now.AddDays(-30), now);
        await SeedSessionAsync(v2, now.AddDays(-30), now);

        await SeedPageviewAsync(nodeA, v1, now.AddHours(-2));
        await SeedPageviewAsync(nodeA, v2, now.AddHours(-3));
        await SeedPageviewAsync(nodeB, v1, now.AddHours(-4));

        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();
        var snapA = await query.GetAsync(nodeA, TestContext.Current.CancellationToken);
        var snapB = await query.GetAsync(nodeB, TestContext.Current.CancellationToken);

        snapA.Should().NotBeNull();
        snapB.Should().NotBeNull();
        snapA!.Pageviews30d.Should().Be(2);
        snapA.UniqueVisitors30d.Should().Be(2);
        snapB!.Pageviews30d.Should().Be(1);
        snapB.UniqueVisitors30d.Should().Be(1);
    }

    [Fact(Skip = "Deferred — slice-008-followup. Requires SeedPublishedContentAsync helper (Spec Kit analyze remediation T013a not applied). Without a real Umbraco published-content cache hit, the tombstone probe falls back to true, so the (no captures + tombstone=false) branch can't be exercised end-to-end in the existing test scaffold.")]
    public async Task EmptyContentReturns200WithZeros()
    {
        // US2 / T041 — full coverage of this branch requires seeding a
        // published content node via IContentService. Tracked as a
        // slice-008 followup alongside slice-007's #34 EntraID-claims
        // shim and the broader integration-test seeding gap (issue #20).
        await Task.CompletedTask;
    }

    [Fact]
    public async Task WindowEndUtc_is_set_from_the_request_time_provider()
    {
        var contentKey = Guid.NewGuid();
        var visitor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await SeedSessionAsync(visitor, now.AddDays(-30), now);
        await SeedPageviewAsync(contentKey, visitor, now.AddHours(-1));

        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();
        var t0 = Services.GetRequiredService<TimeProvider>().GetUtcNow();
        var snapshot = await query.GetAsync(contentKey, TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.WindowEndUtc.Should().BeCloseTo(t0, TimeSpan.FromSeconds(5));
    }
}
