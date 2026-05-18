using Analyzer.Features.Events.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 002 — creates Analyzer's first owned table:
/// <c>analyzerEventReceipt</c>. One row per processed
/// <c>PageviewCaptured</c> notification. Idempotent via
/// <see cref="MigrationBase.TableExists"/> guard; re-runnable on
/// re-deploy.
/// </summary>
/// <remarks>
/// FK to <c>customizerVisitorProfile(key)</c> is declared via raw SQL
/// rather than the NPoco <c>[ForeignKey]</c> attribute so the migration
/// does not import Customizer's internal
/// <c>VisitorProfileDto</c> (Constitution Principle III). Soft FK to
/// <c>customizerPageview(key)</c> is intentionally absent — Customizer
/// may drop the parent pageview row under back-pressure (FR-025),
/// so a hard FK would fail in those cases (data-model §1).
/// </remarks>
public sealed class M0001_AddAnalyzerEventReceiptTable : AsyncMigrationBase
{
    public M0001_AddAnalyzerEventReceiptTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        if (TableExists(Constants.Database.AnalyzerEventReceipt) is false)
        {
            Create.Table<AnalyzerEventReceiptDto>().Do();

            var provider = Database.DatabaseType.GetProviderName();
            var isSqlite = provider?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

            // SQLite enforces FKs only when PRAGMA foreign_keys = ON
            // (off by default in older runtimes). Umbraco's SQLite path
            // doesn't reliably enable it across hosting configs, so we
            // skip the FK declaration on SQLite and rely on the
            // application-layer guarantee (Customizer publishes only
            // for resolved VisitorProfileKey).
            if (!isSqlite)
            {
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerEventReceipt}] " +
                    $"ADD CONSTRAINT [FK_analyzerEventReceipt_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");
            }
        }

        return Task.CompletedTask;
    }
}
