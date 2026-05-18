using Xunit;

namespace Analyzer.Tests.Integration.Sessions;

/// <summary>
/// Slice 003 — placeholder for the integration-test corpus listed in
/// <c>tasks.md</c> T016-T020b, T035, T036, T041-T044. These tests are
/// authored against slice-002's <c>AnalyzerIntegrationTestBase</c> +
/// Aspire / Testcontainers MSSQL substrate. They are tagged
/// <c>[Trait("Category", "Integration")]</c> so CI excludes them
/// (CI invocation: <c>-trait- "Category=Integration"</c>); local-dev
/// runs invoke them via the Aspire AppHost's persistent SQL container.
/// </summary>
/// <remarks>
/// <para>
/// This file holds one xUnit-discoverable test so the trait class is
/// reachable from the runner. The real test bodies (covering US1 AS1
/// through US3 AS4, SC-001 quantitative parameterisation,
/// race-safety, cascade rollback, sweeper logical-close-time, and
/// migration idempotency) land in a follow-up commit once the
/// Aspire AppHost is running locally and the assertions can be
/// validated against real SQL.
/// </para>
/// <para>
/// Cross-references:
/// </para>
/// <list type="bullet">
///   <item><c>specs/003-session-tracking/tasks.md</c> — T016-T020b, T035, T036, T041-T044</item>
///   <item><c>specs/003-session-tracking/contracts/AnalyzerSessionResolver.md</c> — conformance test table</item>
///   <item><c>specs/003-session-tracking/contracts/AnalyzerSessionCascadeStep.md</c> — conformance test table</item>
///   <item><c>specs/003-session-tracking/contracts/AnalyzerSessionSweeperService.md</c> — conformance test table</item>
/// </list>
/// </remarks>
public sealed class SessionIntegrationTestsPlaceholder
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Placeholder_authored_integration_tests_land_in_follow_up()
    {
        // Intentionally empty — see XML doc above. The Category=Integration
        // trait keeps this excluded from the PR-blocking CI run.
        Assert.True(true);
    }
}
