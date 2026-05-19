using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Forms.Web;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Web;

/// <summary>
/// Slice 005 / T026 — direct unit tests for
/// <see cref="AnalyzerFormEventManagementController"/>. Auth + anti-
/// forgery rejections (401 / 403) at the framework level are covered
/// in the integration tests; these tests exercise the action-body
/// logic: unauthorised-visitor short-circuit, validation-exception
/// mapping to 400, happy-path 202.
/// </summary>
public sealed class AnalyzerFormEventManagementControllerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LifecycleHappyPathReturns202()
    {
        var visitor = Guid.NewGuid();
        var expectedEventKey = Guid.NewGuid();
        var handler = new FakeHandler { NextEventKey = expectedEventKey };
        var controller = NewController(new StubVisitorIdentifier(NewIdentity(visitor)), handler);

        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var result = await controller.Lifecycle(
            new AnalyzerFormEventPayload
            {
                FormKey = formKey,
                ContentKey = contentKey,
                EventType = AnalyzerFormEventType.Impression,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<AcceptedResult>();
        var body = ((AcceptedResult)result).Value
            .Should().BeOfType<AnalyzerFormEventResponse>().Subject;
        body.EventKey.Should().Be(expectedEventKey);
        handler.CallCount.Should().Be(1);
        handler.LastCommand!.Actor.Key.Should().Be(visitor);
        handler.LastCommand.FormKey.Should().Be(formKey);
        handler.LastCommand.ContentKey.Should().Be(contentKey);
        handler.LastCommand.EventType.Should().Be(AnalyzerFormEventType.Impression);
        handler.LastCommand.ReceivedUtc.Should().Be(T0);
    }

    [Fact]
    public async Task LifecycleUnavailableActorReturns401()
    {
        var handler = new FakeHandler();
        var controller = NewController(new StubVisitorIdentifier(default), handler);

        var result = await controller.Lifecycle(
            new AnalyzerFormEventPayload
            {
                FormKey = Guid.NewGuid(),
                ContentKey = Guid.NewGuid(),
                EventType = AnalyzerFormEventType.Impression,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<UnauthorizedResult>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task LifecycleRejectsEmptyFormKey()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerFormPayloadValidationException(
                nameof(AnalyzerFormEventPayload.FormKey),
                "formKey must be a non-empty Guid."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Lifecycle(
            new AnalyzerFormEventPayload
            {
                FormKey = Guid.Empty,
                ContentKey = Guid.NewGuid(),
                EventType = AnalyzerFormEventType.Impression,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task LifecycleRejectsMismatchedTimingSlots()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerFormPayloadValidationException(
                nameof(AnalyzerFormEventPayload.ElapsedMsFromImpression),
                "Start rows require elapsedMsFromImpression ≥ 0."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Lifecycle(
            new AnalyzerFormEventPayload
            {
                FormKey = Guid.NewGuid(),
                ContentKey = Guid.NewGuid(),
                EventType = AnalyzerFormEventType.Start,
                ElapsedMsFromImpression = null,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    private static AnalyzerFormEventManagementController NewController(
        IVisitorIdentifier identifier,
        IAnalyzerFormEventCaptureHandler handler)
    {
        var controller = new AnalyzerFormEventManagementController(
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

    private sealed class FakeHandler : IAnalyzerFormEventCaptureHandler
    {
        public int CallCount { get; private set; }
        public AnalyzerFormEventCapture? LastCommand { get; private set; }
        public Guid NextEventKey { get; set; } = Guid.NewGuid();
        public Exception? Throw { get; set; }

        public Task<Guid> HandleAsync(AnalyzerFormEventCapture command, CancellationToken ct)
        {
            CallCount++;
            LastCommand = command;
            if (Throw is not null)
            {
                throw Throw;
            }
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
