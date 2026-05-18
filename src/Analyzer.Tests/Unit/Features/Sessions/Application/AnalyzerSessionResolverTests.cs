using System.Data.Common;
using Analyzer.Analytics;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Analyzer.Features.Sessions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Sessions.Application;

public sealed class AnalyzerSessionResolverTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Cache_hit_fresh_extends_via_repo()
    {
        var visitor = Guid.NewGuid();
        var sessionKey = Guid.NewGuid();
        var repo = new FakeRepository();
        var (resolver, cache) = NewResolver(repo);

        cache.Set(visitor, DeviceKeyHasher.Compute("UA"),
            new AnalyticsSessionCacheEntry(sessionKey, T0, T0, 1));

        // Stage post-extend column values.
        repo.NextExtend = new SessionExtendResult(T0, 2);

        var result = await resolver.ResolveAsync(visitor, "UA", T0.AddMinutes(5), TestContext.Current.CancellationToken);

        result.SessionKey.Should().Be(sessionKey);
        result.Projection.PageviewCount.Should().Be(2);
        result.Projection.LastActivityUtc.Should().Be(T0.AddMinutes(5));
        repo.ExtendCalls.Should().Be(1);
        repo.InsertCalls.Should().Be(0);
        repo.GetLatestCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cache_miss_DB_hit_fresh_extends()
    {
        var visitor = Guid.NewGuid();
        var sessionKey = Guid.NewGuid();
        var repo = new FakeRepository
        {
            NextGetLatest = new AnalyticsSession(
                SessionKey: sessionKey,
                VisitorProfileKey: visitor,
                StartUtc: T0,
                LastActivityUtc: T0,
                EndUtc: null,
                PageviewCount: 1,
                IsActive: true),
            NextExtend = new SessionExtendResult(T0, 2),
        };
        var (resolver, _) = NewResolver(repo);

        var result = await resolver.ResolveAsync(visitor, "UA", T0.AddMinutes(5), TestContext.Current.CancellationToken);

        result.SessionKey.Should().Be(sessionKey);
        result.Projection.PageviewCount.Should().Be(2);
        repo.GetLatestCalls.Should().Be(1);
        repo.ExtendCalls.Should().Be(1);
        repo.InsertCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cache_hit_stale_closes_then_opens_new()
    {
        var visitor = Guid.NewGuid();
        var staleSession = Guid.NewGuid();
        var repo = new FakeRepository();
        var (resolver, cache) = NewResolver(repo, inactivityMinutes: 30);

        cache.Set(visitor, DeviceKeyHasher.Compute("UA"),
            new AnalyticsSessionCacheEntry(staleSession, T0, T0, 1));

        // 30+ minute gap → stale.
        var result = await resolver.ResolveAsync(visitor, "UA", T0.AddMinutes(61), TestContext.Current.CancellationToken);

        result.SessionKey.Should().NotBe(staleSession);
        result.Projection.PageviewCount.Should().Be(1);
        repo.CloseCalls.Should().Be(1);
        repo.LastCloseSessionKey.Should().Be(staleSession);
        repo.InsertCalls.Should().Be(1);
    }

    [Fact]
    public async Task Cache_miss_no_DB_row_opens_new()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepository { NextGetLatest = null };
        var (resolver, _) = NewResolver(repo);

        var result = await resolver.ResolveAsync(visitor, "UA", T0, TestContext.Current.CancellationToken);

        result.Projection.PageviewCount.Should().Be(1);
        result.Projection.IsActive.Should().BeTrue();
        repo.InsertCalls.Should().Be(1);
        repo.LastInsert!.VisitorProfileKey.Should().Be(visitor);
        repo.LastInsert.DeviceKey.Should().Be(DeviceKeyHasher.Compute("UA"));
    }

    [Fact]
    public async Task Insert_collision_retries_via_GetLatest_and_extends()
    {
        var visitor = Guid.NewGuid();
        var winnerSessionKey = Guid.NewGuid();
        var repo = new FakeRepository
        {
            ThrowOnInsert = new FakeDbException("23505"),
            NextExtend = new SessionExtendResult(T0, 2),
        };

        // Sequencing: GetLatest returns null pre-insert (so resolver tries
        // to open), then returns the winner after the collision.
        repo.QueueGetLatest(null);
        repo.QueueGetLatest(new AnalyticsSession(
            SessionKey: winnerSessionKey,
            VisitorProfileKey: visitor,
            StartUtc: T0,
            LastActivityUtc: T0,
            EndUtc: null,
            PageviewCount: 1,
            IsActive: true));

        var (resolver, _) = NewResolver(repo);

        var result = await resolver.ResolveAsync(visitor, "UA", T0, TestContext.Current.CancellationToken);

        result.SessionKey.Should().Be(winnerSessionKey);
        result.Projection.PageviewCount.Should().Be(2);
        repo.InsertCalls.Should().Be(1);
        repo.GetLatestCalls.Should().Be(2);
        repo.ExtendCalls.Should().Be(1);
    }

    [Fact]
    public async Task Null_UA_hashes_to_sentinel_and_resolution_proceeds()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepository();
        var (resolver, _) = NewResolver(repo);

        var result = await resolver.ResolveAsync(visitor, null, T0, TestContext.Current.CancellationToken);

        result.Projection.PageviewCount.Should().Be(1);
        repo.LastInsert!.DeviceKey.Should().Be(DeviceKeyHasher.Compute(null));
        repo.LastInsert.DeviceKey.Should().HaveLength(16);
    }

    [Fact]
    public async Task IOptionsMonitor_reload_changes_inactivity_window()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepository();
        var monitor = new MutableOptionsMonitor<AnalyzerSessionOptions>(
            new AnalyzerSessionOptions { InactivityTimeoutMinutes = 30, CacheCapacity = 100 });
        var cache = new AnalyzerSessionCacheStore(monitor);
        var resolver = new AnalyzerSessionResolver(
            repo, cache, monitor,
            NullLogger<AnalyzerSessionResolver>.Instance);

        // First call: cache miss → opens.
        await resolver.ResolveAsync(visitor, "UA", T0, TestContext.Current.CancellationToken);
        repo.InsertCalls.Should().Be(1);

        // 20 minutes later: still fresh under 30-min timeout → extend.
        repo.NextExtend = new SessionExtendResult(T0, 2);
        await resolver.ResolveAsync(visitor, "UA", T0.AddMinutes(20), TestContext.Current.CancellationToken);
        repo.ExtendCalls.Should().Be(1);
        repo.CloseCalls.Should().Be(0);

        // Reduce timeout to 5 minutes; 20-minute-old cache entry now stale.
        monitor.Set(new AnalyzerSessionOptions { InactivityTimeoutMinutes = 5, CacheCapacity = 100 });

        await resolver.ResolveAsync(visitor, "UA", T0.AddMinutes(40), TestContext.Current.CancellationToken);
        repo.CloseCalls.Should().Be(1);
    }

    private static (AnalyzerSessionResolver resolver, AnalyzerSessionCacheStore cache)
        NewResolver(FakeRepository repo, int inactivityMinutes = 30)
    {
        var monitor = Options.Create(new AnalyzerSessionOptions
        {
            InactivityTimeoutMinutes = inactivityMinutes,
            CacheCapacity = 100,
        }).ToMonitor();
        var cache = new AnalyzerSessionCacheStore(monitor);
        var resolver = new AnalyzerSessionResolver(
            repo, cache, monitor,
            NullLogger<AnalyzerSessionResolver>.Instance);
        return (resolver, cache);
    }

    /// <summary>
    /// In-process fake of <see cref="IAnalyzerSessionRepository"/>.
    /// </summary>
    private sealed class FakeRepository : IAnalyzerSessionRepository
    {
        private readonly Queue<AnalyticsSession?> _queuedGetLatest = new();

        public int GetLatestCalls { get; private set; }
        public int InsertCalls { get; private set; }
        public int ExtendCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public AnalyzerSessionDto? LastInsert { get; private set; }
        public Guid LastCloseSessionKey { get; private set; }

        public AnalyticsSession? NextGetLatest { get; set; }
        public SessionExtendResult NextExtend { get; set; } = new(T0, 1);
        public Exception? ThrowOnInsert { get; set; }

        public void QueueGetLatest(AnalyticsSession? row) => _queuedGetLatest.Enqueue(row);

        public Task<AnalyticsSession?> GetLatestActiveAsync(
            Guid visitorProfileKey, string deviceKey, CancellationToken ct)
        {
            GetLatestCalls++;
            if (_queuedGetLatest.Count > 0)
            {
                return Task.FromResult(_queuedGetLatest.Dequeue());
            }
            return Task.FromResult(NextGetLatest);
        }

        public Task InsertAsync(AnalyzerSessionDto session, CancellationToken ct)
        {
            InsertCalls++;
            LastInsert = session;
            if (ThrowOnInsert is { } ex)
            {
                ThrowOnInsert = null;
                throw ex;
            }
            return Task.CompletedTask;
        }

        public Task<SessionExtendResult> ExtendAsync(
            Guid sessionKey, DateTimeOffset newLastActivityUtc, CancellationToken ct)
        {
            ExtendCalls++;
            return Task.FromResult(NextExtend);
        }

        public Task CloseAsync(
            Guid sessionKey, DateTimeOffset logicalCloseUtc, CancellationToken ct)
        {
            CloseCalls++;
            LastCloseSessionKey = sessionKey;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(
            Guid visitorProfileKey, DateTimeOffset nowUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<IReadOnlyList<Guid>> SweepEligibleAsync(
            DateTimeOffset cutoff, TimeSpan inactivityTimeout, int batchSize, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private sealed class FakeDbException : DbException
    {
        public FakeDbException(string sqlState) : base("unique violation") => SqlState = sqlState;
        public override string? SqlState { get; }
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
