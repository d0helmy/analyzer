using Analyzer.Features.CustomEvents.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 004 — creates the <c>analyzerCustomEvent</c> table. Idempotent
/// via <see cref="MigrationBase.TableExists"/> guard; re-runnable on
/// re-deploy.
/// </summary>
/// <remarks>
/// <para>
/// Hard FKs to <c>analyzerSession(sessionKey)</c> (first
/// Analyzer-to-Analyzer hard FK) and to
/// <c>customizerVisitorProfile(key)</c>, the composite
/// <c>(category, action)</c> index, and the
/// <c>value decimal(18, 4)</c> precision are declared via raw SQL:
/// NPoco's <c>[Index]</c> attribute is single-column; NPoco/Umbraco
/// expose no precision attribute for decimals; importing Customizer's
/// internal <c>VisitorProfileDto</c> would breach Principle III.
/// </para>
/// <para>
/// SQLite skips the FK + precision declarations (lesson #39) — the
/// FK + composite-index raw SQL uses SQL-Server-only syntax and SQLite
/// doesn't carry NULL/precision constraints the same way. Application-
/// layer guarantees plus the single-instance dev path suffice; CI runs
/// against SQL Server via Testcontainers.
/// </para>
/// </remarks>
public sealed class M0003_AddAnalyzerCustomEventTable : AsyncMigrationBase
{
    public M0003_AddAnalyzerCustomEventTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerCustomEvent) is false)
        {
            Create.Table<AnalyzerCustomEventDto>().Do();

            if (!isSqlite)
            {
                // FK to customizerVisitorProfile.key
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerCustomEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                // FK to analyzerSession.sessionKey — first Analyzer-to-Analyzer hard FK.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerCustomEvent_Session] " +
                    $"FOREIGN KEY ([sessionKey]) " +
                    $"REFERENCES [{Constants.Database.AnalyzerSession}]([sessionKey])");

                // Composite index for "events by category" aggregation (slice 010+).
                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerCustomEvent_category_action] " +
                    $"ON [{Constants.Database.AnalyzerCustomEvent}] ([category], [action])");

                // decimal(18, 4) precision on `value` — NPoco's default
                // mapping varies by provider; pin explicitly.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerCustomEvent}] " +
                    "ALTER COLUMN [value] DECIMAL(18, 4) NULL");
            }
        }

        return Task.CompletedTask;
    }
}
