using System.Text.Json;
using Analyzer.Features.Reporting.Application;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.Reporting;

/// <summary>
/// Slice 008 / T044 + T045 — pins SC-004 + SC-005:
/// anonymisation re-keys <c>customizerVisitorProfile.identityRef</c>
/// but does not delete the row, so <c>visitorProfileFk</c>
/// references survive. <c>COUNT(DISTINCT visitorProfileFk)</c> is
/// unchanged by anonymisation, and the response JSON never carries
/// any identity field at the wire boundary.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnonymisedVisitorAggregateTests : ReportingIntegrationTestBase
{
    [Fact]
    public async Task AnonymisationDoesNotReduceUniqueVisitors()
    {
        var contentKey = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var visitors = new List<Guid>();

        for (var i = 0; i < 10; i++)
        {
            var v = Guid.NewGuid();
            visitors.Add(v);
            await SeedSessionAsync(v, now.AddDays(-30), now);
            await SeedPageviewAsync(contentKey, v, now.AddDays(-1));
        }

        // Re-key the first three visitors' identityRef to the anonymised
        // form. The integer FK is unchanged — the pageview rows still
        // point at the same surrogate.
        for (var i = 0; i < 3; i++)
        {
            await SeedAnonymisedVisitorProfileAsync(visitors[i]);
        }

        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();
        var snapshot = await query.GetAsync(contentKey, TestContext.Current.CancellationToken);

        snapshot.Should().NotBeNull();
        snapshot!.UniqueVisitors30d.Should().Be(10,
            "anonymised visitors must continue to contribute to the unique count");
        snapshot.Pageviews30d.Should().Be(10);
    }

    [Fact]
    public async Task ResponseContainsNoIdentityFields()
    {
        var contentKey = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            var v = Guid.NewGuid();
            await SeedSessionAsync(v, now.AddDays(-30), now);
            await SeedPageviewAsync(contentKey, v, now.AddHours(-1));
            if (i < 2)
            {
                await SeedAnonymisedVisitorProfileAsync(v);
            }
        }

        var query = Services.GetRequiredService<IContentAnalyticsQueryService>();
        var snapshot = await query.GetAsync(contentKey, TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();

        var json = JsonSerializer.Serialize(snapshot);
        var reservedTokens = new[] { "upn", "oid", "useremail", "identityref" };
        foreach (var token in reservedTokens)
        {
            json.ToLowerInvariant().Should().NotContain(token,
                $"serialised snapshot must not contain the reserved identity token '{token}'");
        }
    }
}
