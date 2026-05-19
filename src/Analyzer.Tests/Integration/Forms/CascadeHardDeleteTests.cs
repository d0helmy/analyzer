using System.Diagnostics;
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
/// Slice 005 / T038 (US2 — anonymisation cascade for the form-event
/// table). Running the cascade step hard-deletes the visitor's
/// <c>analyzerFormEvent</c> rows without touching other visitors;
/// latency assertion confirms the SC-004 budget (≤ 200 ms for 1 000
/// rows) via the indexed <c>visitorProfileKey</c> predicate.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CascadeHardDeleteTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Cascade_deletes_target_visitor_rows_only()
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
            var cascade = ResolveFormEventCascadeStep(scope.ServiceProvider);
            await cascade.ExecuteAsync(visitorA, ct);
        }

        Count(visitorA).Should().Be(0);
        Count(visitorB).Should().Be(2);
    }

    [Fact]
    public async Task Cascade_completes_under_two_hundred_ms_for_one_thousand_rows()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        await SeedAsync(visitor, count: 1_000, ct);

        using var scope = Services.CreateScope();
        var cascade = ResolveFormEventCascadeStep(scope.ServiceProvider);

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
        var cascade = ResolveFormEventCascadeStep(scope.ServiceProvider);
        await cascade.ExecuteAsync(visitor, ct);

        Count(visitor).Should().Be(0);
    }

    private static IAnonymizationCascadeStep ResolveFormEventCascadeStep(IServiceProvider sp) =>
        sp.GetServices<IAnonymizationCascadeStep>()
          .Single(s => s.GetType().Name == "AnalyzerFormEventCascadeStep");

    private async Task SeedAsync(Guid visitor, int count, CancellationToken ct)
    {
        var actor = NewIdentity(visitor);
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

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);
}
