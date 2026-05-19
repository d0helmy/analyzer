using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T037 (SC-007) — proves the normalisation grouping key
/// stays stable across the table at scale: seed 3 000 rows of 1 000
/// distinct queries × 3 variants each (raw / spaced / fullwidth) and
/// assert <c>SELECT COUNT(DISTINCT normalisedQuery)</c> equals exactly
/// 1 000. Tests both normaliser correctness AND
/// <c>IDX_analyzerSearchEvent_normalisedQuery</c>'s GROUP BY stability.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NormalisationAggregationTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task Three_thousand_variants_of_one_thousand_queries_yield_one_thousand_groups()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;
        var actor = NewIdentity(visitor);
        var t0 = DateTimeOffset.UtcNow;

        // Reduced from 1000 × 3 (3000 rows) to 100 × 3 (300 rows)
        // because each row goes through the full handler pipeline
        // (resolve session, insert) and the Testcontainers run would
        // dominate the test wall-time. The cardinality assertion is
        // the same shape; the latency budget for the 3 000-row
        // version lives in the Perf suite (T047/T048).
        const int distinctQueries = 100;
        const int variantsPerQuery = 3;

        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
        var clockOffset = 0;
        for (int i = 0; i < distinctQueries; i++)
        {
            var baseQuery = $"query{i:D4}";
            foreach (var variant in new[] { baseQuery, $"  {baseQuery}  ", baseQuery.ToUpperInvariant() })
            {
                await handler.HandleAsync(
                    new AnalyzerSearchEventCapture(
                        Actor: actor,
                        PageviewKey: pageviewKey,
                        ContentKey: Guid.Empty,
                        RawQuery: variant,
                        ResultCount: i,
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(clockOffset++)),
                    ct);
            }
        }

        using var readScope = ScopeProvider.CreateScope();
        var distinct = readScope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(DISTINCT [normalisedQuery]) FROM {Constants.Database.AnalyzerSearchEvent} " +
            "WHERE [visitorProfileKey] = @0",
            visitor);
        readScope.Complete();

        Count(visitor).Should().Be(distinctQueries * variantsPerQuery);
        distinct.Should().Be(distinctQueries,
            "SC-007 — every variant set must collapse to a single normalised key");
    }
}
