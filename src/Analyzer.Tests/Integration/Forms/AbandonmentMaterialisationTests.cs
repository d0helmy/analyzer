using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Application.Abandonment;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts.Anonymization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Forms;

/// <summary>
/// Slice 005 / T040 — abandonment materialiser conformance against a
/// real SQL Server. Covers the six contract items from
/// <c>specs/005-forms-tracking/contracts/AnalyzerFormAbandonmentMaterialiser.md</c>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AbandonmentMaterialisationTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task OneAbandonPerOpenLifecycle()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var sessionKey = await SeedStartedLifecycleAsync(visitor, formKey, ct);
        var closeUtc = DateTimeOffset.UtcNow;

        await MaterialiseAsync(new[] { sessionKey }, closeUtc, ct);

        var abandons = ReadAbandons(visitor);
        abandons.Should().HaveCount(1);
        abandons[0].SessionKey.Should().Be(sessionKey);
        abandons[0].FormKey.Should().Be(formKey);
        abandons[0].ReceivedUtc.Should().BeCloseTo(closeUtc, TimeSpan.FromSeconds(1));
        abandons[0].ElapsedMsFromStart.Should().NotBeNull().And.BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task NoAbandonWhenSuccessRecorded()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var sessionKey = await SeedCompletedLifecycleAsync(visitor, formKey, ct);

        await MaterialiseAsync(new[] { sessionKey }, DateTimeOffset.UtcNow, ct);

        ReadAbandons(visitor).Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotentAcrossSweeps()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var sessionKey = await SeedStartedLifecycleAsync(visitor, formKey, ct);

        await MaterialiseAsync(new[] { sessionKey }, DateTimeOffset.UtcNow, ct);
        await MaterialiseAsync(new[] { sessionKey }, DateTimeOffset.UtcNow.AddSeconds(5), ct);

        ReadAbandons(visitor).Should().HaveCount(1,
            "the Abandon-exclusion NOT EXISTS predicate must suppress double-materialisation");
    }

    [Fact]
    public async Task EmptyClosedBatch_is_noop()
    {
        var ct = TestContext.Current.CancellationToken;

        await MaterialiseAsync(Array.Empty<Guid>(), DateTimeOffset.UtcNow, ct);

        // No throw; no abandons inserted. Difficult to assert "no rows"
        // without a visitor scope — the call simply doesn't issue any SQL.
    }

    [Fact]
    public async Task SkipsAnonymisedVisitors()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var sessionKey = await SeedStartedLifecycleAsync(visitor, formKey, ct);

        // Run the cascade step to anonymise (hard-delete) the visitor's
        // form rows — emulates the "anonymised between Start and
        // session-close" edge case. After the cascade, the SELECT
        // returns nothing for this visitor, so the materialiser
        // emits zero Abandons.
        using (var diScope = Services.CreateScope())
        {
            var step = diScope.ServiceProvider
                .GetServices<IAnonymizationCascadeStep>()
                .Single(s => s.GetType().Name == "AnalyzerFormEventCascadeStep");
            await step.ExecuteAsync(visitor, ct);
        }

        await MaterialiseAsync(new[] { sessionKey }, DateTimeOffset.UtcNow, ct);

        ReadAbandons(visitor).Should().BeEmpty();
    }

    [Fact]
    public async Task ElapsedMsFromStart_populated_from_closeUtc_minus_startReceivedUtc()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var ct = TestContext.Current.CancellationToken;

        var formKey = Guid.NewGuid();
        var startUtc = DateTimeOffset.UtcNow;
        var sessionKey = await SeedStartedLifecycleAsync(visitor, formKey, ct, startUtc);

        var closeUtc = startUtc.AddSeconds(30);
        await MaterialiseAsync(new[] { sessionKey }, closeUtc, ct);

        var abandons = ReadAbandons(visitor);
        abandons.Should().HaveCount(1);
        abandons[0].ElapsedMsFromStart.Should().BeInRange(29_000, 31_000,
            "elapsedMsFromStart approximates (logicalCloseUtc - startReceivedUtc) ms");
    }

    private async Task MaterialiseAsync(
        IReadOnlyCollection<Guid> closedSessionKeys,
        DateTimeOffset closeUtc,
        CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var materialiser = scope.ServiceProvider
            .GetRequiredService<IAnalyzerFormAbandonmentMaterialiser>();
        await materialiser.MaterialiseAsync(closedSessionKeys, closeUtc, ct);
    }

    private async Task<Guid> SeedStartedLifecycleAsync(
        Guid visitor,
        Guid formKey,
        CancellationToken ct,
        DateTimeOffset? startUtc = null)
    {
        var actor = NewIdentity(visitor);
        var contentKey = Guid.NewGuid();
        var t0 = startUtc ?? DateTimeOffset.UtcNow;

        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Start, t0.AddMilliseconds(500),
            elapsedFromImpression: 500, elapsedFromStart: null, ct);

        return GetSessionKey(visitor);
    }

    private async Task<Guid> SeedCompletedLifecycleAsync(
        Guid visitor,
        Guid formKey,
        CancellationToken ct)
    {
        var actor = NewIdentity(visitor);
        var contentKey = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow;

        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Impression, t0, null, null, ct);
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Start, t0.AddMilliseconds(500),
            elapsedFromImpression: 500, elapsedFromStart: null, ct);
        await DispatchAsync(actor, formKey, contentKey,
            AnalyzerFormEventType.Success, t0.AddSeconds(3),
            elapsedFromImpression: null, elapsedFromStart: 2500, ct);

        return GetSessionKey(visitor);
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

    private Guid GetSessionKey(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var key = scope.Database.ExecuteScalar<Guid?>(
            $"SELECT TOP 1 sessionKey FROM {Constants.Database.AnalyzerFormEvent} " +
            $"WHERE visitorProfileKey = @0 AND eventType = 1 ORDER BY receivedUtc",
            visitor);
        scope.Complete();
        return key ?? throw new InvalidOperationException(
            $"No Start row found for visitor {visitor}; lifecycle was not seeded correctly.");
    }

    private List<AbandonRow> ReadAbandons(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<AbandonRow>(
            $"SELECT sessionKey AS SessionKey, formKey AS FormKey, " +
            $"       receivedUtc AS ReceivedUtc, elapsedMsFromStart AS ElapsedMsFromStart " +
            $"FROM {Constants.Database.AnalyzerFormEvent} " +
            $"WHERE visitorProfileKey = @0 AND eventType = 3 " +
            $"ORDER BY receivedUtc",
            visitor);
        scope.Complete();
        return rows;
    }

    private static VisitorIdentity NewIdentity(Guid key) =>
        new(IsAvailable: true, Key: key, Oid: "oid-1", Upn: "user@example.com", IsAnonymized: false);

    private sealed class AbandonRow
    {
        public Guid SessionKey { get; set; }
        public Guid FormKey { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
        public int? ElapsedMsFromStart { get; set; }
    }
}
