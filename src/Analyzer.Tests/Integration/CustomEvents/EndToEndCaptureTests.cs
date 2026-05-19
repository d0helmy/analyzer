using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T019 (US1 AS1, AS2, AS4) — end-to-end persistence:
/// driving the capture handler N times for one visitor produces N
/// <c>analyzerCustomEvent</c> rows, all attached to the same session,
/// ordered by <c>receivedUtc</c>. Parameterised across SC-001's
/// per-visitor throughput envelope.
/// </summary>
/// <remarks>
/// Drives the handler via DI scope (slice-002 integration-test pattern)
/// rather than via HTTP — assertions stay focused on persistence +
/// state-store semantics. HTTP-layer auth/anti-forgery rejections are
/// covered by unit tests on the controller + the framework-level
/// guarantees Umbraco's <c>[Authorize]</c> + anti-forgery filter
/// provide.
/// </remarks>
[Trait("Category", "Integration")]
public sealed class EndToEndCaptureTests : AnalyzerIntegrationTestBase
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task N_captures_for_one_visitor_persist_N_rows_under_same_session(int n)
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = NewIdentity(visitor);
        var ct = TestContext.Current.CancellationToken;

        var eventKeys = new List<Guid>();
        for (int i = 0; i < n; i++)
        {
            using var scope = Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>();
            var receivedUtc = DateTimeOffset.UtcNow.AddMilliseconds(i * 10);
            var eventKey = await handler.HandleAsync(
                new CustomEventCapture(
                    Actor: actor,
                    Category: "engagement",
                    Action: "click",
                    Label: $"label-{i}",
                    Value: null,
                    UserAgent: "UA/test",
                    ReceivedUtc: receivedUtc),
                ct);
            eventKeys.Add(eventKey);
        }

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(n);
        rows.Select(r => r.EventKey).Should().BeEquivalentTo(eventKeys);
        rows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1,
            "all captures within the inactivity window must attach to the same session");
        rows.Select(r => r.ReceivedUtc).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Multiple_visitors_produce_disjoint_rows()
    {
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitorA);
        await SeedVisitorProfileAsync(visitorB);
        var ct = TestContext.Current.CancellationToken;

        await CaptureAsync(visitorA, ct);
        await CaptureAsync(visitorA, ct);
        await CaptureAsync(visitorB, ct);

        var aRows = ReadRows(visitorA);
        var bRows = ReadRows(visitorB);
        aRows.Should().HaveCount(2);
        bRows.Should().HaveCount(1);
        aRows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1);
        bRows.Select(r => r.SessionKey).Distinct().Should().HaveCount(1);
        aRows.Select(r => r.SessionKey).First().Should().NotBe(
            bRows.Select(r => r.SessionKey).First());
    }

    private async Task CaptureAsync(Guid visitor, CancellationToken ct)
    {
        using var scope = Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>();
        await handler.HandleAsync(
            new CustomEventCapture(
                Actor: NewIdentity(visitor),
                Category: "engagement",
                Action: "click",
                Label: null,
                Value: null,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);
    }

    private List<RowProjection> ReadRows(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var rows = scope.Database.Fetch<RowProjection>(
            $"SELECT eventKey AS EventKey, sessionKey AS SessionKey, receivedUtc AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerCustomEvent} " +
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
        public Guid SessionKey { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }
}
