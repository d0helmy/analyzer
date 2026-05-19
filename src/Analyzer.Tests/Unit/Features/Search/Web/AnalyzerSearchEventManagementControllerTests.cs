using Analyzer.Analytics;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Search.Web;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Search.Web;

/// <summary>
/// Slice 007 / T026 — direct unit tests for
/// <see cref="AnalyzerSearchEventManagementController"/>. Framework-
/// level auth + anti-forgery rejections are covered in integration
/// tests; these exercise the action-body logic: unauthorised-visitor
/// short-circuit, validation-exception mapping to 400, happy-path 202.
/// </summary>
public sealed class AnalyzerSearchEventManagementControllerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HappyPathReturns202WithEventKey()
    {
        var visitor = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var expectedEventKey = Guid.NewGuid();
        var handler = new FakeHandler
        {
            NextProjection = NewProjection(expectedEventKey, visitor, pageviewKey, contentKey),
        };
        var controller = NewController(new StubVisitorIdentifier(NewIdentity(visitor)), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = pageviewKey,
                Query = "design system",
                ResultCount = 12,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<AcceptedResult>();
        var body = ((AcceptedResult)result).Value
            .Should().BeOfType<AnalyzerSearchEventResponse>().Subject;
        body.EventKey.Should().Be(expectedEventKey);
        handler.CallCount.Should().Be(1);
        handler.LastCommand!.Actor.Key.Should().Be(visitor);
        handler.LastCommand.PageviewKey.Should().Be(pageviewKey);
        handler.LastCommand.RawQuery.Should().Be("design system");
        handler.LastCommand.ResultCount.Should().Be(12);
        handler.LastCommand.ReceivedUtc.Should().Be(T0);
    }

    [Fact]
    public async Task ControllerTrimsQueryBeforeBuildingCommand()
    {
        var visitor = Guid.NewGuid();
        var handler = new FakeHandler
        {
            NextProjection = NewProjection(Guid.NewGuid(), visitor, Guid.NewGuid(), Guid.NewGuid()),
        };
        var controller = NewController(new StubVisitorIdentifier(NewIdentity(visitor)), handler);

        await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "  design system  ",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        handler.LastCommand!.RawQuery.Should().Be("design system",
            "controller trims defensively (server re-trims even though client trims too — Principle VII).");
    }

    [Fact]
    public async Task UnavailableActorReturns401()
    {
        var handler = new FakeHandler();
        var controller = NewController(new StubVisitorIdentifier(default), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "anything",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task EmptyVisitorKeyReturns401()
    {
        var handler = new FakeHandler();
        var emptyKeyIdentity = new VisitorIdentity(true, Guid.Empty, "oid", "upn", false);
        var controller = NewController(new StubVisitorIdentifier(emptyKeyIdentity), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "anything",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RejectsEmptyPageviewKeyWith400()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerSearchPayloadValidationException(
                nameof(AnalyzerSearchEventPayload.PageviewKey),
                "pageviewKey must be a non-empty Guid."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.Empty,
                Query = "x",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task RejectsEmptyQueryWith400()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerSearchPayloadValidationException(
                nameof(AnalyzerSearchEventPayload.Query),
                "rawQuery must be a non-empty string after trim."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "   ",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task RejectsPageviewBindingFailureWith400()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerSearchPayloadValidationException(
                nameof(AnalyzerSearchEventPayload.PageviewKey),
                "pageviewKey does not belong to the resolved visitor."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "x",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task HandlerUnauthorizedAccessExceptionReturns401()
    {
        var handler = new FakeHandler
        {
            Throw = new UnauthorizedAccessException("identity unavailable"),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Capture(
            new AnalyzerSearchEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                Query = "x",
                ResultCount = 1,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
    }

    private static AnalyzerSearchEventManagementController NewController(
        IVisitorIdentifier identifier,
        IAnalyzerSearchEventCaptureHandler handler)
    {
        var controller = new AnalyzerSearchEventManagementController(
            identifier, handler, new FixedClock(T0));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        controller.ControllerContext.HttpContext.Request.Headers["User-Agent"] = "UA/test";
        return controller;
    }

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);

    private static AnalyticsSearchEvent NewProjection(Guid eventKey, Guid visitor, Guid pageview, Guid content) =>
        new()
        {
            EventKey = eventKey,
            VisitorProfileKey = visitor,
            SessionKey = Guid.NewGuid(),
            PageviewKey = pageview,
            ContentKey = content,
            RawQuery = "design system",
            NormalisedQuery = "design system",
            ResultCount = 12,
            ReceivedUtc = T0,
        };

    private sealed class StubVisitorIdentifier : IVisitorIdentifier
    {
        private readonly VisitorIdentity _identity;
        public StubVisitorIdentifier(VisitorIdentity identity) => _identity = identity;
        public VisitorIdentity GetCurrent() => _identity;
    }

    private sealed class FakeHandler : IAnalyzerSearchEventCaptureHandler
    {
        public int CallCount { get; private set; }
        public AnalyzerSearchEventCapture? LastCommand { get; private set; }
        public AnalyticsSearchEvent NextProjection { get; set; } = default!;
        public Exception? Throw { get; set; }

        public Task<AnalyticsSearchEvent> HandleAsync(AnalyzerSearchEventCapture command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            if (Throw is not null)
            {
                throw Throw;
            }
            return Task.FromResult(NextProjection);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
