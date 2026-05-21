using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 — shared test base for the
/// <c>Analyzer.Tests.Integration.Search</c> namespace. Adds
/// search-specific helpers (pageview seeding for the visitor-bound
/// pageviewKey check, row reading, identity construction) on top of
/// <see cref="AnalyzerIntegrationTestBase"/>.
/// </summary>
public abstract class SearchIntegrationTestBase : AnalyzerIntegrationTestBase
{
    /// <summary>
    /// Seed one <c>customizerVisitorPageview</c> row keyed by the supplied
    /// <paramref name="pageviewKey"/>, owned by the visitor identified
    /// by <paramref name="visitorKey"/>, and pointing at the supplied
    /// <paramref name="contentKey"/>. Used by the visitor-bound
    /// pageviewKey check in
    /// <see cref="Analyzer.Features.Search.Infrastructure.Persistence.IAnalyzerSearchEventRepository.ResolvePageviewBindingAsync"/>.
    /// </summary>
    protected async Task SeedPageviewAsync(Guid pageviewKey, Guid visitorKey, Guid contentKey)
    {
        await SeedVisitorProfileAsync(visitorKey);
        using var scope = ScopeProvider.CreateScope();
        var exists = scope.Database.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM customizerVisitorPageview WHERE [key] = @0",
            pageviewKey);
        if (exists == 0)
        {
            // Resolve the visitor's surrogate id so the FK shape matches
            // customizerVisitorPageview's expected (int) FK target.
            var visitorFk = scope.Database.ExecuteScalar<int>(
                "SELECT [id] FROM customizerVisitorProfile WHERE [key] = @0",
                visitorKey);

            scope.Database.Execute(
                "INSERT INTO [customizerVisitorPageview] " +
                "([key], [visitorProfileFk], [contentKey], [pageviewSegmentsJson], " +
                " [wasContentTombstoned], [requestUtc]) " +
                "VALUES (@0, @1, @2, '[]', 0, @3)",
                pageviewKey, visitorFk, contentKey, DateTime.UtcNow);
        }
        scope.Complete();
    }

    /// <summary>
    /// Read all <c>analyzerSearchEvent</c> rows for the visitor,
    /// ordered by <c>receivedUtc</c>.
    /// </summary>
    protected List<SearchEventRow> ReadRows(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<SearchEventRow>(
            "SELECT [eventKey] AS EventKey, [sessionKey] AS SessionKey, " +
            "[pageviewKey] AS PageviewKey, [contentKey] AS ContentKey, " +
            "[rawQuery] AS RawQuery, [normalisedQuery] AS NormalisedQuery, " +
            "[resultCount] AS ResultCount, [receivedUtc] AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerSearchEvent} " +
            "WHERE [visitorProfileKey] = @0 " +
            "ORDER BY [receivedUtc]",
            visitor);
        scope.Complete();
        return rows;
    }

    /// <summary>
    /// COUNT(*) for the visitor — cheaper than <see cref="ReadRows"/>
    /// when the test only needs cardinality.
    /// </summary>
    protected int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var c = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerSearchEvent} WHERE [visitorProfileKey] = @0",
            visitor);
        scope.Complete();
        return c;
    }

    protected static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);

    public sealed class SearchEventRow
    {
        public Guid EventKey { get; set; }
        public Guid SessionKey { get; set; }
        public Guid PageviewKey { get; set; }
        public Guid ContentKey { get; set; }
        public string RawQuery { get; set; } = string.Empty;
        public string NormalisedQuery { get; set; } = string.Empty;
        public int ResultCount { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }
}
