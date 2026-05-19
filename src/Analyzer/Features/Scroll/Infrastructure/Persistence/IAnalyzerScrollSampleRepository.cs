namespace Analyzer.Features.Scroll.Infrastructure.Persistence;

/// <summary>
/// Slice 006 — internal repository for the <c>analyzerScrollSample</c>
/// table. Opens nested <c>IScopeProvider.CreateScope()</c> per call;
/// when an outer scope is open (cascade step inside Customizer's
/// <c>AnonymizeVisitorProfileHandler</c>), the nested scope enlists in
/// the outer transaction and rolls back atomically on a throw — matches
/// the slice-002/003/004/005 repo pattern.
/// </summary>
internal interface IAnalyzerScrollSampleRepository
{
    /// <summary>
    /// Insert one milestone-crossing row. Throws
    /// <see cref="Domain.ScrollSampleDuplicateException"/> when the
    /// unique index <c>UX_analyzerScrollSample_pageviewBucket</c>
    /// rejects the insert because a row already exists for the same
    /// <c>(pageviewKey, bucket)</c> tuple — the handler maps that to
    /// HTTP 409. Other unique-index hits (e.g. an unlikely
    /// <c>eventKey</c> Guid collision) re-throw the original
    /// <see cref="System.Data.Common.DbException"/>.
    /// </summary>
    Task InsertAsync(AnalyzerScrollSampleDto dto, CancellationToken ct);

    /// <summary>
    /// DELETE every row whose <c>visitorProfileKey</c> matches
    /// <paramref name="visitorProfileKey"/>. Used by the cascade step
    /// inside Customizer's outer NPoco scope (FR-009 hard-delete;
    /// SC-004 200 ms budget for 1 000 rows).
    /// </summary>
    Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct);

    /// <summary>
    /// COUNT(*) of rows for the visitor — used by perf-smoke
    /// verification (SC-004 assertion that the cascade actually
    /// removed N rows) and integration tests.
    /// </summary>
    Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct);
}
