using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.CustomEvents.Web;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Xunit;

namespace Analyzer.Tests.Unit.Features.CustomEvents.Web;

/// <summary>
/// Slice 004 / T017 — direct unit tests against
/// <see cref="AnalyzerCustomEventController"/>. Auth/anti-forgery
/// rejections (401 / 400) at the framework level are covered in
/// integration tests (T044); these tests exercise the action-body
/// logic — whitespace guards, visitor-identity defensive check, and
/// happy-path 202.
/// </summary>
public sealed class AnalyzerCustomEventControllerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Happy_path_returns_202_with_eventKey()
    {
        var visitor = Guid.NewGuid();
        var expectedEventKey = Guid.NewGuid();
        var handler = new FakeHandler { NextEventKey = expectedEventKey };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(visitor)),
            handler);

        var result = await controller.Capture(
            new CustomEventPayload
            {
                Category = "engagement",
                Action = "click",
                Label = "header-cta",
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<AcceptedResult>();
        var body = ((AcceptedResult)result).Value.Should().BeOfType<CustomEventResponse>().Subject;
        body.EventKey.Should().Be(expectedEventKey);
        handler.CallCount.Should().Be(1);
        handler.LastCommand!.Actor.Key.Should().Be(visitor);
        handler.LastCommand.Category.Should().Be("engagement");
        handler.LastCommand.Action.Should().Be("click");
        handler.LastCommand.Label.Should().Be("header-cta");
        handler.LastCommand.ReceivedUtc.Should().Be(T0);
    }

    [Fact]
    public async Task Whitespace_only_category_returns_400()
    {
        var handler = new FakeHandler();
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())),
            handler);

        var result = await controller.Capture(
            new CustomEventPayload { Category = "   ", Action = "click" },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Whitespace_only_action_returns_400()
    {
        var handler = new FakeHandler();
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())),
            handler);

        var result = await controller.Capture(
            new CustomEventPayload { Category = "engagement", Action = "  " },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Unavailable_actor_returns_401()
    {
        var handler = new FakeHandler();
        var controller = NewController(
            new StubVisitorIdentifier(default),  // IsAvailable=false
            handler);

        var result = await controller.Capture(
            new CustomEventPayload { Category = "engagement", Action = "click" },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
        handler.CallCount.Should().Be(0);
    }

    private static AnalyzerCustomEventController NewController(
        IVisitorIdentifier identifier,
        ICustomEventCaptureHandler handler)
    {
        var controller = new AnalyzerCustomEventController(
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

    private sealed class StubVisitorIdentifier : IVisitorIdentifier
    {
        private readonly VisitorIdentity _identity;
        public StubVisitorIdentifier(VisitorIdentity identity) => _identity = identity;
        public VisitorIdentity GetCurrent() => _identity;
    }

    private sealed class FakeHandler : ICustomEventCaptureHandler
    {
        public int CallCount { get; private set; }
        public CustomEventCapture? LastCommand { get; private set; }
        public Guid NextEventKey { get; set; } = Guid.NewGuid();

        public Task<Guid> HandleAsync(CustomEventCapture command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            return Task.FromResult(NextEventKey);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
