namespace Analyzer.Features.Forms.Infrastructure.Persistence;

/// <summary>
/// Slice 005 — internal repository for the <c>analyzerFormEvent</c>
/// table. Opens nested <c>IScopeProvider.CreateScope()</c> per call;
/// when an outer scope is open (cascade step inside Customizer's
/// <c>AnonymizeVisitorProfileHandler</c>, or the sweeper's per-pass
/// scope used by the abandonment materialiser), the nested scope
/// enlists in the outer transaction and rolls back atomically on a
/// throw — matches the slice-002/003/004 repo pattern.
/// </summary>
internal interface IAnalyzerFormEventRepository
{
    /// <summary>
    /// Insert one lifecycle row.
    /// </summary>
    Task InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct);

    /// <summary>
    /// DELETE every row whose <c>visitorProfileKey</c> matches
    /// <paramref name="visitorProfileKey"/>. Used by the cascade step
    /// inside Customizer's outer NPoco scope (FR-010 hard-delete;
    /// SC-004 200ms budget for 1000 rows).
    /// </summary>
    Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct);

    /// <summary>
    /// Slice 005 — query the <c>(sessionKey, formKey, visitorProfileKey)</c>
    /// tuples that have a <c>Start</c> row but no <c>Success</c> row
    /// AND no existing <c>Abandon</c> row (idempotency guard) for the
    /// supplied session keys. Used by the abandonment materialiser
    /// after the sweeper logically closes sessions.
    /// </summary>
    Task<IReadOnlyList<UnclosedStartTuple>> ListUnclosedStartsForSessionsAsync(
        IReadOnlyCollection<Guid> sessionKeys,
        CancellationToken ct);

    /// <summary>
    /// Slice 005 — bulk insert <c>Abandon</c> rows produced by the
    /// materialiser. Runs inside the sweeper's outer scope.
    /// </summary>
    Task InsertAbandonsBulkAsync(
        IReadOnlyList<AnalyzerFormEventDto> abandons,
        CancellationToken ct);
}

/// <summary>
/// Result row for
/// <see cref="IAnalyzerFormEventRepository.ListUnclosedStartsForSessionsAsync"/>.
/// Carries the keys plus the <c>Start</c> row's <c>receivedUtc</c>
/// so the materialiser can compute <c>elapsedMsFromStart</c>.
/// </summary>
internal readonly record struct UnclosedStartTuple(
    Guid SessionKey,
    Guid FormKey,
    Guid VisitorProfileKey,
    Guid ContentKey,
    DateTimeOffset StartReceivedUtc);
