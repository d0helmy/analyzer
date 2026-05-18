using System.Data.Common;

namespace Analyzer.Features.Common.Persistence;

/// <summary>
/// Provider-agnostic detection of the unique-index-violation case for
/// repository insert paths. Avoids hard-referencing
/// <c>Microsoft.Data.SqlClient</c> or <c>Microsoft.Data.Sqlite</c> from
/// Analyzer's compile graph (those packages are test-only).
/// </summary>
/// <remarks>
/// Extracted from the slice-002 <c>AnalyzerEventReceiptRepository</c>
/// (research §8) so slice-003's <c>AnalyzerSessionRepository</c> can
/// share the same predicate for its partial-unique-index collision
/// retry path (research §4). SQL Server uses error numbers 2627 / 2601;
/// SQLite uses constraint error code 19; both surface as
/// <see cref="DbException"/> with <c>SqlState</c> 23xxx (ISO
/// integrity-constraint class) in modern providers.
/// </remarks>
internal static class UniqueConstraintViolationDetector
{
    private const int SqlServerDuplicateKey = 2627;
    private const int SqlServerUniqueIndex = 2601;
    private const int SqliteConstraint = 19;

    /// <summary>
    /// <c>true</c> when <paramref name="ex"/> represents a unique /
    /// primary-key constraint violation — i.e., a duplicate-row insert
    /// rejected by an enforcing index. Caller decides the semantic
    /// (treat as idempotent no-op, or as a concurrent-race signal).
    /// </summary>
    public static bool IsUniqueConstraintViolation(DbException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (ex.ErrorCode == SqlServerDuplicateKey ||
            ex.ErrorCode == SqlServerUniqueIndex ||
            ex.ErrorCode == SqliteConstraint)
        {
            return true;
        }

        if (ex.SqlState is { Length: >= 2 } s &&
            s.StartsWith("23", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}
