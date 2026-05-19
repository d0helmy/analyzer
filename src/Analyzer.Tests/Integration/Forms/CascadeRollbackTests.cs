using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Application.Anonymization;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Forms;

/// <summary>
/// Slice 005 / T039 — Analyzer's form-event cascade DELETE must
/// participate in the ambient outer
/// <see cref="Umbraco.Cms.Infrastructure.Scoping.IScopeProvider"/>
/// transaction. When the outer scope does NOT call <c>Complete()</c>
/// (simulating a downstream cascade-step throwing), the DELETE rolls
/// back atomically. Mirrors slice-002 + slice-004 precedent.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeRollbackTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Throw_after_form_event_step_rolls_back_the_delete()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;
        await SeedFormEventsAsync(visitor, count: 3, ct);
        Count(visitor).Should().Be(3);

        using (var outerScope = ScopeProvider.CreateScope())
        {
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerFormEventCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitor, ct);

            // outerScope disposes without Complete() — DELETE rolls back.
        }

        Count(visitor).Should().Be(3,
            "Analyzer's DELETE participates in the outer NPoco scope; without scope.Complete() it rolls back atomically");
    }

    private async Task SeedFormEventsAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalyzerFormEventCaptureHandler>()
                .HandleAsync(
                    new AnalyzerFormEventCapture(
                        Actor: actor,
                        FormKey: formKey,
                        ContentKey: contentKey,
                        EventType: AnalyzerFormEventType.Impression,
                        ElapsedMsFromImpression: null,
                        ElapsedMsFromStart: null,
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(i)),
                    ct);
        }
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerFormEvent} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
