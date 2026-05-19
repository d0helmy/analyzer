using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.CustomEvents.Application.Anonymization;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T039 (US2 AS2) — Analyzer's custom-event cascade DELETE
/// must participate in the ambient outer <see cref="Umbraco.Cms.Infrastructure.Scoping.IScopeProvider"/>
/// transaction. When the outer scope does NOT call <c>Complete()</c>
/// (simulating a downstream cascade-step throwing), the DELETE rolls
/// back atomically and the rows reappear. Mirrors slice-002's
/// <c>CascadeRollbackTests</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeRollbackTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Throw_after_custom_event_step_rolls_back_the_delete()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;
        await SeedCustomEventsAsync(visitor, count: 3, ct);
        Count(visitor).Should().Be(3);

        using (var outerScope = ScopeProvider.CreateScope())
        {
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerCustomEventCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitor, ct);

            // Simulate a later cascade step throwing — outer scope
            // disposes without Complete(), so the DELETE rolls back.
        }

        Count(visitor).Should().Be(3,
            "Analyzer's DELETE participates in the outer NPoco scope; without scope.Complete() it rolls back atomically");
    }

    private async Task SeedCustomEventsAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(new CustomEventCapture(
                    Actor: actor,
                    Category: "engagement",
                    Action: "click",
                    Label: null,
                    Value: null,
                    UserAgent: "UA/test",
                    ReceivedUtc: DateTimeOffset.UtcNow.AddMilliseconds(i)),
                    ct);
        }
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerCustomEvent} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
