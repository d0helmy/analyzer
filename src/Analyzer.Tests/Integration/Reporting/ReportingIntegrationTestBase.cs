using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Analyzer.Tests.TestHelpers;

namespace Analyzer.Tests.Integration.Reporting;

/// <summary>
/// Slice 008 — shared test base for the
/// <c>Analyzer.Tests.Integration.Reporting</c> namespace. Adds
/// reporting-specific seed helpers (anonymised visitor profile,
/// raw <c>customizerVisitorPageview</c> row insertion,
/// <c>analyzerSession</c> insertion) on top of
/// <see cref="AnalyzerIntegrationTestBase"/>.
/// </summary>
/// <remarks>
/// Pageview seeding goes through raw SQL rather than importing
/// Customizer's <c>PageviewDto</c> — Customizer's NPoco DTO requires
/// the full UTM column set and other fields irrelevant to this
/// slice. Raw INSERT keeps the slice's substrate touch minimal.
/// </remarks>
public abstract class ReportingIntegrationTestBase : AnalyzerIntegrationTestBase
{
    protected async Task SeedPageviewAsync(
        Guid contentKey,
        Guid visitorKey,
        DateTimeOffset requestUtc)
    {
        await SeedVisitorProfileAsync(visitorKey);
        using var scope = ScopeProvider.CreateScope();
        var visitorFk = scope.Database.ExecuteScalar<int>(
            "SELECT [id] FROM customizerVisitorProfile WHERE [key] = @0",
            visitorKey);
        scope.Database.Execute(
            "INSERT INTO [customizerVisitorPageview] " +
            "([key], [visitorProfileFk], [contentKey], [pageviewSegmentsJson], " +
            " [wasContentTombstoned], [requestUtc]) " +
            "VALUES (@0, @1, @2, '[]', 0, @3)",
            Guid.NewGuid(), visitorFk, contentKey, requestUtc.UtcDateTime);
        scope.Complete();
    }

    /// <summary>
    /// Seed one <c>analyzerSession</c> row whose
    /// (<paramref name="startUtc"/>, <paramref name="lastActivityUtc"/>)
    /// envelope encloses the pageviews this test expects to be
    /// counted toward the average-time-on-page calculation.
    /// </summary>
    protected async Task SeedSessionAsync(
        Guid visitorKey,
        DateTimeOffset startUtc,
        DateTimeOffset lastActivityUtc)
    {
        await SeedVisitorProfileAsync(visitorKey);
        var sessionKey = Guid.NewGuid();
        using var scope = ScopeProvider.CreateScope();
        await scope.Database.InsertAsync(new AnalyzerSessionDto
        {
            Id = Guid.NewGuid(),
            SessionKey = sessionKey,
            VisitorProfileKey = visitorKey,
            DeviceKey = "test-device",
            StartUtc = startUtc,
            LastActivityUtc = lastActivityUtc,
            EndUtc = null,
            PageviewCount = 1,
            IsActive = true,
        }).ConfigureAwait(false);
        scope.Complete();
    }

    /// <summary>
    /// Seed a visitor whose <c>identityRef</c> has been re-keyed to
    /// the anonymised form Customizer's cascade produces. The integer
    /// FK is unchanged so historical pageviews still join.
    /// </summary>
    protected async Task SeedAnonymisedVisitorProfileAsync(Guid visitorKey)
    {
        await SeedVisitorProfileAsync(visitorKey);
        using var scope = ScopeProvider.CreateScope();
        scope.Database.Execute(
            "UPDATE customizerVisitorProfile SET [identityRef] = @0 WHERE [key] = @1",
            $"anonymized:{Guid.NewGuid():N}",
            visitorKey);
        scope.Complete();
    }
}
