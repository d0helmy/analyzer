using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Xunit;

namespace Analyzer.Tests.Integration.HostBoot;

/// <summary>
/// Regression test for #52. Umbraco.Forms (referenced by Analyzer.Host) ships
/// a PackageMigrationPlan; without overriding the default
/// PackageMigrationsUnattended=true Umbraco enters Level=Upgrading and the
/// Analyzer/Customizer migration components never fire under
/// WebApplicationFactory. AnalyzerIntegrationTestBase pins
/// PackageMigrationsUnattended=false so Umbraco stays at Level=Run and our
/// components run on first boot. If this assertion ever fails, the test
/// suite will collapse with "Invalid object name 'customizerVisitorProfile'"
/// across ~all integration tests (analyzer#48).
/// </summary>
[Trait("Category", "Integration")]
public sealed class MigrationBootProbeTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public void Umbraco_boot_reaches_Run_and_analyzer_customizer_tables_exist()
    {
        var runtime = Services.GetRequiredService<IRuntimeState>();
        runtime.Level.Should().Be(RuntimeLevel.Run);

        using var scope = ScopeProvider.CreateScope();
        var tables = scope.Database.Fetch<string>(
            "SELECT name FROM sys.tables WHERE name LIKE 'analyzer%' OR name LIKE 'customizer%' ORDER BY name");
        scope.Complete();

        tables.Should().Contain("analyzerEventReceipt", "Analyzer migration component must fire on boot");
        tables.Should().Contain("customizerVisitorProfile", "Customizer migration component must fire on boot");
    }
}
