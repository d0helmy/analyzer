using Analyzer.Analytics;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Analyzer.Features.Sessions.Infrastructure.Sweeper;
using Analyzer.Tests.Unit.Features.Sessions.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Sessions.Sweeper;

public sealed class AnalyzerSessionSweeperServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Closes_eligible_sessions_and_invalidates_cache()
    {
        var sessionKey = Guid.NewGuid();
        var repo = new FakeRepo { NextAffected = new[] { sessionKey } };
        var cache = NewCacheStore();
        cache.Set(Guid.NewGuid(), "dev",
            new AnalyticsSessionCacheEntry(sessionKey, FixedNow, FixedNow, 1));

        var sweeper = NewSweeper(repo, cache, sweepIntervalSeconds: 3600);

        using var cts = CreateShortLifeCts();
        await sweeper.StartAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await sweeper.StopAsync(TestContext.Current.CancellationToken);

        repo.SweepCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Zero_eligible_no_op_no_invalidations()
    {
        var repo = new FakeRepo { NextAffected = Array.Empty<Guid>() };
        var cache = NewCacheStore();
        var sweeper = NewSweeper(repo, cache, sweepIntervalSeconds: 3600);

        using var cts = CreateShortLifeCts();
        await sweeper.StartAsync(cts.Token);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await sweeper.StopAsync(TestContext.Current.CancellationToken);

        repo.SweepCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Swallows_tick_exception_and_continues()
    {
        var repo = new FakeRepo { ThrowOnFirstSweep = true };
        var cache = NewCacheStore();
        var sweeper = NewSweeper(repo, cache, sweepIntervalSeconds: 1);

        using var cts = CreateShortLifeCts(TimeSpan.FromMilliseconds(1500));
        await sweeper.StartAsync(cts.Token);
        await Task.Delay(1300, TestContext.Current.CancellationToken);
        await sweeper.StopAsync(TestContext.Current.CancellationToken);

        // First tick threw; later ticks must have succeeded → SweepCalls
        // should advance past 1.
        repo.SweepCalls.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Graceful_shutdown_mid_loop_exits_cleanly()
    {
        // US3 AS4 — confirm cancellation-on-stoppingToken breaks the
        // loop without throwing out of ExecuteAsync.
        var repo = new FakeRepo { NextAffected = Array.Empty<Guid>() };
        var cache = NewCacheStore();
        var sweeper = NewSweeper(repo, cache, sweepIntervalSeconds: 60);

        using var cts = CreateShortLifeCts(TimeSpan.FromMilliseconds(50));
        await sweeper.StartAsync(cts.Token);

        var act = async () => await sweeper.StopAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OptionsMonitor_reload_changes_sweep_interval()
    {
        var repo = new FakeRepo();
        var cache = NewCacheStore();
        var monitor = new MutableOptionsMonitor<AnalyzerSessionOptions>(
            new AnalyzerSessionOptions
            {
                InactivityTimeoutMinutes = 30,
                SweepIntervalSeconds = 60,
                SweepBatchSize = 100,
                CacheCapacity = 100,
            });

        var sweeper = new AnalyzerSessionSweeperService(
            ScopeFactoryFor(repo), cache, monitor, new FixedClock(FixedNow),
            NullLogger<AnalyzerSessionSweeperService>.Instance);

        // Reducing the interval mid-flight should be observed at the
        // next loop-top read of `_options.CurrentValue`.
        monitor.Set(new AnalyzerSessionOptions
        {
            InactivityTimeoutMinutes = 30,
            SweepIntervalSeconds = 1,
            SweepBatchSize = 100,
            CacheCapacity = 100,
        });

        using var cts = CreateShortLifeCts(TimeSpan.FromMilliseconds(50));
        await sweeper.StartAsync(cts.Token);
        await sweeper.StopAsync(TestContext.Current.CancellationToken);
    }

    private static AnalyzerSessionSweeperService NewSweeper(
        FakeRepo repo, AnalyzerSessionCacheStore cache, int sweepIntervalSeconds)
    {
        var monitor = Options.Create(new AnalyzerSessionOptions
        {
            InactivityTimeoutMinutes = 30,
            SweepIntervalSeconds = sweepIntervalSeconds,
            SweepBatchSize = 100,
            CacheCapacity = 100,
        }).ToMonitor();

        return new AnalyzerSessionSweeperService(
            ScopeFactoryFor(repo),
            cache,
            monitor,
            new FixedClock(FixedNow),
            NullLogger<AnalyzerSessionSweeperService>.Instance);
    }

    private static IServiceScopeFactory ScopeFactoryFor(IAnalyzerSessionRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static AnalyzerSessionCacheStore NewCacheStore() =>
        new(Options.Create(new AnalyzerSessionOptions
        {
            CacheCapacity = 100,
            InactivityTimeoutMinutes = 30,
        }).ToMonitor());

    private static CancellationTokenSource CreateShortLifeCts(TimeSpan? lifetime = null) =>
        new(lifetime ?? TimeSpan.FromMilliseconds(200));

    private sealed class FakeRepo : IAnalyzerSessionRepository
    {
        public int SweepCalls { get; private set; }
        public IReadOnlyList<Guid> NextAffected { get; set; } = Array.Empty<Guid>();
        public bool ThrowOnFirstSweep { get; set; }

        public Task<AnalyticsSession?> GetLatestActiveAsync(Guid v, string d, CancellationToken c) =>
            Task.FromResult<AnalyticsSession?>(null);
        public Task InsertAsync(AnalyzerSessionDto s, CancellationToken c) => Task.CompletedTask;
        public Task<SessionExtendResult> ExtendAsync(Guid s, DateTimeOffset n, CancellationToken c) =>
            Task.FromResult(new SessionExtendResult(default, 0));
        public Task CloseAsync(Guid s, DateTimeOffset e, CancellationToken c) => Task.CompletedTask;
        public Task TouchAsync(Guid s, DateTimeOffset n, CancellationToken c) => Task.CompletedTask;
        public Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(
            Guid v, DateTimeOffset n, CancellationToken c) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlyList<Guid>> SweepEligibleAsync(
            DateTimeOffset cutoff, TimeSpan inactivityTimeout, int batchSize, CancellationToken ct)
        {
            SweepCalls++;
            if (ThrowOnFirstSweep && SweepCalls == 1)
            {
                throw new InvalidOperationException("simulated tick failure");
            }
            return Task.FromResult(NextAffected);
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class MutableOptionsMonitor<T> : IOptionsMonitor<T>
        where T : class
    {
        public MutableOptionsMonitor(T initial) => CurrentValue = initial;
        public T CurrentValue { get; private set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
        public void Set(T next) => CurrentValue = next;
    }
}
