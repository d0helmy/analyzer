using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Sessions.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.CustomEvents;

/// <summary>
/// Slice 004 / T020 (US1 AS3, SC-002) — when a custom-event POST
/// arrives after the inactivity window has lapsed, the resolver
/// lazy-closes the old session + opens a new one. Exactly two
/// <c>analyzerSession</c> rows; the custom-event row attaches to the
/// new session.
/// </summary>
[Trait("Category", "Integration")]
public sealed class LazyCloseSessionTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Capture_after_inactivity_window_opens_new_session()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        var actor = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var ct = TestContext.Current.CancellationToken;

        // Step 1 — capture a first event so a session opens.
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(NewCommand(actor, DateTimeOffset.UtcNow), ct);
        }
        var firstSession = ReadSingleSessionKey(visitor);

        // Step 2 — simulate long inactivity by back-dating the session's
        // lastActivityUtc; this is the test substitute for waiting past
        // the configured inactivity window.
        BackdateSessionLastActivity(firstSession, hoursAgo: 2);

        // Step 3 — capture a fresh event. The resolver's cache may
        // still hold the entry, but the cached entry's lastActivityUtc
        // is the in-memory value (also pre-backdate). We invalidate by
        // creating a new resolver scope; or rely on cache-miss path
        // since the cache is in-memory and won't know we backdated.
        // The resolver's stale-cache-hit path closes + opens new.
        // Either way, the *outcome* in the DB is what matters: two
        // session rows + the new event attaches to the second.
        using (var scope = Services.CreateScope())
        {
            // Force a fresh cache lookup by invalidating directly.
            scope.ServiceProvider
                .GetRequiredService<AnalyzerSessionCacheStore>()
                .InvalidateByVisitorKey(visitor);

            await scope.ServiceProvider.GetRequiredService<ICustomEventCaptureHandler>()
                .HandleAsync(NewCommand(actor, DateTimeOffset.UtcNow), ct);
        }

        var sessionCount = ReadSessionCount(visitor);
        sessionCount.Should().Be(2, "the resolver should have lazy-closed the stale row + opened a new one");

        var (mostRecentEventSession, _) = ReadLatestCustomEvent(visitor);
        mostRecentEventSession.Should().NotBe(firstSession);
    }

    private static CustomEventCapture NewCommand(VisitorIdentity actor, DateTimeOffset utc) =>
        new(
            Actor: actor,
            Category: "engagement",
            Action: "click",
            Label: null,
            Value: null,
            UserAgent: "UA/test",
            ReceivedUtc: utc);

    private Guid ReadSingleSessionKey(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var key = scope.Database.Single<Guid>(
            $"SELECT sessionKey FROM {Constants.Database.AnalyzerSession} " +
            $"WHERE visitorProfileKey = @0", visitor);
        scope.Complete();
        return key;
    }

    private void BackdateSessionLastActivity(Guid sessionKey, int hoursAgo)
    {
        using var scope = ScopeProvider.CreateScope();
        scope.Database.Execute(
            $"UPDATE {Constants.Database.AnalyzerSession} " +
            $"SET lastActivityUtc = @0 " +
            $"WHERE sessionKey = @1",
            DateTimeOffset.UtcNow.AddHours(-hoursAgo),
            sessionKey);
        scope.Complete();
    }

    private int ReadSessionCount(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerSession} " +
            $"WHERE visitorProfileKey = @0", visitor);
        scope.Complete();
        return count;
    }

    private (Guid SessionKey, DateTimeOffset ReceivedUtc) ReadLatestCustomEvent(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var row = scope.Database.Single<EventRow>(
            $"SELECT TOP (1) sessionKey AS SessionKey, receivedUtc AS ReceivedUtc " +
            $"FROM {Constants.Database.AnalyzerCustomEvent} " +
            $"WHERE visitorProfileKey = @0 " +
            $"ORDER BY receivedUtc DESC",
            visitor);
        scope.Complete();
        return (row.SessionKey, row.ReceivedUtc);
    }

    private sealed class EventRow
    {
        public Guid SessionKey { get; set; }
        public DateTimeOffset ReceivedUtc { get; set; }
    }
}
