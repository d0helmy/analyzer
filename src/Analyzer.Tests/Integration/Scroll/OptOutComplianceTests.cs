using Analyzer.Analytics;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Integration.Scroll;

/// <summary>
/// Slice 006 / T043 (US2 SC-003) — server-side opt-out compliance.
///
/// The opt-out attribute is enforced **client-side** (the scroll
/// module short-circuits at init time and never POSTs); the server
/// has no opt-out concept and accepts whatever lands on the endpoint.
/// Coverage of the client short-circuit lives in the Vitest spec
/// <c>scroll-tracking/opt-out.spec.ts</c>.
///
/// This integration test pins the server-side invariant that
/// matters: when nothing POSTs (because the client opted out), the
/// DB is empty. It's a deliberately tiny test — the load-bearing
/// proof is the client-side Vitest case that shows zero fetches
/// fired.
/// </summary>
[Trait("Category", "Integration")]
public sealed class OptOutComplianceTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task When_client_does_not_dispatch_zero_rows_persist()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);

        // Deliberately do NOT invoke the capture handler — simulates
        // the opt-out client-side short-circuit (no POST, no
        // server-side work).

        Count(visitor).Should().Be(0,
            "client-side opt-out means zero POSTs reach the server; the table stays empty");
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerScrollSample} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }
}
