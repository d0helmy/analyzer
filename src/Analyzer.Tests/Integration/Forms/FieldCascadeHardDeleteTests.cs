using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Forms;

/// <summary>
/// Slice 005 / T056 — field-event cascade hard-delete + rollback.
/// Mirrors the lifecycle table's
/// <see cref="CascadeHardDeleteTests"/> + <see cref="CascadeRollbackTests"/>
/// for the second slice-005 table.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FieldCascadeHardDeleteTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Cascade_deletes_target_visitor_field_rows_only()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitorA, count: 3, ct);
        await SeedAsync(visitorB, count: 2, ct);

        using (var scope = Services.CreateScope())
        {
            var cascade = ResolveFieldCascadeStep(scope.ServiceProvider);
            await cascade.ExecuteAsync(visitorA, ct);
        }

        Count(visitorA).Should().Be(0);
        Count(visitorB).Should().Be(2);
    }

    private static IAnonymizationCascadeStep ResolveFieldCascadeStep(IServiceProvider sp) =>
        sp.GetServices<IAnonymizationCascadeStep>()
          .Single(s => s.GetType().Name == "AnalyzerFormFieldEventCascadeStep");

    private async Task SeedAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = NewIdentity(visitor);
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

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);
}
