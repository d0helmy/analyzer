using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T038 — the visitor-bound <c>pageviewKey</c> check
/// (research §R3 + FR-008). Three cases:
/// <list type="bullet">
///   <item>POST with pageviewKey belonging to a different visitor →
///     400, zero rows.</item>
///   <item>POST with non-existent pageviewKey → 400, zero rows.</item>
///   <item>POST with valid same-visitor pageviewKey → 202, one row.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class PageviewVisitorBindingTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task PageviewBelongingToDifferentVisitor_rejected_zero_rows_written()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewOwnedByB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        await SeedPageviewAsync(pageviewOwnedByB, visitorB, contentKey);
        var ct = TestContext.Current.CancellationToken;

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
        var act = async () => await handler.HandleAsync(
            new AnalyzerSearchEventCapture(
                Actor: NewIdentity(visitorA),
                PageviewKey: pageviewOwnedByB,
                ContentKey: Guid.Empty,
                RawQuery: "spoofed",
                ResultCount: 1,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.PageviewKey));
        ex.Message.Should().Contain("does not belong to the resolved visitor");
        Count(visitorA).Should().Be(0);
        Count(visitorB).Should().Be(0);
    }

    [Fact]
    public async Task NonExistentPageview_rejected_zero_rows_written()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
        var act = async () => await handler.HandleAsync(
            new AnalyzerSearchEventCapture(
                Actor: NewIdentity(visitor),
                PageviewKey: Guid.NewGuid(), // never seeded
                ContentKey: Guid.Empty,
                RawQuery: "ghost",
                ResultCount: 1,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.PageviewKey));
        ex.Message.Should().Contain("does not exist");
        Count(visitor).Should().Be(0);
    }

    [Fact]
    public async Task ValidSameVisitorPageview_persists_one_row()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
        var projection = await handler.HandleAsync(
            new AnalyzerSearchEventCapture(
                Actor: NewIdentity(visitor),
                PageviewKey: pageviewKey,
                ContentKey: Guid.Empty,
                RawQuery: "valid",
                ResultCount: 5,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);

        projection.PageviewKey.Should().Be(pageviewKey);
        projection.ContentKey.Should().Be(contentKey,
            "server projects contentKey from the pageview binding lookup");
        Count(visitor).Should().Be(1);
    }
}
