using Analyzer.Features.Search.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 007 — creates the <c>analyzerSearchEvent</c> table (data-model
/// §3.1). Idempotent via <see cref="MigrationBase.TableExists"/> guard;
/// re-runnable on re-deploy.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server branch additionally declares:
/// <list type="bullet">
///   <item>
///     <c>FK_analyzerSearchEvent_VisitorProfile</c> → hard FK to
///     <c>customizerVisitorProfile(key)</c>. Raw SQL — importing
///     Customizer's internal <c>VisitorProfileDto</c> would breach
///     Principle III.
///   </item>
///   <item>
///     <c>FK_analyzerSearchEvent_Session</c> → hard FK to
///     <c>analyzerSession(sessionKey)</c>. Search events resolve a
///     session synchronously per slice-003 contract; sessionKey is
///     NOT NULL.
///   </item>
///   <item>
///     <c>CK_analyzerSearchEvent_resultCount</c> CHECK constraint
///     enforcing <c>resultCount &gt;= 0</c> — DB-layer defence against
///     a buggy handler bypassing model validation.
///   </item>
///   <item>
///     <c>CK_analyzerSearchEvent_rawQueryLength</c> +
///     <c>CK_analyzerSearchEvent_normalisedQueryLength</c> CHECK
///     constraints enforcing length 1-256 on both query columns —
///     defence in depth against a custom normaliser that collapses
///     input to nothing.
///   </item>
/// </list>
/// </para>
/// <para>
/// SQLite skips the FK + CHECK declarations (slice-002 lesson #39).
/// Application-layer guarantees plus the single-instance dev path
/// suffice; CI runs against SQL Server via Testcontainers.
/// </para>
/// </remarks>
public sealed class M0007_AddAnalyzerSearchEventTable : AsyncMigrationBase
{
    public M0007_AddAnalyzerSearchEventTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerSearchEvent) is false)
        {
            Create.Table<AnalyzerSearchEventDto>().Do();

            if (!isSqlite)
            {
                // FK to customizerVisitorProfile.key — hard FK; the
                // cascade step deletes Analyzer's rows before Customizer
                // re-keys the profile row, so referential integrity
                // holds end-to-end through anonymisation.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSearchEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerSearchEvent_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                // FK to analyzerSession.sessionKey — search-event capture
                // resolves a session synchronously per slice-003
                // contract; sessionKey is NOT NULL (data-model §1.1).
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSearchEvent}] " +
                    $"ADD CONSTRAINT [FK_analyzerSearchEvent_Session] " +
                    $"FOREIGN KEY ([sessionKey]) " +
                    $"REFERENCES [analyzerSession]([sessionKey])");

                // CHECK — resultCount must be non-negative.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSearchEvent}] " +
                    $"ADD CONSTRAINT [CK_analyzerSearchEvent_resultCount] " +
                    $"CHECK ([resultCount] >= 0)");

                // CHECK — rawQuery length within (0, 256].
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSearchEvent}] " +
                    $"ADD CONSTRAINT [CK_analyzerSearchEvent_rawQueryLength] " +
                    $"CHECK (LEN([rawQuery]) BETWEEN 1 AND 256)");

                // CHECK — normalisedQuery length within (0, 256]. Defence
                // in depth against a custom IAnalyzerSearchQueryNormaliser
                // that collapses input to an empty string.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerSearchEvent}] " +
                    $"ADD CONSTRAINT [CK_analyzerSearchEvent_normalisedQueryLength] " +
                    $"CHECK (LEN([normalisedQuery]) BETWEEN 1 AND 256)");
            }
        }

        return Task.CompletedTask;
    }
}
