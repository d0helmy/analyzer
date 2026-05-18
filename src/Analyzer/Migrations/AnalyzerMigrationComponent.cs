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

    public AnalyzerMigrationComponent(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor executor,
        IKeyValueService keyValueService,
        IRuntimeState runtimeState)
    {
        _scopeProvider = scopeProvider;
        _executor = executor;
        _keyValueService = keyValueService;
        _runtimeState = runtimeState;
    }

    public async Task InitializeAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        if (_runtimeState.Level < RuntimeLevel.Run)
        {
            return;
        }

        var plan = new AnalyzerMigrationPlan();
        var upgrader = new Upgrader(plan);
        await upgrader.ExecuteAsync(_executor, _scopeProvider, _keyValueService);
    }

    public Task TerminateAsync(bool isShuttingDown, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
