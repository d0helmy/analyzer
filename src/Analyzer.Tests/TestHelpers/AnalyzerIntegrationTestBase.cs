using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit;

namespace Analyzer.Tests.TestHelpers;

/// <summary>
/// Slice-002 integration-test base. Spins up an Umbraco host backed by
/// a real SQL Server (cascade-step atomic-rollback semantics under
/// <c>IScopeProvider</c> cannot be faithfully reproduced on SQLite —
/// research §7 + SC-006).
/// </summary>
/// <remarks>
/// <para>
/// Connection-string resolution order (research §7):
/// </para>
/// <list type="number">
/// <item><c>ConnectionStrings__umbracoDbDSN</c> environment variable —
/// preferred when the Aspire AppHost is already running locally; uses
/// the persistent volume so tests reuse the warm container.</item>
/// <item>Testcontainers fallback — spins up an ephemeral
/// <c>mcr.microsoft.com/mssql/server</c> container per test class
/// (first run pulls ~1.5 GB; subsequent runs reuse the image).</item>
/// </list>
/// <para>
/// Tagged <c>Category=Integration</c> so unit-test runs skip these by
/// default. CI invokes both traits explicitly.
/// </para>
/// </remarks>
public abstract class AnalyzerIntegrationTestBase : IAsyncLifetime
{
    private const string EnvConnectionString = "ConnectionStrings__umbracoDbDSN";

    private MsSqlContainer? _container;
    private WebApplicationFactory<Program>? _factory;

    static AnalyzerIntegrationTestBase() => PreloadTestHostAssemblies();

    /// <summary>
    /// Lazy-resolved when first read. Throws if accessed before
    /// <see cref="InitializeAsync"/> has run.
    /// </summary>
    protected IServiceProvider Services =>
        _factory?.Services
        ?? throw new InvalidOperationException(
            $"{nameof(AnalyzerIntegrationTestBase)} must be initialised via xUnit's IAsyncLifetime contract before resolving services.");

    /// <summary>
    /// Convenience accessor — opens scopes for cascade-step + repository
    /// tests that need NPoco scope semantics.
    /// </summary>
    protected IScopeProvider ScopeProvider =>
        Services.GetRequiredService<IScopeProvider>();

    public async ValueTask InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable(EnvConnectionString);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _container = new MsSqlBuilder().Build();
            await _container.StartAsync();
            connectionString = _container.GetConnectionString();
        }

        await ResetSchemaAsync(connectionString);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:umbracoDbDSN"] = connectionString,
                        ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.SqlClient",
                        ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                        ["Umbraco:CMS:Unattended:UnattendedUserName"] = "Analyzer Test Service Account",
                        ["Umbraco:CMS:Unattended:UnattendedUserEmail"] = "tests@analyzer.local",
                        ["Umbraco:CMS:Unattended:UnattendedUserPassword"] = "Analyzer-Test-123!",
                        ["Umbraco:CMS:Hosting:LocalTempStorageLocation"] = "EnvironmentTemp",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:0"] = "xunit",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:1"] = "Microsoft.AspNetCore.Mvc.Testing",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:2"] = "Microsoft.Testing",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:3"] = "FluentAssertions",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:4"] = "testhost",
                        ["Umbraco:CMS:TypeFinder:AdditionalAssemblyExclusionEntries:5"] = "Testcontainers",
                    });
                });
            });

        // Force host construction so Umbraco's migration pipeline runs
        // before any test method is invoked.
        _ = _factory.Services;
    }

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Drops + recreates the Umbraco schema between class fixtures so
    /// migration history is clean and the test base owns a fresh
    /// database. No-op when the container was just created (already
    /// empty); idempotent.
    /// </summary>
    private static async Task ResetSchemaAsync(string connectionString)
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var targetDb = csb.InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDb))
        {
            return;
        }

        csb.InitialCatalog = "master";
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{targetDb.Replace("'", "''")}') " +
            $"BEGIN ALTER DATABASE [{targetDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{targetDb}]; END;" +
            $"CREATE DATABASE [{targetDb}];";
        await cmd.ExecuteNonQueryAsync();
    }

    // Customizer slice-002 lesson: Umbraco's TypeFinder walks the
    // loaded-assembly graph at boot and Assembly.Load(name) fails for
    // xUnit / MTP / ASP.NET test-host DLLs that aren't listed in the
    // generated deps.json. LoadFrom(path) sidesteps deps.json
    // resolution and parks the assemblies in the load context so
    // subsequent Load(name) calls succeed.
    private static void PreloadTestHostAssemblies()
    {
        var dir = AppContext.BaseDirectory;
        foreach (var pattern in new[] { "xunit.*.dll", "Microsoft.AspNetCore.Mvc.Testing.dll", "Microsoft.Testing.*.dll", "testhost.dll" })
        {
            foreach (var path in Directory.EnumerateFiles(dir, pattern))
            {
                try
                {
                    Assembly.LoadFrom(path);
                }
                catch
                {
                    // Best-effort preload; failures surface elsewhere.
                }
            }
        }
    }
}
