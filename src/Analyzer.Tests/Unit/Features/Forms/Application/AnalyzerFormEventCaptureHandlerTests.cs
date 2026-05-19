using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Application;

/// <summary>
/// Slice 005 / T022 — orchestrator unit tests for
/// <see cref="AnalyzerFormEventCaptureHandler"/>. Covers the 5
/// conformance items in the contract doc: RejectsEmptyVisitor,
/// RejectsMismatchedTimingSlots, HappyPathInsertsAndAppendsState,
/// AuditEmittedOnceOnSuccess, SessionActivityDispatch.
/// </summary>
public sealed class AnalyzerFormEventCaptureHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectsEmptyVisitor()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewImpression(Guid.Empty);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RejectsImpressionWithElapsedSlots()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewImpression(Guid.NewGuid()) with { ElapsedMsFromImpression = 500 };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerFormEventCapture.EventType));
    }

    [Fact]
    public async Task RejectsStartWithoutElapsedMsFromImpression()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = new AnalyzerFormEventCapture(
            Actor: NewIdentity(Guid.NewGuid()),
            FormKey: Guid.NewGuid(),
            ContentKey: Guid.NewGuid(),
            EventType: AnalyzerFormEventType.Start,
            ElapsedMsFromImpression: null,
            ElapsedMsFromStart: null,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerFormEventCapture.ElapsedMsFromImpression));
    }

    [Fact]
    public async Task RejectsClientAbandon()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = new AnalyzerFormEventCapture(
            Actor: NewIdentity(Guid.NewGuid()),
            FormKey: Guid.NewGuid(),
            ContentKey: Guid.NewGuid(),
            EventType: AnalyzerFormEventType.Abandon,
            ElapsedMsFromImpression: null,
            ElapsedMsFromStart: 1000,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>();
    }

    [Fact]
    public async Task HappyPathInsertsAndAppendsState()
    {
        var handler = NewHandler(out var resolver, out var repo, out var store, out var auditor);
        var visitor = Guid.NewGuid();
        var command = NewImpression(visitor);

        var eventKey = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        eventKey.Should().NotBe(Guid.Empty);
        resolver.CallCount.Should().Be(1);
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.EventKey.Should().Be(eventKey);
        repo.LastInsert.VisitorProfileKey.Should().Be(visitor);
        repo.LastInsert.EventType.Should().Be((byte)AnalyzerFormEventType.Impression);
        store.CurrentRequestFormEvents.Should().HaveCount(1);
        store.CurrentRequestFormEvents[0].EventKey.Should().Be(eventKey);
        auditor.CallCount.Should().Be(1);
        auditor.LastEventKey.Should().Be(eventKey);
        auditor.LastEventType.Should().Be(AnalyzerFormEventType.Impression);
    }

    [Fact]
    public async Task AuditEmittedOnceOnSuccess_ZeroOnRejection()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor);

        var act = async () =>
            await handler.HandleAsync(
                NewImpression(Guid.NewGuid()) with { FormKey = Guid.Empty },
                TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>();
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestFormEvents.Should().BeEmpty();
        auditor.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(0, 2)] // Impression → FormImpression
    [InlineData(1, 1)] // Start → CustomEvent
    [InlineData(2, 1)] // Success → CustomEvent
    public async Task SessionActivityDispatch(byte eventTypeRaw, int expectedKindRaw)
    {
        var eventType = (AnalyzerFormEventType)eventTypeRaw;
        var expectedKind = (SessionActivityKind)expectedKindRaw;
        var handler = NewHandler(out var resolver, out _, out _, out _);
        var command = eventType switch
        {
            AnalyzerFormEventType.Impression => NewImpression(Guid.NewGuid()),
            AnalyzerFormEventType.Start => NewImpression(Guid.NewGuid()) with
            {
                EventType = AnalyzerFormEventType.Start,
                ElapsedMsFromImpression = 100,
            },
            AnalyzerFormEventType.Success => NewImpression(Guid.NewGuid()) with
            {
                EventType = AnalyzerFormEventType.Success,
                ElapsedMsFromStart = 2000,
            },
            _ => throw new InvalidOperationException(),
        };

        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        resolver.LastActivityKind.Should().Be(expectedKind);
    }

    private static AnalyzerFormEventCaptureHandler NewHandler(
        out FakeResolver resolver,
        out FakeRepository repo,
        out AnalyticsEventStateStore store,
        out FakeAuditor auditor)
    {
        resolver = new FakeResolver();
        repo = new FakeRepository();
        store = new AnalyticsEventStateStore();
        auditor = new FakeAuditor();
        return new AnalyzerFormEventCaptureHandler(
            resolver, repo, store, auditor,
            NullLogger<AnalyzerFormEventCaptureHandler>.Instance);
    }

    private static AnalyzerFormEventCapture NewImpression(Guid visitor) =>
        new(
            Actor: visitor == Guid.Empty
                ? new VisitorIdentity(IsAvailable: false, Key: Guid.Empty, Oid: null, Upn: null, IsAnonymized: false)
                : NewIdentity(visitor),
            FormKey: Guid.NewGuid(),
            ContentKey: Guid.NewGuid(),
            EventType: AnalyzerFormEventType.Impression,
            ElapsedMsFromImpression: null,
            ElapsedMsFromStart: null,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);

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

    private sealed class FakeRepository : IAnalyzerFormEventRepository
    {
        public int InsertCalls { get; private set; }
        public AnalyzerFormEventDto? LastInsert { get; private set; }

        public Task InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = dto;
            return Task.CompletedTask;
        }

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UnclosedStartTuple>> ListUnclosedStartsForSessionsAsync(
            IReadOnlyCollection<Guid> sessionKeys,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UnclosedStartTuple>>(Array.Empty<UnclosedStartTuple>());

        public Task InsertAbandonsBulkAsync(
            IReadOnlyList<AnalyzerFormEventDto> abandons,
            CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeAuditor : IAnalyzerFormEventAuditor
    {
        public int CallCount { get; private set; }
        public Guid LastEventKey { get; private set; }
        public AnalyzerFormEventType LastEventType { get; private set; }

        public void Audit(
            VisitorIdentity actor,
            Guid eventKey,
            Guid formKey,
            AnalyzerFormEventType eventType,
            DateTimeOffset receivedUtc)
        {
            CallCount++;
            LastEventKey = eventKey;
            LastEventType = eventType;
        }
    }
}
