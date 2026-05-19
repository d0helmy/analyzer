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
/// Slice 005 / T046 — orchestrator unit tests for
/// <see cref="AnalyzerFormFieldEventCaptureHandler"/>. Covers the
/// HadValue/EventType invariant + the 5 standard conformance items.
/// </summary>
public sealed class AnalyzerFormFieldEventCaptureHandlerTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RejectsEmptyVisitor()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewFocus(Guid.Empty);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RejectsHadValueOnFocus()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = NewFocus(Guid.NewGuid()) with { HadValue = true };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        var ex = (await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>()).Which;
        ex.PropertyName.Should().Be(nameof(AnalyzerFormFieldEventCapture.HadValue));
    }

    [Fact]
    public async Task RejectsMissingHadValueOnUnfocus()
    {
        var handler = NewHandler(out _, out _, out _, out _);
        var command = new AnalyzerFormFieldEventCapture(
            Actor: NewIdentity(Guid.NewGuid()),
            FormKey: Guid.NewGuid(),
            FieldKey: Guid.NewGuid(),
            EventType: AnalyzerFormFieldEventType.FieldUnfocus,
            HadValue: null,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>();
    }

    [Fact]
    public async Task HappyPathInsertsAndAppendsState_Focus()
    {
        var handler = NewHandler(out var resolver, out var repo, out var store, out var auditor);
        var visitor = Guid.NewGuid();
        var command = NewFocus(visitor);

        var eventKey = await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        eventKey.Should().NotBe(Guid.Empty);
        resolver.LastActivityKind.Should().Be(SessionActivityKind.CustomEvent);
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.EventType.Should().Be((byte)AnalyzerFormFieldEventType.FieldFocus);
        repo.LastInsert.HadValue.Should().BeNull();
        store.CurrentRequestFormFieldEvents.Should().HaveCount(1);
        auditor.CallCount.Should().Be(1);
        auditor.LastHadValue.Should().BeNull();
    }

    [Fact]
    public async Task HappyPathInsertsAndAppendsState_Unfocus_with_hadValue()
    {
        var handler = NewHandler(out _, out var repo, out var store, out var auditor);
        var visitor = Guid.NewGuid();
        var command = new AnalyzerFormFieldEventCapture(
            Actor: NewIdentity(visitor),
            FormKey: Guid.NewGuid(),
            FieldKey: Guid.NewGuid(),
            EventType: AnalyzerFormFieldEventType.FieldUnfocus,
            HadValue: true,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

        await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        repo.LastInsert!.HadValue.Should().BeTrue();
        store.CurrentRequestFormFieldEvents[0].HadValue.Should().BeTrue();
        auditor.LastHadValue.Should().BeTrue();
    }

    [Fact]
    public async Task NoAuditOnValidationFailure()
    {
        var handler = NewHandler(out _, out var repo, out _, out var auditor);
        var command = NewFocus(Guid.NewGuid()) with { FormKey = Guid.Empty };

        var act = async () =>
            await handler.HandleAsync(command, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<AnalyzerFormPayloadValidationException>();
        repo.InsertCalls.Should().Be(0);
        auditor.CallCount.Should().Be(0);
    }

    private static AnalyzerFormFieldEventCaptureHandler NewHandler(
        out FakeResolver resolver,
        out FakeRepository repo,
        out AnalyticsEventStateStore store,
        out FakeAuditor auditor)
    {
        resolver = new FakeResolver();
        repo = new FakeRepository();
        store = new AnalyticsEventStateStore();
        auditor = new FakeAuditor();
        return new AnalyzerFormFieldEventCaptureHandler(
            resolver, repo, store, auditor,
            NullLogger<AnalyzerFormFieldEventCaptureHandler>.Instance);
    }

    private static AnalyzerFormFieldEventCapture NewFocus(Guid visitor) =>
        new(
            Actor: visitor == Guid.Empty
                ? new VisitorIdentity(IsAvailable: false, Key: Guid.Empty, Oid: null, Upn: null, IsAnonymized: false)
                : NewIdentity(visitor),
            FormKey: Guid.NewGuid(),
            FieldKey: Guid.NewGuid(),
            EventType: AnalyzerFormFieldEventType.FieldFocus,
            HadValue: null,
            UserAgent: "UA/1",
            ReceivedUtc: T0);

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);

    private sealed class FakeResolver : IAnalyzerSessionResolver
    {
        public SessionActivityKind LastActivityKind { get; private set; }
        public ValueTask<SessionResolutionResult> ResolveAsync(
            Guid visitorProfileKey, string? userAgent, DateTimeOffset receivedUtc,
            SessionActivityKind activityKind, CancellationToken ct)
        {
            LastActivityKind = activityKind;
            var sessionKey = Guid.NewGuid();
            var projection = new AnalyticsSession(
                sessionKey, visitorProfileKey, receivedUtc, receivedUtc, null, 1, true);
            return ValueTask.FromResult(new SessionResolutionResult(sessionKey, projection));
        }
    }

    private sealed class FakeRepository : IAnalyzerFormFieldEventRepository
    {
        public int InsertCalls { get; private set; }
        public AnalyzerFormFieldEventDto? LastInsert { get; private set; }

        public Task InsertAsync(AnalyzerFormFieldEventDto dto, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = dto;
            return Task.CompletedTask;
        }

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeAuditor : IAnalyzerFormFieldEventAuditor
    {
        public int CallCount { get; private set; }
        public bool? LastHadValue { get; private set; }

        public void Audit(
            VisitorIdentity actor, Guid eventKey, Guid formKey, Guid fieldKey,
            AnalyzerFormFieldEventType eventType, bool? hadValue, DateTimeOffset receivedUtc)
        {
            CallCount++;
            LastHadValue = hadValue;
        }
    }
}
