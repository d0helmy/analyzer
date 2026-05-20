using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace Analyzer.Migrations;

/// <summary>
/// Runs <see cref="AnalyzerMigrationPlan"/> on Umbraco startup so the
/// <c>analyzerEventReceipt</c> table exists before any subscriber
/// dispatches a write. Mirrors Customizer's
/// <c>CustomizerMigrationComponent</c> shape verbatim.
/// </summary>
public sealed class AnalyzerMigrationComponent : IAsyncComponent
{
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IMigrationPlanExecutor _executor;
    private readonly IKeyValueService _keyValueService;
    private readonly IRuntimeState _runtimeState;
    private readonly ILogger<AnalyzerMigrationComponent> _logger;

    public AnalyzerMigrationComponent(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor executor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState,
        ILogger<AnalyzerMigrationComponent> logger)
    {
        _scopeProvider = scopeProvider;
        _executor = executor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
        _logger = logger;
    }

    public async Task InitializeAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AnalyzerMigrationComponent.InitializeAsync invoked: RuntimeLevel={RuntimeLevel}, IsRestarting={IsRestarting}",
            _runtimeState.Level, isRestarting);

        var plan = new AnalyzerMigrationPlan();
        var upgrader = new Upgrader(plan);
        await upgrader.ExecuteAsync(_executor, _scopeProvider, _keyValueService);

        _logger.LogInformation("AnalyzerMigrationComponent migration plan execution completed");
    }

    public Task TerminateAsync(bool isShuttingDown, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
