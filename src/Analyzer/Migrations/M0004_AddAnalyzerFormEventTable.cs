using Analyzer.Features.Forms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 005 — creates the <c>analyzerFormEvent</c> table (data-model
/// §3.1). Idempotent via <see cref="MigrationBase.TableExists"/> guard;
/// re-runnable on re-deploy.
/// </summary>
/// <remarks>
/// <para>
/// Hard FK to <c>customizerVisitorProfile(key)</c> and the composite
/// lifecycle index <c>(visitorProfileKey, formKey, sessionKey, eventType)</c>
/// are declared via raw SQL — NPoco's <c>[Index]</c> attribute is
/// single-column, and importing Customizer's internal
/// <c>VisitorProfileDto</c> would breach Principle III.
/// </para>
/// <para>
/// SQLite skips the FK + composite-index declarations (slice-002 lesson
/// #39). Application-layer guarantees plus the single-instance dev path
/// suffice; CI runs against SQL Server via Testcontainers.
/// </para>
/// </remarks>
public sealed class M0004_AddAnalyzerFormEventTable : AsyncMigrationBase
{
    public M0004_AddAnalyzerFormEventTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerFormEvent) is false)
        {
            Create.Table<AnalyzerFormEventDto>().Do();

            if (!isSqlite)
            {
                // FK to customizerVisitorProfile.key
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerFormEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerFormEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                // Composite lifecycle index drives the
                // ListUnclosedStartsForSessionsAsync query path + the
                // SC-004 cascade-DELETE budget on visitorProfileKey.
                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerFormEvent_lifecycle] " +
                    $"ON [{Constants.Database.AnalyzerFormEvent}] " +
                    "([visitorProfileKey], [formKey], [sessionKey], [eventType])");
            }
        }

        return Task.CompletedTask;
    }
}
