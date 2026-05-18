using Analyzer.Analytics;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Application.Anonymization;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Sessions.Application.Anonymization;

public sealed class AnalyzerSessionCascadeStepTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SoftAnonymise_calls_repo_with_visitor_key_and_clock_now()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepo();
        var step = NewStep(repo);

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.Calls.Should().Be(1);
        repo.LastVisitor.Should().Be(visitor);
        repo.LastNowUtc.Should().Be(FixedNow);
    }

    [Fact]
    public async Task Cache_invalidate_runs_after_repository_success()
    {
        var visitor = Guid.NewGuid();
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var repo = new FakeRepo { NextAffected = new[] { sessionA, sessionB } };

        var cache = NewCacheStore();
        cache.Set(visitor, "deviceA",
            new AnalyticsSessionCacheEntry(sessionA, FixedNow, FixedNow, 1));
        cache.Set(visitor, "deviceB",
            new AnalyticsSessionCacheEntry(sessionB, FixedNow, FixedNow, 1));

        var step = new AnalyzerSessionCascadeStep(
            repo, cache, new FixedClock(FixedNow),
            NullLogger<AnalyzerSessionCascadeStep>.Instance);

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        cache.TryGet(visitor, "deviceA", out _).Should().BeFalse();
        cache.TryGet(visitor, "deviceB", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Zero_row_visitor_is_no_op_no_error()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepo { NextAffected = Array.Empty<Guid>() };
        var step = NewStep(repo);

        var act = async () => await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Empty_visitor_key_short_circuits_no_repo_call()
    {
        var repo = new FakeRepo();
        var step = NewStep(repo);

        await step.ExecuteAsync(Guid.Empty, TestContext.Current.CancellationToken);

        repo.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Idempotent_rerun_returns_empty_no_error()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepo
        {
            NextAffected = new[] { Guid.NewGuid() },
        };
        var step = NewStep(repo);
        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.NextAffected = Array.Empty<Guid>();

        var act = async () => await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
        repo.Calls.Should().Be(2);
    }

    private static AnalyzerSessionCascadeStep NewStep(FakeRepo repo)
    {
        var cache = NewCacheStore();
        return new AnalyzerSessionCascadeStep(
            repo, cache, new FixedClock(FixedNow),
            NullLogger<AnalyzerSessionCascadeStep>.Instance);
    }

    private static AnalyzerSessionCacheStore NewCacheStore() =>
        new(Options.Create(new AnalyzerSessionOptions
        {
            CacheCapacity = 100,
            InactivityTimeoutMinutes = 30,
        }).ToMonitor());

    private sealed class FakeRepo : IAnalyzerSessionRepository
    {
        public int Calls { get; private set; }
        public Guid LastVisitor { get; private set; }
        public DateTimeOffset LastNowUtc { get; private set; }
        public IReadOnlyList<Guid> NextAffected { get; set; } = Array.Empty<Guid>();

        public Task<AnalyticsSession?> GetLatestActiveAsync(Guid v, string d, CancellationToken c) =>
            Task.FromResult<AnalyticsSession?>(null);
        public Task InsertAsync(AnalyzerSessionDto s, CancellationToken c) => Task.CompletedTask;
        public Task<SessionExtendResult> ExtendAsync(Guid s, DateTimeOffset n, CancellationToken c) =>
            Task.FromResult(new SessionExtendResult(default, 0));
        public Task CloseAsync(Guid s, DateTimeOffset e, CancellationToken c) => Task.CompletedTask;
        public Task<IReadOnlyList<Guid>> SweepEligibleAsync(
            DateTimeOffset c1, TimeSpan i, int b, CancellationToken c2) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(
            Guid visitorProfileKey, DateTimeOffset nowUtc, CancellationToken ct)
        {
            Calls++;
            LastVisitor = visitorProfileKey;
            LastNowUtc = nowUtc;
            return Task.FromResult(NextAffected);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
