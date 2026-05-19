using Analyzer.Analytics;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Forms.Application.Abandonment;

/// <summary>
/// Slice 005 — default
/// <see cref="IAnalyzerFormAbandonmentMaterialiser"/>. Single-query
/// SELECT (with the two <c>NOT EXISTS</c> predicates that exclude
/// already-Successful and already-Abandoned lifecycles) followed by a
/// bulk INSERT. SC-002: idempotent across re-runs of the same closed
/// batch — the Abandon-exclusion predicate suppresses
/// double-materialisation.
/// </summary>
internal sealed class AnalyzerFormAbandonmentMaterialiser : IAnalyzerFormAbandonmentMaterialiser
{
    private readonly IAnalyzerFormEventRepository _repository;
    private readonly ILogger<AnalyzerFormAbandonmentMaterialiser> _logger;

    public AnalyzerFormAbandonmentMaterialiser(
        IAnalyzerFormEventRepository repository,
        ILogger<AnalyzerFormAbandonmentMaterialiser> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task MaterialiseAsync(
        IReadOnlyCollection<Guid> closedSessionKeys,
        DateTimeOffset logicalCloseUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(closedSessionKeys);
        ct.ThrowIfCancellationRequested();

        if (closedSessionKeys.Count == 0)
        {
            return;
        }

        var tuples = await _repository
            .ListUnclosedStartsForSessionsAsync(closedSessionKeys, ct)
            .ConfigureAwait(false);

        if (tuples.Count == 0)
        {
            return;
        }

        var abandons = new List<AnalyzerFormEventDto>(tuples.Count);
        foreach (var tuple in tuples)
        {
            // SkipsAnonymisedVisitors edge case: an anonymised visitor's
            // Start row has already been hard-deleted by the cascade
            // step (Principle IV), so the SELECT returns no rows for
            // them — nothing to filter at this layer.

            var elapsedMs = (int)Math.Max(
                0,
                (logicalCloseUtc - tuple.StartReceivedUtc).TotalMilliseconds);

            abandons.Add(new AnalyzerFormEventDto
            {
                Id = Guid.NewGuid(),
                EventKey = Guid.NewGuid(),
                VisitorProfileKey = tuple.VisitorProfileKey,
                SessionKey = tuple.SessionKey,
                FormKey = tuple.FormKey,
                ContentKey = tuple.ContentKey,
                EventType = (byte)AnalyzerFormEventType.Abandon,
                ElapsedMsFromImpression = null,
                ElapsedMsFromStart = elapsedMs,
                ReceivedUtc = logicalCloseUtc,
            });
        }

        await _repository.InsertAbandonsBulkAsync(abandons, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Materialised {Count} Abandon row(s) for {SessionCount} closed session(s) at {LogicalCloseUtc:O}",
            abandons.Count,
            closedSessionKeys.Count,
            logicalCloseUtc);
    }
}
