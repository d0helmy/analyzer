using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Sessions;

/// <summary>
/// Slice 004 / T013 — DB-rooted behavioural assertions for the new
/// <see cref="IAnalyzerSessionRepository.TouchAsync"/> method
/// (Clarification §1; data-model §10). Lives under Integration/ because
/// the spec's three assertions ("advances lastActivityUtc; doesn't bump
/// pageviewCount; idempotent on already-closed rows") all depend on
/// real SQL semantics — NPoco's <c>IScopeProvider</c> cannot be faked
/// without re-implementing the persistence story.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnalyzerSessionRepositoryTouchTests : AnalyzerIntegrationTestBase
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TouchAsync_advances_lastActivityUtc_without_changing_pageviewCount()
    {
        var visitor = Guid.NewGuid();
        var sessionKey = await SeedActiveSessionAsync(visitor, deviceKey: "dev1", pageviewCount: 3);

        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAnalyzerSessionRepository>();
        var advanced = T0.AddMinutes(10);

        await repo.TouchAsync(sessionKey, advanced, TestContext.Current.CancellationToken);

        var (lastActivity, pageviewCount, isActive) = ReadRow(sessionKey);
        lastActivity.Should().BeCloseTo(advanced, TimeSpan.FromSeconds(1));
        pageviewCount.Should().Be(3);
        isActive.Should().BeTrue();
    }

    [Fact]
    public async Task TouchAsync_is_idempotent_on_already_closed_rows()
    {
        var visitor = Guid.NewGuid();
        var sessionKey = await SeedActiveSessionAsync(visitor, deviceKey: "dev2", pageviewCount: 1);

        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAnalyzerSessionRepository>();

        await repo.CloseAsync(sessionKey, T0.AddMinutes(30), TestContext.Current.CancellationToken);

        // Touch against the now-closed row — no-op (WHERE isActive = 1
        // filters us out); must not throw and must not re-open.
        await repo.TouchAsync(sessionKey, T0.AddMinutes(45), TestContext.Current.CancellationToken);

        var (_, _, isActive) = ReadRow(sessionKey);
        isActive.Should().BeFalse();
    }

    private async Task<Guid> SeedActiveSessionAsync(Guid visitor, string deviceKey, int pageviewCount)
    {
        using var scope = Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAnalyzerSessionRepository>();
        var sessionKey = Guid.NewGuid();
        await repo.InsertAsync(
            new AnalyzerSessionDto
            {
                Id = Guid.NewGuid(),
                SessionKey = sessionKey,
                VisitorProfileKey = visitor,
                DeviceKey = deviceKey,
                StartUtc = T0,
                LastActivityUtc = T0,
                EndUtc = null,
                PageviewCount = pageviewCount,
                IsActive = true,
                AnonymizedUtc = null,
            },
            TestContext.Current.CancellationToken);
        return sessionKey;
    }

    private (DateTimeOffset LastActivityUtc, int PageviewCount, bool IsActive) ReadRow(Guid sessionKey)
    {
        using var scope = ScopeProvider.CreateScope();
        var row = scope.Database.Single<RowProjection>(
            $"SELECT lastActivityUtc AS LastActivityUtc, pageviewCount AS PageviewCount, isActive AS IsActive " +
            $"FROM {Constants.Database.AnalyzerSession} WHERE sessionKey = @0",
            sessionKey);
        scope.Complete();
        return (row.LastActivityUtc, row.PageviewCount, row.IsActive);
    }

    private sealed class RowProjection
    {
        public DateTimeOffset LastActivityUtc { get; set; }
        public int PageviewCount { get; set; }
        public bool IsActive { get; set; }
    }
}
