using System.Data.Common;
using Analyzer.Analytics;
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
        };

        using var scope = _scopeProvider.CreateScope();
        try
        {
            await scope.Database.InsertAsync(dto).ConfigureAwait(false);
            scope.Complete();
        }
        catch (DbException ex) when (IsUniqueConstraintViolation(ex))
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

    /// <summary>
    /// Provider-agnostic detection of the unique-index-violation case.
    /// Avoids hard-referencing <c>Microsoft.Data.SqlClient</c> or
    /// <c>Microsoft.Data.Sqlite</c> from Analyzer's compile graph
    /// (research §8). SQL Server uses error numbers 2627 / 2601;
    /// SQLite uses constraint error code 19; both surface as
    /// <see cref="DbException"/> with <c>SqlState</c> 23xxx
    /// (ISO integrity-constraint class) in modern providers.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbException ex)
    {
        const int SqlServerDuplicateKey = 2627;
        const int SqlServerUniqueIndex = 2601;
        const int SqliteConstraint = 19;

        // Provider-specific numeric error codes.
        if (ex.ErrorCode == SqlServerDuplicateKey ||
            ex.ErrorCode == SqlServerUniqueIndex ||
            ex.ErrorCode == SqliteConstraint)
        {
            return true;
        }

        // ANSI SQLSTATE class 23 covers integrity constraint violations;
        // 23505 is "unique violation" in PostgreSQL; SQL Server uses
        // 23000 for the broader class. Modern providers populate this.
        if (ex.SqlState is { Length: >= 2 } s && s.StartsWith("23", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
