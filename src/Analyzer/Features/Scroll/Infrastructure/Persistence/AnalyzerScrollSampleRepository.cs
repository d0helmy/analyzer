using System.Data.Common;
using Analyzer.Analytics;
using Analyzer.Features.Common.Persistence;
using Analyzer.Features.Scroll.Domain;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Scroll.Infrastructure.Persistence;

/// <summary>
/// Slice 006 — NPoco-backed
/// <see cref="IAnalyzerScrollSampleRepository"/>. Mirrors slice-005's
/// form-event repo: nested-scope semantics participate in Customizer's
/// anonymisation outer scope (cascade step), so a downstream throw
/// rolls back the insert atomically.
/// </summary>
internal sealed class AnalyzerScrollSampleRepository : IAnalyzerScrollSampleRepository
{
    private const string PageviewBucketIndexName = "UX_analyzerScrollSample_pageviewBucket";

    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerScrollSampleRepository> _logger;

    public AnalyzerScrollSampleRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerScrollSampleRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task InsertAsync(AnalyzerScrollSampleDto dto, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ct.ThrowIfCancellationRequested();

        try
        {
            using var scope = _scopeProvider.CreateScope();
            await scope.Database.InsertAsync(dto).ConfigureAwait(false);
            scope.Complete();
        }
        catch (DbException ex) when (UniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex))
        {
            // Discriminate by index name so eventKey-Guid collisions
            // (astronomically rare client bug) bubble unchanged while
            // (pageviewKey, bucket) violations surface as the typed
            // ScrollSampleDuplicateException for the 409 idempotency
            // path. SqlException + SqliteException both include the
            // index name in Message.
            if (ex.Message.Contains(PageviewBucketIndexName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "Scroll-milestone insert hit unique index {Index} for pageviewKey={PageviewKey} bucket={Bucket}",
                    PageviewBucketIndexName, dto.PageviewKey, dto.Bucket);
                throw new ScrollSampleDuplicateException(
                    dto.PageviewKey,
                    (AnalyzerScrollBucket)dto.Bucket);
            }

            throw;
        }
    }

    public Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Execute(
            $"DELETE FROM {Constants.Database.AnalyzerScrollSample} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }

    public Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerScrollSample} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.FromResult(count);
    }
}
