using System.Data.Common;
using Analyzer.Analytics;
using Analyzer.Features.Common.Persistence;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Analyzer.Features.Events.Infrastructure.Persistence;

/// <summary>
/// NPoco-backed implementation of
/// <see cref="IAnalyzerEventReceiptRepository"/>. Opens nested
/// <c>IScopeProvider.CreateScope()</c> per call — when the caller has
/// already opened an outer scope (e.g. <c>AnonymizeVisitorProfileHandler</c>),
/// the nested scope enlists in the outer transaction and rolls back
/// atomically on a throw.
/// </summary>
internal sealed class AnalyzerEventReceiptRepository : IAnalyzerEventReceiptRepository
{
    private readonly IScopeProvider _scopeProvider;
    private readonly ILogger<AnalyzerEventReceiptRepository> _logger;

    public AnalyzerEventReceiptRepository(
        IScopeProvider scopeProvider,
        ILogger<AnalyzerEventReceiptRepository> logger)
    {
        _scopeProvider = scopeProvider;
        _logger = logger;
    }

    public async Task InsertAsync(AnalyticsEventReceipt receipt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ct.ThrowIfCancellationRequested();

        var dto = new AnalyzerEventReceiptDto
        {
            Id = receipt.Id,
            PageviewKey = receipt.PageviewKey,
            VisitorProfileKey = receipt.VisitorProfileKey,
            ReceivedUtc = receipt.ReceivedUtc,
            SessionKey = receipt.SessionKey,
        };

        using var scope = _scopeProvider.CreateScope();
        try
        {
            await scope.Database.InsertAsync(dto).ConfigureAwait(false);
            scope.Complete();
        }
        catch (DbException ex) when (UniqueConstraintViolationDetector.IsUniqueConstraintViolation(ex))
        {
            _logger.LogDebug(
                "Duplicate dispatch tolerated for PageviewKey={PageviewKey}",
                receipt.PageviewKey);
            scope.Complete();
        }
    }

    public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var scope = _scopeProvider.CreateScope();
        scope.Database.Execute(
            $"DELETE FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
            visitorProfileKey);
        scope.Complete();
        return Task.CompletedTask;
    }
}
