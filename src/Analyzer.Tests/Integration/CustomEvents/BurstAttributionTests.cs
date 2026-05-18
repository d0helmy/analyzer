using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T021 (US1 AS5) — two captures inside the inactivity
/// window attach to the same session, and the session's
/// <c>lastActivityUtc</c> advances WITHOUT bumping <c>pageviewCount</c>
/// (Clarification §1).
/// </summary>
[Trait("Category", "Integration")]
public sealed class BurstAttributionTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Two_consecutive_events_attach_to_same_session_and_advance_lastActivityUtc()
    {
        var visitor = Guid.NewGuid();
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var t0 = DateTimeOffset.UtcNow;
        var t1 = t0.AddSeconds(5);
        var ct = TestContext.Current.CancellationToken;

        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(NewCommand(actor, "click", t0), ct);
        }

        var (sessionKeyAfterFirst, pageviewCountAfterFirst, lastActivityAfterFirst) =
            ReadSession(visitor);

        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(NewCommand(actor, "scroll", t1), ct);
        }

        var (sessionKeyAfterSecond, pageviewCountAfterSecond, lastActivityAfterSecond) =
            ReadSession(visitor);

        sessionKeyAfterSecond.Should().Be(sessionKeyAfterFirst,
            "burst within inactivity window must attach to same session");
        pageviewCountAfterSecond.Should().Be(pageviewCountAfterFirst,
            "custom events must NOT increment pageviewCount (Clarification §1)");
        lastActivityAfterSecond.Should().BeAfter(lastActivityAfterFirst,
            "TouchAsync must advance lastActivityUtc");

        // Both events should be persisted.
        ReadCustomEventCount(visitor).Should().Be(2);
    }

    private static CustomEventCapture NewCommand(
        VisitorIdentity actor, string action, DateTimeOffset receivedUtc) =>
        new(
            Actor: actor,
            Category: "engagement",
            Action: action,
            Label: null,
            Value: null,
            UserAgent: "UA/test",
            ReceivedUtc: receivedUtc);

    private (Guid SessionKey, int PageviewCount, DateTimeOffset LastActivityUtc) ReadSession(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var row = scope.Database.Single<SessionRow>(
            $"SELECT sessionKey AS SessionKey, pageviewCount AS PageviewCount, lastActivityUtc AS LastActivityUtc " +
            $"FROM {Constants.Database.AnalyzerSession} " +
            $"WHERE visitorProfileKey = @0", visitor);
        scope.Complete();
        return (row.SessionKey, row.PageviewCount, row.LastActivityUtc);
    }

    private int ReadCustomEventCount(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerCustomEvent} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }

    private sealed class SessionRow
    {
        public Guid SessionKey { get; set; }
        public int PageviewCount { get; set; }
        public DateTimeOffset LastActivityUtc { get; set; }
    }
}
