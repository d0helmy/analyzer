using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Scroll.Application;

/// <summary>
/// Slice 006 / T020 — orchestrator unit tests for
/// <see cref="AnalyzerScrollEventCaptureHandler"/>. Covers the 6
/// conformance items in the contract doc:
/// RejectsUnavailableActor, RejectsEmptyVisitorKey, RejectsInvalidBucket,
/// RejectsEmptyPageviewKey, HappyPathInsertsAndAppendsState,
/// DuplicateRowAuditedAsDuplicateAndStateNotAppended.
/// </summary>
public sealed class AnalyzerScrollEventCaptureHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectsUnavailableActor()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewCommand(Guid.NewGuid()) with
        {
            Actor = new VisitorIdentity(
                IsAvailable: false, Key: Guid.NewGuid(),
                Oid: null, Upn: null, IsAnonymized: false),
        };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RejectsEmptyVisitorKey()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewCommand(Guid.Empty);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RejectsEmptyPageviewKey()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor);
        var command = NewCommand(Guid.NewGuid()) with { PageviewKey = Guid.Empty };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerScrollPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerScrollEventCapture.PageviewKey));
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestScrollEvents.Should().BeEmpty();
        auditor.AcceptedCalls.Should().Be(0);
        auditor.DuplicateCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsInvalidBucket()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor);
        var command = NewCommand(Guid.NewGuid()) with { Bucket = (AnalyzerScrollBucket)42 };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerScrollPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerScrollEventCapture.Bucket));
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestScrollEvents.Should().BeEmpty();
        auditor.AcceptedCalls.Should().Be(0);
    }

    [Fact]
    public async Task HappyPathInsertsAndAppendsState()
    {
        var handler = NewHandler(out var resolver, out var repo, out var store, out var auditor);
        var visitor = Guid.NewGuid();
        var command = NewCommand(visitor);

        var eventKey = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        eventKey.Should().NotBe(Guid.Empty);
        resolver.CallCount.Should().Be(1);
        resolver.LastActivityKind.Should().Be(SessionActivityKind.ScrollEvent,
            "scroll milestones are intentional engagement");
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.EventKey.Should().Be(eventKey);
        repo.LastInsert.VisitorProfileKey.Should().Be(visitor);
        repo.LastInsert.PageviewKey.Should().Be(command.PageviewKey);
        repo.LastInsert.Bucket.Should().Be((byte)command.Bucket);
        store.CurrentRequestScrollEvents.Should().HaveCount(1);
        store.CurrentRequestScrollEvents[0].EventKey.Should().Be(eventKey);
        store.CurrentRequestScrollEvents[0].Bucket.Should().Be(command.Bucket);
        auditor.AcceptedCalls.Should().Be(1);
        auditor.DuplicateCalls.Should().Be(0);
        auditor.LastEventKey.Should().Be(eventKey);
    }

    [Fact]
    public async Task DuplicateRowAuditedAsDuplicateAndStateNotAppended()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor);
        var command = NewCommand(Guid.NewGuid());
        repo.NextInsertThrows = new ScrollSampleDuplicateException(command.PageviewKey, command.Bucket);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ScrollSampleDuplicateException>();

        repo.InsertCalls.Should().Be(1);
        store.CurrentRequestScrollEvents.Should().BeEmpty(
            "duplicate path must NOT append — no row landed for this request");
        auditor.AcceptedCalls.Should().Be(0);
        auditor.DuplicateCalls.Should().Be(1, "the 409 path emits a Duplicate-tagged audit entry");
        auditor.LastPageviewKey.Should().Be(command.PageviewKey);
        auditor.LastBucket.Should().Be(command.Bucket);
    }

    private static AnalyzerScrollEventCaptureHandler NewHandler(
        out FakeResolver resolver,
        out FakeRepository repo,
        out AnalyticsEventStateStore store,
        out FakeAuditor auditor)
    {
        resolver = new FakeResolver();
        repo = new FakeRepository();
        store = new AnalyticsEventStateStore();
        auditor = new FakeAuditor();
        return new AnalyzerScrollEventCaptureHandler(
            resolver, repo, store, auditor,
            NullLogger<AnalyzerScrollEventCaptureHandler>.Instance);
    }

    private static AnalyzerScrollEventCapture NewCommand(Guid visitorKey) =>
        new(
            Actor: visitorKey == Guid.Empty
                ? new VisitorIdentity(IsAvailable: true, Key: Guid.Empty, Oid: null, Upn: null, IsAnonymized: false)
                : new VisitorIdentity(IsAvailable: true, Key: visitorKey, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false),
            PageviewKey: Guid.NewGuid(),
            ContentKey: Guid.NewGuid(),
            Bucket: AnalyzerScrollBucket.Half,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

    private sealed class FakeResolver : IAnalyzerSessionResolver
    {
        public int CallCount { get; private set; }
        public SessionActivityKind LastActivityKind { get; private set; }
        public Guid NextSessionKey { get; set; } = Guid.NewGuid();

        public ValueTask<SessionResolutionResult> ResolveAsync(
            Guid visitorProfileKey,
            string? userAgent,
            DateTimeOffset receivedUtc,
            SessionActivityKind activityKind,
            CancellationToken ct)
        {
            CallCount++;
            LastActivityKind = activityKind;
            var projection = new AnalyticsSession(
                SessionKey: NextSessionKey,
                VisitorProfileKey: visitorProfileKey,
                StartUtc: receivedUtc,
                LastActivityUtc: receivedUtc,
                EndUtc: null,
                PageviewCount: 1,
                IsActive: true);
            return ValueTask.FromResult(new SessionResolutionResult(NextSessionKey, projection));
        }
    }

    private sealed class FakeRepository : IAnalyzerScrollSampleRepository
    {
        public int InsertCalls { get; private set; }
        public AnalyzerScrollSampleDto? LastInsert { get; private set; }
        public Exception? NextInsertThrows { get; set; }

        public Task InsertAsync(AnalyzerScrollSampleDto dto, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = dto;
            if (NextInsertThrows is not null)
            {
                var toThrow = NextInsertThrows;
                NextInsertThrows = null;
                throw toThrow;
            }
            return Task.CompletedTask;
        }

        public Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.FromResult(0);
    }

    private sealed class FakeAuditor : IAnalyzerScrollEventAuditor
    {
        public int AcceptedCalls { get; private set; }
        public int DuplicateCalls { get; private set; }
        public Guid LastEventKey { get; private set; }
        public Guid LastPageviewKey { get; private set; }
        public AnalyzerScrollBucket LastBucket { get; private set; }

        public void AuditAccepted(
            VisitorIdentity actor, Guid eventKey, Guid pageviewKey,
            AnalyzerScrollBucket bucket, DateTimeOffset receivedUtc)
        {
            AcceptedCalls++;
            LastEventKey = eventKey;
            LastPageviewKey = pageviewKey;
            LastBucket = bucket;
        }

        public void AuditDuplicate(
            VisitorIdentity actor, Guid attemptedEventKey, Guid pageviewKey,
            AnalyzerScrollBucket bucket, DateTimeOffset receivedUtc)
        {
            DuplicateCalls++;
            LastEventKey = attemptedEventKey;
            LastPageviewKey = pageviewKey;
            LastBucket = bucket;
        }
    }
}
