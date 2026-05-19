using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Search.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Search.Application;

/// <summary>
/// Slice 007 / T022 — orchestrator unit tests for
/// <see cref="AnalyzerSearchEventCaptureHandler"/>.
/// </summary>
public sealed class AnalyzerSearchEventCaptureHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectsUnavailableActor()
    {
        var handler = NewHandler(out _, out _, out _, out _, out _, out _);
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
        var handler = NewHandler(out _, out _, out _, out _, out _, out _);
        var command = NewCommand(Guid.Empty);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RejectsEmptyRawQuery()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor, out _, out _);
        var command = NewCommand(Guid.NewGuid()) with { RawQuery = "   " };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.RawQuery));
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestSearchEvents.Should().BeEmpty();
        auditor.AcceptedCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsEmptyNormalisedQuery()
    {
        // A custom IAnalyzerSearchQueryNormaliser that returns empty
        // for a valid input — handler must reject (defence in depth).
        var emptyNormaliser = new EmptyNormaliser();
        var handler = NewHandler(emptyNormaliser, out _, out var repo, out var store, out var auditor, out _);
        var command = NewCommand(Guid.NewGuid()) with { RawQuery = "valid" };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.RawQuery));
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestSearchEvents.Should().BeEmpty();
        auditor.AcceptedCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsNegativeResultCount()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor, out _, out _);
        var command = NewCommand(Guid.NewGuid()) with { ResultCount = -1 };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.ResultCount));
        repo.InsertCalls.Should().Be(0);
        store.CurrentRequestSearchEvents.Should().BeEmpty();
        auditor.AcceptedCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsEmptyPageviewKey()
    {
        var handler = NewHandler(out _, out var repo, out _, out _, out _, out _);
        var command = NewCommand(Guid.NewGuid()) with { PageviewKey = Guid.Empty };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.PageviewKey));
        repo.InsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsPageviewBelongingToDifferentVisitor()
    {
        var visitor = Guid.NewGuid();
        var otherVisitor = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var handler = NewHandler(out _, out var repo, out _, out var auditor, out _, out _);
        repo.PageviewBindings[pageviewKey] = new PageviewBinding(otherVisitor, contentKey);
        var command = NewCommand(visitor) with { PageviewKey = pageviewKey };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.PageviewKey));
        ex.Message.Should().Contain("does not belong to the resolved visitor");
        repo.InsertCalls.Should().Be(0);
        auditor.AcceptedCalls.Should().Be(0);
    }

    [Fact]
    public async Task RejectsNonExistentPageview()
    {
        var visitor = Guid.NewGuid();
        var handler = NewHandler(out _, out var repo, out _, out _, out _, out _);
        // No entry in PageviewBindings — repo returns null.
        var command = NewCommand(visitor);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerSearchPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerSearchEventCapture.PageviewKey));
        ex.Message.Should().Contain("does not exist");
        repo.InsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task HappyPathInsertsAndAppendsStateAndAudits()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        var handler = NewHandler(out var resolver, out var repo, out var store, out var auditor, out var normaliser, out _);
        repo.PageviewBindings[pageviewKey] = new PageviewBinding(visitor, contentKey);
        normaliser.LastInput = null;
        var command = NewCommand(visitor) with
        {
            PageviewKey = pageviewKey,
            RawQuery = "Design System",
            ResultCount = 12,
        };

        var projection = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        projection.EventKey.Should().NotBe(Guid.Empty);
        projection.VisitorProfileKey.Should().Be(visitor);
        projection.PageviewKey.Should().Be(pageviewKey);
        projection.ContentKey.Should().Be(contentKey, "server-set from the pageview binding, not the client");
        projection.RawQuery.Should().Be("Design System");
        projection.NormalisedQuery.Should().Be("design system");
        projection.ResultCount.Should().Be(12);
        resolver.CallCount.Should().Be(1);
        resolver.LastActivityKind.Should().Be(SessionActivityKind.SearchEvent,
            "search submissions are intentional engagement — Touch, not Extend");
        normaliser.LastInput.Should().Be("Design System");
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.EventKey.Should().Be(projection.EventKey);
        repo.LastInsert.NormalisedQuery.Should().Be("design system");
        repo.LastInsert.ContentKey.Should().Be(contentKey);
        store.CurrentRequestSearchEvents.Should().HaveCount(1);
        store.CurrentRequestSearchEvents[0].EventKey.Should().Be(projection.EventKey);
        auditor.AcceptedCalls.Should().Be(1);
        auditor.LastEventKey.Should().Be(projection.EventKey);
        auditor.LastResultCount.Should().Be(12);
    }

    private static AnalyzerSearchEventCaptureHandler NewHandler(
        out FakeResolver resolver,
        out FakeRepository repo,
        out AnalyticsEventStateStore store,
        out FakeAuditor auditor,
        out FakeNormaliser normaliser,
        out Guid sessionKey)
    {
        normaliser = new FakeNormaliser();
        return NewHandler(normaliser, out resolver, out repo, out store, out auditor, out sessionKey);
    }

    private static AnalyzerSearchEventCaptureHandler NewHandler(
        IAnalyzerSearchQueryNormaliser normaliser,
        out FakeResolver resolver,
        out FakeRepository repo,
        out AnalyticsEventStateStore store,
        out FakeAuditor auditor,
        out Guid sessionKey)
    {
        resolver = new FakeResolver();
        repo = new FakeRepository();
        store = new AnalyticsEventStateStore();
        auditor = new FakeAuditor();
        sessionKey = resolver.NextSessionKey;
        return new AnalyzerSearchEventCaptureHandler(
            normaliser, resolver, repo, store, auditor,
            NullLogger<AnalyzerSearchEventCaptureHandler>.Instance);
    }

    private static AnalyzerSearchEventCapture NewCommand(Guid visitorKey) =>
        new(
            Actor: visitorKey == Guid.Empty
                ? new VisitorIdentity(IsAvailable: true, Key: Guid.Empty, Oid: null, Upn: null, IsAnonymized: false)
                : new VisitorIdentity(IsAvailable: true, Key: visitorKey, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false),
            PageviewKey: Guid.NewGuid(),
            ContentKey: Guid.Empty,
            RawQuery: "Hello World",
            ResultCount: 3,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

    private sealed class FakeNormaliser : IAnalyzerSearchQueryNormaliser
    {
        public string? LastInput { get; set; }
        public string Normalise(string rawQuery)
        {
            LastInput = rawQuery;
            return rawQuery.Trim().ToLowerInvariant();
        }
    }

    private sealed class EmptyNormaliser : IAnalyzerSearchQueryNormaliser
    {
        public string Normalise(string rawQuery) => string.Empty;
    }

    private sealed class FakeResolver : IAnalyzerSessionResolver
    {
        public int CallCount { get; private set; }
        public SessionActivityKind LastActivityKind { get; private set; }
        public Guid NextSessionKey { get; } = Guid.NewGuid();

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

    private sealed class FakeRepository : IAnalyzerSearchEventRepository
    {
        public int InsertCalls { get; private set; }
        public AnalyzerSearchEventDto? LastInsert { get; private set; }
        public Dictionary<Guid, PageviewBinding> PageviewBindings { get; } = new();

        public Task InsertAsync(AnalyzerSearchEventDto dto, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = dto;
            return Task.CompletedTask;
        }

        public Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.FromResult(0);

        public Task<PageviewBinding?> ResolvePageviewBindingAsync(Guid pageviewKey, CancellationToken ct) =>
            Task.FromResult(PageviewBindings.TryGetValue(pageviewKey, out var binding)
                ? (PageviewBinding?)binding
                : null);
    }

    private sealed class FakeAuditor : IAnalyzerSearchEventAuditor
    {
        public int AcceptedCalls { get; private set; }
        public Guid LastEventKey { get; private set; }
        public Guid LastPageviewKey { get; private set; }
        public int LastResultCount { get; private set; }

        public void AuditAccepted(
            VisitorIdentity actor, Guid eventKey, Guid pageviewKey,
            int resultCount, DateTimeOffset receivedUtc)
        {
            AcceptedCalls++;
            LastEventKey = eventKey;
            LastPageviewKey = pageviewKey;
            LastResultCount = resultCount;
        }
    }
}
