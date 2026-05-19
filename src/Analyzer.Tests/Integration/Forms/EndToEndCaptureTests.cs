using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Forms;

/// <summary>
/// Slice 005 / T037 (US1 AS1) — end-to-end per-form lifecycle
/// persistence. Driving the capture handler with Impression / Start /
/// Success commands for one visitor + form persists 3 rows attached
/// to the same session. Multi-visitor case asserts disjointness.
/// </summary>
/// <remarks>
/// HTTP-boundary verification of the management endpoint route
/// remains gated on issue #23 (slice-004 unresolved mgmt-API 404 in
/// the test host). Handler-level evidence is accepted per slice-004
/// precedent and provides the core lifecycle evidence at slice-005
/// ship time.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EndToEndCaptureTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Impression_Start_Success_persist_three_rows_under_same_session()
    {
        var visitor = Guid.NewGuid();
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Start, t0.AddMilliseconds(800),
            elapsedFromImpression: 800, elapsedFromStart: null, ct);
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Success, t0.AddMilliseconds(2500),
            elapsedFromImpression: null, elapsedFromStart: 1700, ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(3);
        rows.Select(r => r.EventType).Should().BeEquivalentTo(new byte[]
        {
            (byte)AnalyzerFormEventType.Impression,
            (byte)AnalyzerFormEventType.Start,
            (byte)AnalyzerFormEventType.Success,
        }, options => options.WithStrictOrdering());
        rows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1,
            "all three lifecycle rows in a single page lifecycle attach to the same session");
        rows.All(r => r.FormKey == formKey).Should().BeTrue();
        rows.All(r => r.ContentKey == contentKey).Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_visitors_produce_disjoint_rows()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var ct = TestContext.Current.CancellationToken;
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(NewIdentity(visitorA), formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);
        await DispatchAsync(NewIdentity(visitorA), formKey, contentKey,
            AnalyzerFormEventType.Start, t0.AddSeconds(1),
            elapsedFromImpression: 1000, elapsedFromStart: null, ct);
        await DispatchAsync(NewIdentity(visitorB), formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);

        ReadRows(visitorA).Should().HaveCount(2);
        ReadRows(visitorB).Should().HaveCount(1);
    }

    [Fact]
    public async Task Server_side_form_rejection_does_not_emit_Success()
    {
        // Simulates the Umbraco-Forms 4xx case: client dispatched
        // Impression + Start, but the form submit was rejected
        // server-side — client never dispatched Success. The
        // lifecycle stays at Start until the sweeper materialises
        // Abandon (covered by AbandonmentMaterialisationTests).
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var formKey = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var ct = TestContext.Current.CancellationToken;
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(NewIdentity(visitor), formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);
        await DispatchAsync(NewIdentity(visitor), formKey, contentKey,
            AnalyzerFormEventType.Start, t0.AddMilliseconds(500),
            elapsedFromImpression: 500, elapsedFromStart: null, ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(2);
        rows.Any(r => r.EventType == (byte)AnalyzerFormEventType.Success)
            .Should().BeFalse();
    }

    private async Task DispatchAsync(
        VisitorIdentity actor,
        Guid formKey,
        Guid contentKey,
        AnalyzerFormEventType eventType,
        DateTimeOffset receivedUtc,
        int? elapsedFromImpression,
        int? elapsedFromStart,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<IAnalyzerFormEventCaptureHandler>();
        await handler.HandleAsync(
            new AnalyzerFormEventCapture(
                Actor: actor,
                FormKey: formKey,
                ContentKey: contentKey,
                EventType: eventType,
                ElapsedMsFromImpression: elapsedFromImpression,
                ElapsedMsFromStart: elapsedFromStart,
                UserAgent: "UA/test",
                ReceivedUtc: receivedUtc),
            ct);
    }

    private List<RowProjection> ReadRows(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<RowProjection>(
            $"SELECT eventKey AS EventKey, sessionKey AS SessionKey, " +
            $"       formKey AS FormKey, contentKey AS ContentKey, " +
            $"       eventType AS EventType, receivedUtc AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerFormEvent} " +
            $"WHERE visitorProfileKey = @0 ORDER BY receivedUtc",
            visitor);
        scope.Complete();
        return rows;
    }

    private static VisitorIdentity NewIdentity(Guid key) => new(
        IsAvailable: true,
        Key: key,
        Oid: "oid-1",
        Upn: "user@example.com",
        IsAnonymized: false);

    private sealed class RowProjection
    {
        public Guid EventKey { get; set; }
        public Guid? SessionKey { get; set; }
        public Guid FormKey { get; set; }
        public Guid ContentKey { get; set; }
        public byte EventType { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }
}
