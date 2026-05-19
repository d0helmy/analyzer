namespace Analyzer.Features.Search.Infrastructure.Persistence;

/// <summary>
/// Slice 007 — internal repository for the <c>analyzerSearchEvent</c>
/// table. Opens nested <c>IScopeProvider.CreateScope()</c> per call;
/// when an outer scope is open (cascade step inside Customizer's
/// <c>AnonymizeVisitorProfileHandler</c>), the nested scope enlists
/// in the outer transaction and rolls back atomically on a throw —
/// matches the slice-002/003/004/005/006 repo pattern.
/// </summary>
internal interface IAnalyzerSearchEventRepository
{
    /// <summary>
    /// Insert one accepted search-submission row. Single INSERT — no
    /// idempotency guard (search events have no
    /// <c>(pageviewKey, normalisedQuery)</c> unique index per research
    /// §R7: re-running the same search IS a distinct engagement signal).
    /// </summary>
    Task InsertAsync(AnalyzerSearchEventDto dto, CancellationToken ct);

    /// <summary>
    /// DELETE every row whose <c>visitorProfileKey</c> matches
    /// <paramref name="visitorProfileKey"/>. Used by the cascade step
    /// inside Customizer's outer NPoco scope (FR-010 hard-delete;
    /// SC-004 200 ms budget for 1 000 rows).
    /// </summary>
    Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct);

    /// <summary>
    /// COUNT(*) of rows for the visitor — used by perf-smoke
    /// verification (SC-004 assertion that the cascade actually removed
    /// N rows) and integration tests.
    /// </summary>
    Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct);

    /// <summary>
    /// Slice 007 — visitor-bound <c>pageviewKey</c> validation
    /// (research §R3). Returns <c>(visitorProfileKey, contentKey)</c>
    /// for <paramref name="pageviewKey"/> in <c>customizerPageview</c>,
    /// or <c>null</c> if the pageview does not exist. Used by the
    /// handler to (a) reject 400 when a client POSTs a
    /// <c>pageviewKey</c> that belongs to a different visitor (defends
    /// against forged correlations on a PII-bearing row), and
    /// (b) project the row's <c>contentKey</c> server-side (defends
    /// against a client forging arbitrary content-key correlations).
    /// </summary>
    Task<PageviewBinding?> ResolvePageviewBindingAsync(Guid pageviewKey, CancellationToken ct);
}

/// <summary>
/// Slice 007 — projection returned by
/// <see cref="IAnalyzerSearchEventRepository.ResolvePageviewBindingAsync"/>.
/// </summary>
internal readonly record struct PageviewBinding(Guid VisitorProfileKey, Guid ContentKey);
