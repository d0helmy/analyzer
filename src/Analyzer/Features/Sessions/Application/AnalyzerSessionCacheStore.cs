using System.Collections.Concurrent;
using Analyzer.Features.Sessions.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Sessions.Application;

/// <summary>
/// Slice 003 — singleton bounded LRU cache of active sessions keyed by
/// <c>(visitorProfileKey, deviceKey)</c>. Backed by
/// <c>Microsoft.Extensions.Caching.Memory.MemoryCache</c> with
/// <c>SizeLimit = options.CacheCapacity</c> and <c>Size = 1</c> per
/// entry — concurrent-by-design (lock-free reads, lock-on-write), no
/// application-level locking required (lesson #44).
/// </summary>
/// <remarks>
/// <para>
/// Cache capacity is captured at construction; runtime changes require
/// a host restart (spec FR-008 qualifier). Sliding expiration is
/// <c>inactivityTimeout * 2</c> — an entry that's been silent longer
/// than the inactivity timeout is no longer authoritative, so falling
/// back to a DB read at that point is correct, not a performance
/// penalty.
/// </para>
/// <para>
/// Cross-instance coherence is NOT maintained (singleton per process).
/// Two Umbraco hosts behind a load balancer hold independent caches;
/// the DB-level partial unique index
/// <c>UX_analyzerSession_active_visitor_device</c> serialises the rare
/// collision case.
/// </para>
/// </remarks>
internal sealed class AnalyzerSessionCacheStore : IDisposable
{
    private readonly IOptionsMonitor<AnalyzerSessionOptions> _options;
    private readonly MemoryCache _cache;
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    public AnalyzerSessionCacheStore(IOptionsMonitor<AnalyzerSessionOptions> options)
    {
        _options = options;
        var capacity = Math.Max(1, options.CurrentValue.CacheCapacity);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = capacity });
    }

    /// <summary>Cache key shape: <c>"{visitorKey:N}|{deviceKey}"</c>.</summary>
    private static string Key(Guid visitorProfileKey, string deviceKey) =>
        $"{visitorProfileKey:N}|{deviceKey}";

    public bool TryGet(
        Guid visitorProfileKey,
        string deviceKey,
        out AnalyticsSessionCacheEntry entry)
    {
        var key = Key(visitorProfileKey, deviceKey);
        if (_cache.TryGetValue(key, out AnalyticsSessionCacheEntry? cached) && cached is not null)
        {
            entry = cached;
            return true;
        }

        entry = default!;
        return false;
    }

    public void Set(
        Guid visitorProfileKey,
        string deviceKey,
        AnalyticsSessionCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var key = Key(visitorProfileKey, deviceKey);
        var slidingMinutes = Math.Max(1, _options.CurrentValue.InactivityTimeoutMinutes) * 2;

        var cacheEntryOptions = new MemoryCacheEntryOptions
        {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(slidingMinutes),
        };
        cacheEntryOptions.RegisterPostEvictionCallback((evictedKey, _, _, _) =>
        {
            if (evictedKey is string s)
            {
                _keys.TryRemove(s, out _);
            }
        });

        _cache.Set(key, entry, cacheEntryOptions);
        _keys.TryAdd(key, 0);
    }

    public void Invalidate(Guid visitorProfileKey, string deviceKey)
    {
        var key = Key(visitorProfileKey, deviceKey);
        _cache.Remove(key);
        _keys.TryRemove(key, out _);
    }

    /// <summary>
    /// Walk the cache keys and evict any entry whose value is the given
    /// session key. O(N) in cache size — bounded by
    /// <c>CacheCapacity</c>. Called by the sweeper after closing rows
    /// and by the cascade step after soft-anonymisation.
    /// </summary>
    public void InvalidateBySessionKey(Guid sessionKey)
    {
        foreach (var key in _keys.Keys)
        {
            if (_cache.TryGetValue(key, out AnalyticsSessionCacheEntry? cached) &&
                cached is not null &&
                cached.SessionKey == sessionKey)
            {
                _cache.Remove(key);
                _keys.TryRemove(key, out _);
            }
        }
    }

    /// <summary>
    /// Walk the cache keys and evict any entry for the given visitor.
    /// O(N) in cache size; called by the cascade step on visitor
    /// anonymisation (post-repository-success).
    /// </summary>
    public void InvalidateByVisitorKey(Guid visitorProfileKey)
    {
        var prefix = $"{visitorProfileKey:N}|";
        foreach (var key in _keys.Keys)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                _cache.Remove(key);
                _keys.TryRemove(key, out _);
            }
        }
    }

    public void Dispose() => _cache.Dispose();
}
