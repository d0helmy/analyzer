using Analyzer.Analytics;
using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.CustomEvents.Infrastructure.Persistence;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 / T016 — orchestrator unit tests for
/// <see cref="CustomEventCaptureHandler"/>. Asserts the four steps
/// (resolver → repository.InsertAsync → state-store.AppendCustomEvent →
/// auditor.Audit) run in order; verifies the receiptKey wiring (null
/// when no in-request receipt; populated when CurrentRequestReceipt is
/// non-null — US1 AS6).
/// </summary>
public sealed class CustomEventCaptureHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Happy_path_runs_resolver_repository_state_store_auditor_in_order()
    {
        var visitor = Guid.NewGuid();
        var sessionKey = Guid.NewGuid();
        var resolver = new FakeResolver { NextSessionKey = sessionKey };
        var repo = new FakeRepository();
        var store = new AnalyticsEventStateStore();
        var auditor = new FakeAuditor();
        var handler = NewHandler(resolver, repo, store, auditor);

        var command = NewCommand(visitor);

        var eventKey = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        eventKey.Should().NotBe(Guid.Empty);
        resolver.CallCount.Should().Be(1);
        resolver.LastActivityKind.Should().Be(SessionActivityKind.CustomEvent);
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.EventKey.Should().Be(eventKey);
        repo.LastInsert.SessionKey.Should().Be(sessionKey);
        repo.LastInsert.VisitorProfileKey.Should().Be(visitor);
        repo.LastInsert.Category.Should().Be(command.Category);
        repo.LastInsert.ReceivedUtc.Should().Be(command.ReceivedUtc);
        store.CurrentRequestCustomEvents.Should().HaveCount(1);
        store.CurrentRequestCustomEvents[0].EventKey.Should().Be(eventKey);
        auditor.CallCount.Should().Be(1);
        auditor.LastEventKey.Should().Be(eventKey);
        auditor.LastActor.Key.Should().Be(visitor);
    }

    [Fact]
    public async Task ReceiptKey_is_null_when_no_in_request_receipt()
    {
        var visitor = Guid.NewGuid();
        var resolver = new FakeResolver();
        var repo = new FakeRepository();
        var store = new AnalyticsEventStateStore();
        var auditor = new FakeAuditor();
        var handler = NewHandler(resolver, repo, store, auditor);

        await handler.HandleAsync(NewCommand(visitor), TestContext.Current.CancellationToken);

        repo.LastInsert!.ReceiptKey.Should().BeNull();
        store.CurrentRequestCustomEvents[0].ReceiptKey.Should().BeNull();
    }

    [Fact]
    public async Task ReceiptKey_populated_when_state_store_has_current_receipt()
    {
        var visitor = Guid.NewGuid();
        var receiptId = Guid.NewGuid();
        var resolver = new FakeResolver();
        var repo = new FakeRepository();
        var store = new AnalyticsEventStateStore();
        store.SetCurrentReceipt(new AnalyticsEventReceipt(
            Id: receiptId,
            PageviewKey: Guid.NewGuid(),
            VisitorProfileKey: visitor,
            ReceivedUtc: T0));
        var auditor = new FakeAuditor();
        var handler = NewHandler(resolver, repo, store, auditor);

        await handler.HandleAsync(NewCommand(visitor), TestContext.Current.CancellationToken);

        repo.LastInsert!.ReceiptKey.Should().Be(receiptId);
        store.CurrentRequestCustomEvents[0].ReceiptKey.Should().Be(receiptId);
    }

    private static CustomEventCaptureHandler NewHandler(
        FakeResolver resolver,
        FakeRepository repo,
        AnalyticsEventStateStore store,
        FakeAuditor auditor) =>
        new(resolver, repo, store, auditor, NullLogger<CustomEventCaptureHandler>.Instance);

    private static CustomEventCapture NewCommand(Guid visitor) =>
        new(
            Actor: new VisitorIdentity(
                IsAvailable: true,
                Key: visitor,
                Oid: "oid-1",
                Upn: "user@example.com",
                IsAnonymized: false),
            Category: "engagement",
            Action: "click",
            Label: "header-cta",
            Value: null,
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

    private sealed class FakeRepository : IAnalyzerCustomEventRepository
    {
        public int InsertCalls { get; private set; }
        public AnalyzerCustomEventDto? LastInsert { get; private set; }

        public Task InsertAsync(AnalyzerCustomEventDto dto, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = dto;
            return Task.CompletedTask;
        }

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuditor : ICustomEventAuditor
    {
        public int CallCount { get; private set; }
        public VisitorIdentity LastActor { get; private set; }
        public Guid LastEventKey { get; private set; }

        public void Audit(VisitorIdentity actor, Guid eventKey, string category, string action, DateTimeOffset receivedUtc)
        {
            CallCount++;
            LastActor = actor;
            LastEventKey = eventKey;
        }
    }
}
