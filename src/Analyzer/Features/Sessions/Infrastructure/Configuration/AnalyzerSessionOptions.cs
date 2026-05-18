namespace Analyzer.Features.Sessions.Infrastructure.Configuration;

/// <summary>
/// Slice 003 — runtime-reloadable tunables for the session subsystem.
/// Bound from <c>appsettings.json</c> under the <c>Analyzer:Session</c>
/// key via <c>IOptionsMonitor&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InactivityTimeoutMinutes"/>, <see cref="SweepIntervalSeconds"/>
/// and <see cref="SweepBatchSize"/> are read on every resolver call /
/// sweeper tick — operators can retune at runtime without a host
/// restart (spec FR-008).
/// </para>
/// <para>
/// <see cref="CacheCapacity"/> is captured at first
/// <c>AnalyzerSessionCacheStore</c> construction (the underlying
/// <c>MemoryCache.SizeLimit</c> is fixed for the lifetime of the cache
/// instance) — a capacity change requires a host restart (acceptable
/// operational tradeoff; spec FR-008 qualifier).
/// </para>
/// </remarks>
internal sealed class AnalyzerSessionOptions
{
    /// <summary>
    /// Inactivity timeout in minutes. A session whose
    /// <c>lastActivityUtc + InactivityTimeoutMinutes</c> is in the past
    /// is closed by either the lazy-close branch (US1 AS3) or the
    /// sweeper (US3 AS1). Default 30.
    /// </summary>
    public int InactivityTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Sweeper tick interval in seconds. Smaller intervals close
    /// inactive sessions faster, at the cost of more frequent DB scans.
    /// Default 60.
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum sessions closed per sweeper tick. Bounds the per-tick
    /// UPDATE statement size; keeps individual statements well below
    /// any reasonable SQL timeout. Default 1000.
    /// </summary>
    public int SweepBatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum entries held in the in-memory active-session cache.
    /// LRU eviction once exceeded. Default 10 000. Changes require
    /// host restart.
    /// </summary>
    public int CacheCapacity { get; set; } = 10_000;
}
