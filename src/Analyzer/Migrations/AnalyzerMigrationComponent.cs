using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Migrations;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Migrations.Upgrade;

namespace Analyzer.Migrations;

/// <summary>
/// Runs <see cref="AnalyzerMigrationPlan"/> on Umbraco startup so the
/// <c>analyzerEventReceipt</c> table exists before any subscriber
/// dispatches a write. The <see cref="Upgrader"/> tracks the executed
/// migration set in <c>umbracoKeyValue</c> and is a no-op when nothing
/// is pending, so running unconditionally on every boot is safe.
/// </summary>
/// <remarks>
/// #28: previously gated on <c>_runtimeState.Level &lt; RuntimeLevel.Run</c>,
/// which is a chicken-and-egg trap on fresh databases: Umbraco parks
/// the runtime at <c>Level=Upgrading</c> precisely because this plan
/// is pending, and the gate then refuses to run the plan that would
/// unblock <c>Run</c>. #28's slice-007 T053 evidence shows that
/// dropping the gate lets <c>InitializeAsync</c> fire at
/// <c>Level=Upgrading</c> and the <see cref="Upgrader"/> then creates
/// the analyzer + customizer tables. The notification-handler
/// refactor #28 also proposes was tried and empirically didn't help
/// (<c>UmbracoApplicationStartedNotification</c> only fires at Run,
/// which is the level we're stuck out of).
/// </remarks>
public sealed class AnalyzerMigrationComponent : IAsyncComponent
{
    private readonly ICoreScopeProvider _scopeProvider;
    private readonly IMigrationPlanExecutor _executor;
    private readonly IKeyValueService _keyValueService;

    public AnalyzerMigrationComponent(
        ICoreScopeProvider scopeProvider,
        IMigrationPlanExecutor executor,
        IKeyValueService keyValueService)
    {
        _scopeProvider = scopeProvider;
        _executor = executor;
        _keyValueService = keyValueService;
    }

    public async Task InitializeAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        var plan = new AnalyzerMigrationPlan();
        var upgrader = new Upgrader(plan);
        await upgrader.ExecuteAsync(_executor, _scopeProvider, _keyValueService);
    }

    public Task TerminateAsync(bool isShuttingDown, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
