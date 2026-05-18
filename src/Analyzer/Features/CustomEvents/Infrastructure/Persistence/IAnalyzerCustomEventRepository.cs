namespace Analyzer.Features.CustomEvents.Infrastructure.Persistence;

/// <summary>
/// Slice 004 — internal repository for the
/// <c>analyzerCustomEvent</c> table. Opens nested
/// <c>IScopeProvider.CreateScope()</c> per call; when an outer scope is
/// already open (e.g. <c>AnonymizeVisitorProfileHandler</c>), the
/// nested scope enlists in the outer transaction and rolls back
/// atomically on a throw — matches slice-002 receipt + slice-003
/// session repo conventions.
/// </summary>
internal interface IAnalyzerCustomEventRepository
{
    /// <summary>
    /// Insert one custom-event row. Throws on FK constraint violation
    /// (e.g. session no longer active because the sweeper closed it
    /// between resolver + insert — rare).
    /// </summary>
    Task InsertAsync(AnalyzerCustomEventDto dto, CancellationToken ct);

    /// <summary>
    /// DELETE every row whose <c>visitorProfileKey</c> matches
    /// <paramref name="visitorProfileKey"/>. Used by the cascade step
    /// inside Customizer's outer NPoco scope (US2 hard-delete).
    /// </summary>
    Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct);
}
