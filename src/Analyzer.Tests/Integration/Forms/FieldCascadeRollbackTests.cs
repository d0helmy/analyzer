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
/// Slice 005 / T056 — outer-scope dispose-without-Complete() rolls
/// back the field-table DELETE atomically.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FieldCascadeRollbackTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Throw_after_field_event_step_rolls_back_the_delete()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(visitor, count: 3, ct);
        Count(visitor).Should().Be(3);

        using (var outerScope = ScopeProvider.CreateScope())
        {
            using var diScope = Services.CreateScope();
            var step = ActivatorUtilities.CreateInstance<AnalyzerFormFieldEventCascadeStep>(
                diScope.ServiceProvider);

            await step.ExecuteAsync(visitor, ct);
            // outerScope disposes without Complete() — DELETE rolls back.
        }

        Count(visitor).Should().Be(3,
            "the field-table DELETE participates in the outer NPoco scope; without Complete() it rolls back atomically");
    }

    private async Task SeedAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var formKey = Guid.NewGuid();
        var fieldKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;
        for (int i = 0; i < count; i++)
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IAnalyzerFormFieldEventCaptureHandler>()
                .HandleAsync(
                    new AnalyzerFormFieldEventCapture(
                        Actor: actor,
                        FormKey: formKey,
                        FieldKey: fieldKey,
                        EventType: AnalyzerFormFieldEventType.FieldFocus,
                        HadValue: null,
                        UserAgent: "UA/test",
                        ReceivedUtc: t0.AddMilliseconds(i)),
                    ct);
        }
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerFormFieldEvent} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
