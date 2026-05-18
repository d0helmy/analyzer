using Analyzer.Features.Sessions.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 003 — creates the <c>analyzerSession</c> table and adds an
/// additive nullable <c>sessionKey</c> column to slice-002's
/// <c>analyzerEventReceipt</c>. Idempotent via <see cref="MigrationBase.TableExists"/>
/// and <see cref="MigrationBase.ColumnExists"/> guards; re-runnable on
/// re-deploy.
/// </summary>
/// <remarks>
/// <para>
/// Hard FK to <c>customizerVisitorProfile(key)</c>, the partial unique
/// index <c>UX_analyzerSession_active_visitor_device</c>, and the
/// composite sweep index are declared via raw SQL (not NPoco
/// attributes) because (a) importing Customizer's internal
/// <c>VisitorProfileDto</c> would breach Principle III; (b) NPoco's
/// <c>[Index]</c> attribute doesn't model <c>WHERE</c> clauses.
/// </para>
/// <para>
/// SQLite skips the FK + partial unique index declarations (lesson #39):
/// SQLite's <c>PRAGMA foreign_keys</c> is unreliable across Umbraco
/// hosting configs, and NPoco's grammar doesn't reliably emit partial
/// unique indexes. The application-layer single-instance dev path is
/// sufficient there; CI exercises the real shape against SQL Server
/// via Testcontainers.
/// </para>
/// <para>
/// Pre-existing slice-002 <c>analyzerEventReceipt</c> rows keep
/// <c>sessionKey = null</c> — no back-fill (FR-004 pre-sessions cohort).
/// </para>
/// </remarks>
public sealed class M0002_AddAnalyzerSessionTableAndReceiptSessionKey : AsyncMigrationBase
{
    public M0002_AddAnalyzerSessionTableAndReceiptSessionKey(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        // 1) Create analyzerSession (NPoco-driven via DTO).
        if (TableExists(Constants.Database.AnalyzerSession) is false)
        {
            Create.Table<AnalyzerSessionDto>().Do();

            if (!isSqlite)
            {
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSession}] " +
                    $"ADD CONSTRAINT [FK_analyzerSession_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                Database.Execute(
                    "CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerSession_active_visitor_device] " +
                    $"ON [{Constants.Database.AnalyzerSession}] ([visitorProfileKey], [deviceKey]) " +
                    "WHERE [isActive] = 1");

                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerSession_sweep] " +
                    $"ON [{Constants.Database.AnalyzerSession}] ([isActive], [lastActivityUtc])");
            }
        }

        // 2) Add sessionKey column + index to analyzerEventReceipt.
        if (ColumnExists(Constants.Database.AnalyzerEventReceipt, "sessionKey") is false)
        {
            Alter.Table(Constants.Database.AnalyzerEventReceipt)
                 .AddColumn("sessionKey")
                 .AsGuid()
                 .Nullable()
                 .Do();

            Create.Index("IDX_analyzerEventReceipt_sessionKey")
                  .OnTable(Constants.Database.AnalyzerEventReceipt)
                  .OnColumn("sessionKey")
                  .Ascending()
                  .WithOptions()
                  .NonClustered()
                  .Do();
        }

        return Task.CompletedTask;
    }
}
