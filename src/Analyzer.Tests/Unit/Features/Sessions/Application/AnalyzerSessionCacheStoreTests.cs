using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Sessions.Application;

public sealed class AnalyzerSessionCacheStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryGet_returns_false_when_no_entry()
    {
        using var store = NewStore();

        store.TryGet(Guid.NewGuid(), "dev", out _).Should().BeFalse();
    }

    [Fact]
    public void Set_then_TryGet_round_trips_entry()
    {
        using var store = NewStore();
        var visitor = Guid.NewGuid();
        var entry = NewEntry(Guid.NewGuid(), Now);

        store.Set(visitor, "dev", entry);

        store.TryGet(visitor, "dev", out var got).Should().BeTrue();
        got.SessionKey.Should().Be(entry.SessionKey);
        got.LastActivityUtc.Should().Be(Now);
        got.PageviewCount.Should().Be(entry.PageviewCount);
    }

    [Fact]
    public void Invalidate_removes_entry()
    {
        using var store = NewStore();
        var visitor = Guid.NewGuid();
        store.Set(visitor, "dev", NewEntry(Guid.NewGuid(), Now));

        store.Invalidate(visitor, "dev");

        store.TryGet(visitor, "dev", out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidateBySessionKey_removes_matching_entry()
    {
        using var store = NewStore();
        var visitor = Guid.NewGuid();
        var sessionKey = Guid.NewGuid();
        store.Set(visitor, "deviceA", NewEntry(sessionKey, Now));
        store.Set(visitor, "deviceB", NewEntry(Guid.NewGuid(), Now));

        store.InvalidateBySessionKey(sessionKey);

        store.TryGet(visitor, "deviceA", out _).Should().BeFalse();
        store.TryGet(visitor, "deviceB", out _).Should().BeTrue();
    }

    [Fact]
    public void InvalidateByVisitorKey_removes_all_devices_for_visitor()
    {
        using var store = NewStore();
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        store.Set(visitorA, "deviceA", NewEntry(Guid.NewGuid(), Now));
        store.Set(visitorA, "deviceB", NewEntry(Guid.NewGuid(), Now));
        store.Set(visitorB, "deviceA", NewEntry(Guid.NewGuid(), Now));

        store.InvalidateByVisitorKey(visitorA);

        store.TryGet(visitorA, "deviceA", out _).Should().BeFalse();
        store.TryGet(visitorA, "deviceB", out _).Should().BeFalse();
        store.TryGet(visitorB, "deviceA", out _).Should().BeTrue();
    }

    [Fact]
    public void Concurrent_set_and_get_safe()
    {
        using var store = NewStore(capacity: 1000);
        var visitorKeys = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();

        Parallel.For(0, 1000, i =>
        {
            var visitor = visitorKeys[i % visitorKeys.Length];
            store.Set(visitor, $"dev-{i % 10}", NewEntry(Guid.NewGuid(), Now));
            store.TryGet(visitor, $"dev-{i % 10}", out _);
        });
    }

    private static AnalyzerSessionCacheStore NewStore(int capacity = 10) =>
        new(Options.Create(new AnalyzerSessionOptions
        {
            CacheCapacity = capacity,
            InactivityTimeoutMinutes = 30,
        }).ToMonitor());

    private static AnalyticsSessionCacheEntry NewEntry(Guid sessionKey, DateTimeOffset now) =>
        new(SessionKey: sessionKey,
            StartUtc: now,
            LastActivityUtc: now,
            PageviewCount: 1);
}

internal static class OptionsTestExtensions
{
    /// <summary>
    /// Wrap an <see cref="IOptions{T}"/> in an
    /// <see cref="IOptionsMonitor{T}"/> stub for tests that don't
    /// exercise the reload path.
    /// </summary>
    public static IOptionsMonitor<T> ToMonitor<T>(this IOptions<T> options)
        where T : class => new TestOptionsMonitor<T>(options.Value);

    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; private set; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
