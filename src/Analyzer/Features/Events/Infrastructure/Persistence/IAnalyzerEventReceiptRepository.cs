using Analyzer.Analytics;

namespace Analyzer.Features.Events.Infrastructure.Persistence;

/// <summary>
/// Internal repository for <c>analyzerEventReceipt</c>. The two write
/// surfaces slice 002 needs:
/// <list type="bullet">
/// <item><see cref="InsertAsync"/> — single-row insert driven by the
/// dispatcher's batch flush. Tolerates the
/// unique-violation on <c>pageviewKey</c> (treats duplicate dispatch
/// as a no-op).</item>
/// <item><see cref="DeleteByVisitorKeyAsync"/> — bulk delete of all
/// receipts for a visitor, called by
/// <c>AnalyzerEventReceiptCascadeStep</c> inside Customizer's
/// anonymisation outer scope.</item>
/// </list>
/// </summary>
internal interface IAnalyzerEventReceiptRepository
{
    /// <summary>
    /// Insert one receipt row. Catches the
    /// unique-violation on <c>pageviewKey</c> and treats it as a
    /// successful no-op (idempotency; research §8). Other database
    /// errors propagate.
    /// </summary>
    Task InsertAsync(AnalyticsEventReceipt receipt, CancellationToken ct);

    /// <summary>
    /// Delete every receipt row whose <c>visitorProfileKey</c> matches
    /// <paramref name="visitorProfileKey"/>. Opens a nested
    /// <c>IScopeProvider.CreateScope()</c> that enlists in the ambient
    /// outer scope by Umbraco convention; the cascade-step relies on
    /// this for atomic rollback.
    /// </summary>
    Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct);
}
