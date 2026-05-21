using System.Data.Common;

namespace Analyzer.Features.Common.Persistence;

/// <summary>
/// Provider-agnostic detection of the unique-index-violation case for
/// repository insert paths. Avoids hard-referencing
/// <c>Microsoft.Data.SqlClient</c> or <c>Microsoft.Data.Sqlite</c> from
/// Analyzer's compile graph (those packages are test-only).
/// </summary>
/// <remarks>
/// <para>
/// Extracted from the slice-002 <c>AnalyzerEventReceiptRepository</c>
/// (research §8) so slice-003's <c>AnalyzerSessionRepository</c> can
/// share the same predicate for its partial-unique-index collision
/// retry path (research §4). SQL Server uses error numbers 2627 / 2601;
/// SQLite uses constraint error code 19.
/// </para>
/// <para>
/// Provider-property notes (empirically verified, see #59):
/// </para>
/// <list type="bullet">
/// <item><c>Microsoft.Data.SqlClient.SqlException</c> sets
/// <c>HResult</c> (and therefore <c>ErrorCode</c>) to the standard
/// HRESULT <c>0x80131904</c> — NOT the SQL Server error number — and
/// leaves <c>SqlState</c> null. The actual error number lives on the
/// provider-specific <c>Number</c> property, read here via reflection
/// to keep <c>Microsoft.Data.SqlClient</c> out of Analyzer's compile
/// graph.</item>
/// <item><c>Microsoft.Data.Sqlite.SqliteException</c> sets
/// <c>SqlState</c> to ISO class <c>23xxx</c> and exposes the SQLite
/// error code via <c>ErrorCode</c> (HRESULT==19 for constraint
/// violations); the <c>SqlState</c> check covers SQLite without
/// reflection.</item>
/// </list>
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

        if (TryGetSqlServerErrorNumber(ex, out var number) &&
            (number == SqlServerDuplicateKey || number == SqlServerUniqueIndex))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetSqlServerErrorNumber(DbException ex, out int number)
    {
        number = 0;
        var property = ex.GetType().GetProperty("Number", typeof(int));
        if (property?.GetValue(ex) is int value)
        {
            number = value;
            return true;
        }
        return false;
    }
}
