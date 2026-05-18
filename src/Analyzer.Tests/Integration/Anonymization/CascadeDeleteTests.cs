using Analyzer.Analytics;
using Analyzer.Features.Events.Infrastructure.Persistence;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Anonymization;

/// <summary>
/// US2 AS1 + SC-003 — Customizer's anonymisation deletes the visitor's
/// receipts inside the outer scope; other visitors' rows untouched;
/// ≥ 10 k row delete completes under the SC-003 200 ms budget.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeDeleteTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task AnonymisationDeletesReceiptsForOneVisitorOnly()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedReceiptsAsync(visitorA, count: 3);
        await SeedReceiptsAsync(visitorB, count: 2);

        await RunCascadeForAsync(visitorA);

        CountFor(visitorA).Should().Be(0);
        CountFor(visitorB).Should().Be(2);
    }

    [Fact]
    public async Task PostAnonymisationCountIsZero()
    {
        var visitorKey = Guid.NewGuid();
        await SeedReceiptsAsync(visitorKey, count: 5);

        await RunCascadeForAsync(visitorKey);

        CountFor(visitorKey).Should().Be(0);
    }

    [Fact]
    public async Task CompletesUnderTwoHundredMsForTenThousandRows()
    {
        var visitorKey = Guid.NewGuid();
        await SeedReceiptsAsync(visitorKey, count: 10_000);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await RunCascadeForAsync(visitorKey);
        sw.Stop();

        CountFor(visitorKey).Should().Be(0);
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            "SC-003 budget: ≤ 200 ms for 10 k rows via the indexed visitorProfileKey predicate");
    }

    private async Task SeedReceiptsAsync(Guid visitorKey, int count)
    {
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAnalyzerEventReceiptRepository>();
        for (int i = 0; i < count; i++)
        {
            await repo.InsertAsync(new AnalyticsEventReceipt(
                Id: Guid.NewGuid(),
                PageviewKey: Guid.NewGuid(),
                VisitorProfileKey: visitorKey,
                ReceivedUtc: DateTimeOffset.UtcNow),
                TestContext.Current.CancellationToken);
        }
    }

    private async Task RunCascadeForAsync(Guid visitorKey)
    {
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAnalyzerEventReceiptRepository>();
        await repo.DeleteByVisitorKeyAsync(visitorKey, TestContext.Current.CancellationToken);
    }

    private int CountFor(Guid visitorKey)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
            visitorKey);
        scope.Complete();
        return count;
    }
}
