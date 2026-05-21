using System.Reflection;
using Customizer.Features.Visitors.Persistence;
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
/// <item>Testcontainers fallback — spins up a <em>single</em>
/// process-shared <c>mcr.microsoft.com/mssql/server</c> container and
/// gives each test class its own catalog inside it (#53 — Docker
/// concurrency conflicts when xUnit v3's parallel runner tried to
/// spin up one container per class). First run pulls ~1.5 GB;
/// subsequent runs reuse the image. Testcontainers' Ryuk reaper
/// disposes the container on process exit.</item>
/// </list>
/// <para>
/// Tagged <c>Category=Integration</c> so unit-test runs skip these by
/// default. CI invokes both traits explicitly.
/// </para>
/// </remarks>
[Collection("AnalyzerIntegration")]
public abstract class AnalyzerIntegrationTestBase : IAsyncLifetime
{
    private const string EnvConnectionString = "ConnectionStrings__umbracoDbDSN";

    private static MsSqlContainer? _sharedContainer;
    private static readonly SemaphoreSlim _sharedContainerLock = new(1, 1);

    private WebApplicationFactory<Program>? _factory;
    private string? _perClassCatalog;
    private string? _serverConnectionString;

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
        var envConnectionString = Environment.GetEnvironmentVariable(EnvConnectionString);
        string connectionString;

        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            // Aspire AppHost mode — operator-owned warm container with a
            // persistent catalog. Reset the schema between classes so each
            // class starts clean.
            connectionString = envConnectionString;
            await ResetSchemaAsync(connectionString);
        }
        else
        {
            // Testcontainers mode — share one container across the test
            // process, give each class its own catalog. #53.
            _serverConnectionString = await EnsureSharedContainerAsync();
            _perClassCatalog = $"Analyzer_{Guid.NewGuid():N}";
            var csb = new SqlConnectionStringBuilder(_serverConnectionString)
            {
                InitialCatalog = _perClassCatalog,
            };
            await CreateCatalogAsync(_serverConnectionString, _perClassCatalog);
            connectionString = csb.ConnectionString;
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                // Umbraco 17.3.5 registers several *EventAuthorizer singletons
                // that consume the scoped IAuthorizationService — invalid under
                // ASP.NET Core's Development-mode scope validation that
                // WebApplicationFactory turns on by default. Pin to Production
                // so the host boots; integration tests still exercise the
                // same real persistence + scope semantics, just without the
                // upstream DI registration mismatch blocking boot.
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:umbracoDbDSN"] = connectionString,
                        ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "Microsoft.Data.SqlClient",
                        ["Umbraco:CMS:Unattended:InstallUnattended"] = "true",
                        // #52 — Umbraco.Forms (referenced by Analyzer.Host but not by
                        // Customizer.Host) ships a PackageMigrationPlan. With the
                        // default PackageMigrationsUnattended=true Umbraco enters
                        // Level=Upgrading and pushes our migration components onto the
                        // async UnattendedUpgradeBackgroundService, which under
                        // WebApplicationFactory either doesn't complete or takes >60s.
                        // Forcing it to false makes Umbraco stay at Level=Run and fire
                        // the component pipeline synchronously on first boot so
                        // CustomizerMigrationComponent + AnalyzerMigrationComponent
                        // create the analyzer*/customizer* tables before any test
                        // method runs. Umbraco.Forms's own migration is irrelevant for
                        // these tests and stays pending — harmless.
                        ["Umbraco:CMS:Unattended:PackageMigrationsUnattended"] = "false",
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

        if (_serverConnectionString is not null && _perClassCatalog is not null)
        {
            // Drop the per-class catalog so the shared container doesn't
            // accumulate dead Analyzer_<guid> databases across runs.
            try
            {
                await DropCatalogAsync(_serverConnectionString, _perClassCatalog);
            }
            catch
            {
                // Best-effort cleanup. The shared container is reaped on
                // process exit by Testcontainers' Ryuk, so a leaked catalog
                // dies with the container anyway.
            }
        }
    }

    /// <summary>
    /// Inserts a minimal <c>customizerVisitorProfile</c> row so any
    /// visitor-keyed insert in <c>analyzerSession</c> /
    /// <c>analyzerCustomEvent</c> / <c>analyzerEventReceipt</c> can
    /// satisfy its FK to <c>customizerVisitorProfile(key)</c>. Tests
    /// generate visitor Guids inline and don't otherwise touch
    /// Customizer's visitor pipeline; this helper plugs the seed gap
    /// without dragging in the full Customizer identity-resolution path.
    /// </summary>
    /// <remarks>
    /// Idempotent — re-seeding the same key is a no-op so callers can
    /// safely seed once per Guid without coordinating across helpers.
    /// </remarks>
    protected async Task SeedVisitorProfileAsync(Guid visitorKey)
    {
        using var scope = ScopeProvider.CreateScope();
        var exists = scope.Database.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM customizerVisitorProfile WHERE [key] = @0",
            visitorKey);
        if (exists == 0)
        {
            var now = DateTime.UtcNow;
            await scope.Database.InsertAsync(new VisitorProfileDto
            {
                Key = visitorKey,
                IdentityRef = $"oid:{visitorKey:N}",
                VisitCount = 1,
                ProfileCreatedUtc = now,
                LastSeenUtc = now,
                IsAnonymized = false,
                AnonymizedActorKey = null,
                AnonymizedUtc = null,
                RowVersion = 1,
                CreatedUtc = now,
                UpdatedUtc = now,
                CreatedActorKey = Guid.Empty,
                UpdatedActorKey = Guid.Empty,
            }).ConfigureAwait(false);
        }
        scope.Complete();
    }

    /// <summary>
    /// Starts (or returns the already-started) shared MSSQL container for
    /// this test process. Concurrent callers race on a semaphore so only
    /// one container is built no matter how many xUnit collections start
    /// in parallel. Container is reaped on process exit by Testcontainers'
    /// Ryuk — no explicit dispose path needed.
    /// </summary>
    private static async Task<string> EnsureSharedContainerAsync()
    {
        if (_sharedContainer is not null)
        {
            return _sharedContainer.GetConnectionString();
        }

        await _sharedContainerLock.WaitAsync();
        try
        {
            if (_sharedContainer is null)
            {
                var container = new MsSqlBuilder().Build();
                await container.StartAsync();
                _sharedContainer = container;
            }
            return _sharedContainer.GetConnectionString();
        }
        finally
        {
            _sharedContainerLock.Release();
        }
    }

    /// <summary>
    /// Creates a new SQL Server catalog named <paramref name="catalog"/>
    /// against the server reachable via <paramref name="serverConnectionString"/>
    /// (whose <c>Initial Catalog</c> may be anything; the command runs
    /// against <c>master</c>).
    /// </summary>
    private static async Task CreateCatalogAsync(string serverConnectionString, string catalog)
    {
        var csb = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "master" };
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{catalog.Replace("]", "]]")}];";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops the catalog created by <see cref="CreateCatalogAsync"/>.
    /// </summary>
    private static async Task DropCatalogAsync(string serverConnectionString, string catalog)
    {
        var csb = new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = "master" };
        await using var conn = new SqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'{catalog.Replace("'", "''")}') " +
            $"BEGIN ALTER DATABASE [{catalog.Replace("]", "]]")}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{catalog.Replace("]", "]]")}]; END;";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Drops + recreates the warm Aspire catalog so each class starts
    /// from a clean schema. Used only in env-var (Aspire) mode; the
    /// Testcontainers path gives each class its own catalog and skips
    /// this entirely.
    /// </summary>
    private static async Task ResetSchemaAsync(string connectionString)
    {
        var csb = new SqlConnectionStringBuilder(connectionString);
        var targetDb = csb.InitialCatalog;
        if (string.IsNullOrWhiteSpace(targetDb)
            || string.Equals(targetDb, "master", StringComparison.OrdinalIgnoreCase))
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
