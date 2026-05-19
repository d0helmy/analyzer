using Analyzer.Features.Forms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 005 — creates the <c>analyzerFormFieldEvent</c> table
/// (data-model §3.2). Idempotent via
/// <see cref="MigrationBase.TableExists"/> guard.
/// </summary>
/// <remarks>
/// <para>
/// Two composite indexes are declared via raw SQL:
/// <list type="bullet">
///   <item>
///     <c>IDX_analyzerFormFieldEvent_perField</c> on
///     <c>(formKey, fieldKey, eventType)</c> — drives per-field
///     interaction reports.
///   </item>
///   <item>
///     <c>IDX_analyzerFormFieldEvent_cascadeProbe</c> on
///     <c>(visitorProfileKey, formKey, sessionKey)</c> — drives the
///     SC-004 200ms cascade-DELETE budget for 1000 rows.
///   </item>
/// </list>
/// </para>
/// </remarks>
public sealed class M0005_AddAnalyzerFormFieldEventTable : AsyncMigrationBase
{
    public M0005_AddAnalyzerFormFieldEventTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerFormFieldEvent) is false)
        {
            Create.Table<AnalyzerFormFieldEventDto>().Do();

            if (!isSqlite)
            {
                // FK to customizerVisitorProfile.key
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerFormFieldEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerFormFieldEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerFormFieldEvent_perField] " +
                    $"ON [{Constants.Database.AnalyzerFormFieldEvent}] " +
                    "([formKey], [fieldKey], [eventType])");

                Database.Execute(
                    "CREATE NONCLUSTERED INDEX [IDX_analyzerFormFieldEvent_cascadeProbe] " +
                    $"ON [{Constants.Database.AnalyzerFormFieldEvent}] " +
                    "([visitorProfileKey], [formKey], [sessionKey])");
            }
        }

        return Task.CompletedTask;
    }
}
