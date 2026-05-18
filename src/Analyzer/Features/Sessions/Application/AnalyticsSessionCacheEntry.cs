namespace Analyzer.Features.Sessions.Application;

/// <summary>
/// Slice 003 — value type held in <see cref="AnalyzerSessionCacheStore"/>.
/// Compact projection of the active-session row, enough for the
/// resolver to make an extend / close-and-open decision without
/// re-reading from the database.
/// </summary>
/// <param name="SessionKey">
/// The public stable identifier of the cached session.
/// </param>
/// <param name="StartUtc">
/// Session start time — needed to construct the post-extend
/// <see cref="Analytics.AnalyticsSession"/> projection client-side
/// when the repository's <c>ExtendAsync</c> returns only the deltas.
/// </param>
/// <param name="LastActivityUtc">
/// Last-observed activity. Combined with the configured inactivity
/// timeout to decide whether the cache entry is fresh or stale.
/// </param>
/// <param name="PageviewCount">
/// Last-known pageview count. Updated client-side after a successful
/// <c>ExtendAsync</c> returns the post-update value.
/// </param>
internal sealed record AnalyticsSessionCacheEntry(
    Guid SessionKey,
    DateTimeOffset StartUtc,
    DateTimeOffset LastActivityUtc,
    int PageviewCount);
