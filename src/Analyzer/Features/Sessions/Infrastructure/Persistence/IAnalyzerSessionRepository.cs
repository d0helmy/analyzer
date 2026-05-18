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
