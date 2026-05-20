using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Web;
using Analyzer.Reporting.ContentAnalytics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Reporting.Web;

/// <summary>
/// Slice 008 / T025 — pins the controller's branching between
/// 200-with-snapshot and 404-with-problem-details. Headers and
/// problem-details type are asserted against
/// <c>contracts/AnalyzerContentAnalyticsManagementController.md</c>.
/// </summary>
public sealed class AnalyzerContentAnalyticsManagementControllerTests
{
    private static readonly Guid ContentKey =
        new("ac716910-a82e-4280-bdf1-3b752e04b5b3");

    [Fact]
    public async Task Returns_200_with_snapshot_and_no_store_header_when_service_resolves()
    {
        var snapshot = new ContentAnalyticsSnapshot(
            ContentKey: ContentKey,
            WindowEndUtc: DateTimeOffset.UtcNow,
            Pageviews24h: 12,
            Pageviews7d: 84,
            Pageviews30d: 318,
            UniqueVisitors30d: 47,
            AvgTimeOnPageSeconds30d: 92,
            IsContentCurrentlyTombstoned: false,
            TopReferrers30d: Array.Empty<string>());
        var (controller, _) = NewController(snapshot);

        var result = await controller.GetAsync(ContentKey, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeSameAs(snapshot);
        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public async Task Returns_404_problem_details_when_service_returns_null()
    {
        var (controller, _) = NewController(snapshot: null);

        var result = await controller.GetAsync(ContentKey, CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        objectResult.ContentTypes.Should().Contain("application/problem+json");

        var problem = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Type.Should().Be(AnalyzerContentAnalyticsManagementController.ProblemTypeNotFound);
        problem.Title.Should().Be("Content node not found");
        problem.Status.Should().Be(404);
        problem.Extensions.Should().ContainKey("contentKey");
        problem.Extensions["contentKey"].Should().Be(ContentKey);
    }

    [Fact]
    public async Task Cache_control_header_is_set_on_404_path_too()
    {
        var (controller, _) = NewController(snapshot: null);

        _ = await controller.GetAsync(ContentKey, CancellationToken.None);

        controller.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    private static (AnalyzerContentAnalyticsManagementController Controller, StubQueryService Service)
        NewController(ContentAnalyticsSnapshot? snapshot)
    {
        var service = new StubQueryService(snapshot);
        var controller = new AnalyzerContentAnalyticsManagementController(service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (controller, service);
    }

    private sealed class StubQueryService : IContentAnalyticsQueryService
    {
        private readonly ContentAnalyticsSnapshot? _snapshot;
        public StubQueryService(ContentAnalyticsSnapshot? snapshot) => _snapshot = snapshot;

        public Task<ContentAnalyticsSnapshot?> GetAsync(Guid contentKey, CancellationToken ct)
            => Task.FromResult(_snapshot);
    }
}
