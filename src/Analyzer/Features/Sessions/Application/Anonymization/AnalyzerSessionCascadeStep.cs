using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Sessions.Application.Anonymization;

/// <summary>
/// Slice 003 US2 / FR-006 — participates in Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> outer scope by
/// soft-anonymising the visitor's <c>analyzerSession</c> rows. Sets
/// <c>anonymizedUtc = now</c> and clears <c>deviceKey</c>; preserves
/// aggregates (<c>pageviewCount</c>, <c>startUtc</c>, <c>endUtc</c>).
/// </summary>
/// <remarks>
/// <para>
/// Distinct semantic from slice-002's
/// <c>AnalyzerEventReceiptCascadeStep</c> (hard-delete) — Constitution
/// Principle IV v1.1.1 authorises the per-table choice. Session-level
/// aggregates are load-bearing for slice 005's content app and
/// slice 010's reports; hard-deleting on anonymisation would create
/// artificial dips. Spec Assumption #2.
/// </para>
/// <para>
/// Throw inside the repository call rolls back the entire
/// anonymisation transaction unconditionally — the outer
/// <c>scope.Complete()</c> only runs after every cascade step
/// succeeds. Cache invalidation runs AFTER repository success; if the
/// outer transaction subsequently rolls back, the cache may briefly
/// reflect "anonymised" state for an un-anonymised row — self-healing
/// on the next resolver call via the sliding expiration + DB re-read.
/// </para>
/// </remarks>
internal sealed class AnalyzerSessionCascadeStep : IAnonymizationCascadeStep
{
    private readonly IAnalyzerSessionRepository _repository;
    private readonly AnalyzerSessionCacheStore _cacheStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AnalyzerSessionCascadeStep> _logger;

    public AnalyzerSessionCascadeStep(
        IAnalyzerSessionRepository repository,
        AnalyzerSessionCacheStore cacheStore,
        TimeProvider timeProvider,
        ILogger<AnalyzerSessionCascadeStep> logger)
    {
        _repository = repository;
        _cacheStore = cacheStore;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        if (visitorProfileKey == Guid.Empty)
        {
            _logger.LogDebug(
                "AnalyzerSessionCascadeStep called with empty VisitorProfileKey; skipping.");
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var affectedSessionKeys = await _repository
            .SoftAnonymizeByVisitorKeyAsync(visitorProfileKey, nowUtc, ct)
            .ConfigureAwait(false);

        // Evict cache entries for the visitor — covers all (visitor,
        // device) pairs. O(N) in cache size; bounded by CacheCapacity.
        _cacheStore.InvalidateByVisitorKey(visitorProfileKey);

        _logger.LogInformation(
            "Analyzer session soft-anonymisation completed for VisitorProfileKey={VisitorKey} Count={Count}",
            visitorProfileKey,
            affectedSessionKeys.Count);
    }
}
