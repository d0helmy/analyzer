using Analyzer.Analytics;

namespace Analyzer.Features.Sessions.Infrastructure.Persistence;

/// <summary>
/// Slice 003 — internal repository for the <c>analyzerSession</c> table.
/// Opens nested <c>IScopeProvider.CreateScope()</c> per call; when an
/// outer scope is open (e.g. <c>AnonymizeVisitorProfileHandler</c>),
/// the nested scope enlists in the outer transaction and rolls back
/// atomically on a throw — matching slice-002's
/// <c>AnalyzerEventReceiptRepository</c> pattern.
/// </summary>
internal interface IAnalyzerSessionRepository
{
    /// <summary>
    /// Return the most-recent active session for
    /// <paramref name="visitorProfileKey"/> + <paramref name="deviceKey"/>,
    /// or null if none. Used by the resolver on cache miss + the
    /// race-collision retry path.
    /// </summary>
    Task<AnalyticsSession?> GetLatestActiveAsync(
        Guid visitorProfileKey,
        string deviceKey,
        CancellationToken ct);

    /// <summary>
    /// Open a new session row. Throws <see cref="System.Data.Common.DbException"/>
    /// (unique-violation) if the partial unique index
    /// <c>UX_analyzerSession_active_visitor_device</c> catches a race;
    /// the resolver catches via <c>UniqueConstraintViolationDetector</c>
    /// and re-reads.
    /// </summary>
    Task InsertAsync(AnalyzerSessionDto session, CancellationToken ct);

    /// <summary>
    /// UPDATE <c>lastActivityUtc</c> + increment <c>pageviewCount</c>
    /// atomically for the active row keyed by
    /// <paramref name="sessionKey"/>. Returns the post-update
    /// <c>(StartUtc, PageviewCount)</c> via SQL Server's
    /// <c>OUTPUT INSERTED.col</c> clause (SQLite uses SELECT-after-UPDATE
    /// in the same scope) so the resolver can project an
    /// <see cref="AnalyticsSession"/> client-side without a second
    /// SELECT (research §1; FR-009 budget).
    /// </summary>
    Task<SessionExtendResult> ExtendAsync(
        Guid sessionKey,
        DateTimeOffset newLastActivityUtc,
        CancellationToken ct);

    /// <summary>
    /// UPDATE <c>isActive = false, endUtc = logicalCloseUtc</c> on the
    /// row keyed by <paramref name="sessionKey"/>. Idempotent (UPDATE
    /// against an already-closed row is a no-op).
    /// </summary>
    Task CloseAsync(
        Guid sessionKey,
        DateTimeOffset logicalCloseUtc,
        CancellationToken ct);

    /// <summary>
    /// Slice 004 — advance <c>lastActivityUtc</c> on the session WITHOUT
    /// incrementing <c>pageviewCount</c>. Used by the custom-event
    /// capture path (Clarification §1). 1 indexed UPDATE; idempotent on
    /// already-closed rows (the <c>WHERE isActive = 1</c> predicate
    /// makes the touch a no-op when the sweeper has closed the row
    /// concurrently).
    /// </summary>
    Task TouchAsync(
        Guid sessionKey,
        DateTimeOffset newLastActivityUtc,
        CancellationToken ct);

    /// <summary>
    /// Slice 003 US2 — soft-anonymise every active-or-closed session
    /// row for <paramref name="visitorProfileKey"/>. Sets
    /// <c>anonymizedUtc = now</c> + clears <c>deviceKey</c>; preserves
    /// aggregates (<c>pageviewCount</c>, <c>startUtc</c>, <c>endUtc</c>).
    /// Idempotent — <c>WHERE anonymizedUtc IS NULL</c> excludes
    /// already-anonymised rows. Returns the sessionKeys that were
    /// affected so the cascade step can evict cache entries.
    /// </summary>
    Task<IReadOnlyList<Guid>> SoftAnonymizeByVisitorKeyAsync(
        Guid visitorProfileKey,
        DateTimeOffset nowUtc,
        CancellationToken ct);

    /// <summary>
    /// Slice 003 US3 — sweep eligible sessions: rows where
    /// <c>isActive = 1 AND lastActivityUtc &lt; cutoff</c>. For each
    /// match, close with <c>endUtc = lastActivityUtc + inactivityTimeout</c>
    /// (logical close time, NOT now — spec Assumption #5). Bounded by
    /// <paramref name="batchSize"/>. Returns the sessionKeys closed.
    /// </summary>
    Task<IReadOnlyList<Guid>> SweepEligibleAsync(
        DateTimeOffset cutoff,
        TimeSpan inactivityTimeout,
        int batchSize,
        CancellationToken ct);
}

/// <summary>
/// Post-update columns returned by
/// <see cref="IAnalyzerSessionRepository.ExtendAsync"/>. Lets the
/// resolver build the <see cref="AnalyticsSession"/> projection
/// client-side without an additional SELECT round-trip.
/// </summary>
internal readonly record struct SessionExtendResult(
    DateTimeOffset StartUtc,
    int PageviewCount);
