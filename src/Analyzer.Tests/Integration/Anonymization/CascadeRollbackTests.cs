using Analyzer.Analytics;
using Analyzer.Features.Events.Application.Anonymization;
using Analyzer.Features.Events.Infrastructure.Persistence;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Anonymization;

/// <summary>
/// US2 AS2 — when any cascade step in the outer NPoco scope throws,
/// every earlier delete inside that same outer scope rolls back atomically.
/// The full cross-product orchestrator path (Customizer's
/// <c>AnonymizeVisitorProfileHandler</c> → cascade steps → visitor-row
/// overwrite → outbox) is verified end-to-end by Customizer's tests;
/// this test confines itself to Analyzer's atomic-rollback contribution
/// inside an outer <c>IScopeProvider.CreateScope()</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeRollbackTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task ThrowAfterAnalyzerStepRollsBackTheDelete()
    {
        var visitorKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorKey);
        await SeedReceiptsAsync(visitorKey, count: 4);
        CountFor(visitorKey).Should().Be(4);

        using (var scope = ScopeProvider.CreateScope())
        {
            // Resolve Analyzer's cascade-step through DI so it shares
            // the ambient outer scope (its repository opens nested
            // scopes that enlist in this outer transaction).
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerEventReceiptCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitorKey, TestContext.Current.CancellationToken);

            // Simulate a later cascade step throwing — by NOT calling
            // scope.Complete() the outer NPoco scope rolls back on
            // Dispose, undoing Analyzer's DELETE.
            // (No scope.Complete() call here.)
        }

        CountFor(visitorKey).Should().Be(4,
            "Analyzer's DELETE participates in the outer NPoco scope; without scope.Complete() it rolls back atomically");
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
