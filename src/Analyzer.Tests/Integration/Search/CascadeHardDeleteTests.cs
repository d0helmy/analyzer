using System.Diagnostics;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T039 (US1 cascade + SC-004) — running the cascade step
/// hard-deletes the visitor's <c>analyzerSearchEvent</c> rows without
/// touching other visitors; latency assertion confirms the SC-004
/// budget (≤ 200 ms for 1 000 rows) via the indexed
/// <c>visitorProfileKey</c> predicate. Includes the
/// <c>PIICleanupVerification</c> assertion: after the cascade, no row
/// references the visitor's unique seed query (proving the literal
/// query text is gone, not just the visitor link).
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeHardDeleteTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task Cascade_deletes_target_visitor_rows_only()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var pageviewA = Guid.NewGuid();
        var pageviewB = Guid.NewGuid();
        await SeedPageviewAsync(pageviewA, visitorA, contentKey);
        await SeedPageviewAsync(pageviewB, visitorB, contentKey);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitorA, pageviewA, count: 3, ct);
        await SeedAsync(visitorB, pageviewB, count: 2, ct);

        using (var scope = Services.CreateScope())
        {
            var cascade = ResolveSearchCascadeStep(scope.ServiceProvider);
            await cascade.ExecuteAsync(visitorA, ct);
        }

        Count(visitorA).Should().Be(0);
        Count(visitorB).Should().Be(2);
    }

    [Fact]
    public async Task Cascade_completes_under_two_hundred_ms_for_one_thousand_rows()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitor, pageviewKey, count: 1_000, ct);

        using var scope = Services.CreateScope();
        var cascade = ResolveSearchCascadeStep(scope.ServiceProvider);

        var sw = Stopwatch.StartNew();
        await cascade.ExecuteAsync(visitor, ct);
        sw.Stop();

        Count(visitor).Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(200,
            "SC-004 budget: hard-delete of 1 000 rows via the indexed visitorProfileKey predicate must complete within 200 ms");
    }

    [Fact]
    public async Task Cascade_is_zero_row_noop_for_visitor_with_no_events()
    {
        var visitor = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;

        using var scope = Services.CreateScope();
        var cascade = ResolveSearchCascadeStep(scope.ServiceProvider);
        await cascade.ExecuteAsync(visitor, ct);

        Count(visitor).Should().Be(0);
    }

    [Fact]
    public async Task Cascade_removes_literal_query_text_not_just_link()
    {
        // PIICleanupVerification — proves the row is hard-deleted (not
        // re-keyed). Seed a row whose rawQuery is a unique sentinel
        // string; after cascade, a LIKE query for that sentinel
        // returns zero rows.
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        var sentinel = $"slice-007-pii-{Guid.NewGuid():N}";
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<IAnalyzerSearchEventCaptureHandler>();
            await handler.HandleAsync(
                new AnalyzerSearchEventCapture(
                    Actor: NewIdentity(visitor),
                    PageviewKey: pageviewKey,
                    ContentKey: Guid.Empty,
                    RawQuery: sentinel,
                    ResultCount: 1,
                    UserAgent: "UA/test",
                    ReceivedUtc: DateTimeOffset.UtcNow),
                ct);
        }

        using (var scope = Services.CreateScope())
        {
            var cascade = ResolveSearchCascadeStep(scope.ServiceProvider);
            await cascade.ExecuteAsync(visitor, ct);
        }

        using var readScope = ScopeProvider.CreateScope();
        var leakedRows = readScope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerSearchEvent} " +
            "WHERE [rawQuery] LIKE @0",
            $"%{sentinel}%");
        readScope.Complete();
        leakedRows.Should().Be(0,
            "hard-delete removes the literal query text — not just the visitor link (FR-SRC-04 PII parsimony)");
    }

    private static IAnonymizationCascadeStep ResolveSearchCascadeStep(IServiceProvider sp) =>
        sp.GetServices<IAnonymizationCascadeStep>()
          .Single(s => s.GetType().Name == "AnalyzerSearchEventCascadeStep");

    private async Task SeedAsync(Guid visitor, Guid pageviewKey, int count, CancellationToken ct)
    {
        var actor = NewIdentity(visitor);
        var t0 = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalyzerSearchEventCaptureHandler>()
                .HandleAsync(
                    new AnalyzerSearchEventCapture(
                        Actor: actor,
                        PageviewKey: pageviewKey,
                        ContentKey: Guid.Empty,
                        RawQuery: $"query-{i:D5}",
                        ResultCount: i % 10,
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(i)),
                    ct);
        }
    }
}
