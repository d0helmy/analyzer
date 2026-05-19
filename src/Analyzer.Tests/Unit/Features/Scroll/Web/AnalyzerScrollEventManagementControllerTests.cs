using Analyzer.Analytics;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Scroll.Web;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Scroll.Web;

/// <summary>
/// Slice 006 / T024 — direct unit tests for
/// <see cref="AnalyzerScrollEventManagementController"/>. Auth +
/// anti-forgery rejections (401 / 403) at the framework level are
/// covered in integration tests; these tests exercise the action-body
/// logic: unauthorised-visitor short-circuit, validation-exception
/// mapping to 400, duplicate-exception mapping to 409, happy-path
/// 202.
/// </summary>
public sealed class AnalyzerScrollEventManagementControllerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HappyPathReturns202WithEventKey()
    {
        var visitor = Guid.NewGuid();
        var expectedEventKey = Guid.NewGuid();
        var handler = new FakeHandler { NextEventKey = expectedEventKey };
        var controller = NewController(new StubVisitorIdentifier(NewIdentity(visitor)), handler);

        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var result = await controller.Milestone(
            new AnalyzerScrollEventPayload
            {
                PageviewKey = pageviewKey,
                ContentKey = contentKey,
                Bucket = AnalyzerScrollBucket.Half,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeOfType<AcceptedResult>();
        var body = ((AcceptedResult)result).Value
            .Should().BeOfType<AnalyzerScrollEventResponse>().Subject;
        body.EventKey.Should().Be(expectedEventKey);
        handler.CallCount.Should().Be(1);
        handler.LastCommand!.Actor.Key.Should().Be(visitor);
        handler.LastCommand.PageviewKey.Should().Be(pageviewKey);
        handler.LastCommand.ContentKey.Should().Be(contentKey);
        handler.LastCommand.Bucket.Should().Be(AnalyzerScrollBucket.Half);
        handler.LastCommand.ReceivedUtc.Should().Be(T0);
    }

    [Fact]
    public async Task UnavailableActorReturns401()
    {
        var handler = new FakeHandler();
        var controller = NewController(new StubVisitorIdentifier(default), handler);

        var result = await controller.Milestone(
            new AnalyzerScrollEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                ContentKey = Guid.NewGuid(),
                Bucket = AnalyzerScrollBucket.Quarter,
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
            Throw = new AnalyzerScrollPayloadValidationException(
                nameof(AnalyzerScrollEventPayload.PageviewKey),
                "pageviewKey must be a non-empty Guid."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Milestone(
            new AnalyzerScrollEventPayload
            {
                PageviewKey = Guid.Empty,
                ContentKey = Guid.NewGuid(),
                Bucket = AnalyzerScrollBucket.Quarter,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task RejectsInvalidBucketWith400()
    {
        var handler = new FakeHandler
        {
            Throw = new AnalyzerScrollPayloadValidationException(
                nameof(AnalyzerScrollEventPayload.Bucket),
                "bucket must be a defined AnalyzerScrollBucket value."),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Milestone(
            new AnalyzerScrollEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                ContentKey = Guid.NewGuid(),
                Bucket = (AnalyzerScrollBucket)42,
            },
            TestContext.Current.CancellationToken);

        result.Should().BeAssignableTo<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task DuplicateReturns409WithCode()
    {
        var handler = new FakeHandler
        {
            Throw = new ScrollSampleDuplicateException(Guid.NewGuid(), AnalyzerScrollBucket.Half),
        };
        var controller = NewController(
            new StubVisitorIdentifier(NewIdentity(Guid.NewGuid())), handler);

        var result = await controller.Milestone(
            new AnalyzerScrollEventPayload
            {
                PageviewKey = Guid.NewGuid(),
                ContentKey = Guid.NewGuid(),
                Bucket = AnalyzerScrollBucket.Half,
            },
            TestContext.Current.CancellationToken);

        var conflict = result.Should().BeOfType<ConflictObjectResult>().Subject;
        conflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        conflict.Value.Should().BeOfType<AnalyzerScrollEventDuplicateResponse>()
            .Which.Code.Should().Be("duplicate");
    }

    private static AnalyzerScrollEventManagementController NewController(
        IVisitorIdentifier identifier,
        IAnalyzerScrollEventCaptureHandler handler)
    {
        var controller = new AnalyzerScrollEventManagementController(
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

    private sealed class FakeHandler : IAnalyzerScrollEventCaptureHandler
    {
        public int CallCount { get; private set; }
        public AnalyzerScrollEventCapture? LastCommand { get; private set; }
        public Guid NextEventKey { get; set; } = Guid.NewGuid();
        public Exception? Throw { get; set; }

        public Task<Guid> HandleAsync(AnalyzerScrollEventCapture command, CancellationToken ct)
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
