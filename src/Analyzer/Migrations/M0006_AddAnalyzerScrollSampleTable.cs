using Analyzer.Features.Scroll.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Analyzer.Migrations;

/// <summary>
/// Slice 006 — creates the <c>analyzerScrollSample</c> table (data-model
/// §3.1). Idempotent via <see cref="MigrationBase.TableExists"/> guard;
/// re-runnable on re-deploy.
/// </summary>
/// <remarks>
/// <para>
/// SQL Server branch additionally declares:
/// <list type="bullet">
///   <item>
///     <c>FK_analyzerScrollSample_VisitorProfile</c> → hard FK to
///     <c>customizerVisitorProfile(key)</c>. Raw SQL — importing
///     Customizer's internal <c>VisitorProfileDto</c> would breach
///     Principle III.
///   </item>
///   <item>
///     <c>CK_analyzerScrollSample_bucket</c> CHECK constraint enforcing
///     <c>bucket IN (25, 50, 75, 100)</c> — DB-layer defence against a
///     buggy handler bypassing model validation.
///   </item>
///   <item>
///     <c>UX_analyzerScrollSample_pageviewBucket</c> unique non-clustered
///     index on <c>(pageviewKey, bucket)</c> — enforces FR-003
///     per-pageview-per-bucket idempotency at the DB layer
///     (complements the client-side per-bucket fire-once flag and the
///     handler-layer duplicate-detection path).
///   </item>
/// </list>
/// </para>
/// <para>
/// SQLite skips the FK + CHECK + composite-UX declarations (slice-002
/// lesson #39). Application-layer guarantees plus the single-instance
/// dev path suffice; CI runs against SQL Server via Testcontainers.
/// </para>
/// </remarks>
public sealed class M0006_AddAnalyzerScrollSampleTable : AsyncMigrationBase
{
    public M0006_AddAnalyzerScrollSampleTable(IMigrationContext context) : base(context)
    {
    }

    protected override Task MigrateAsync()
    {
        var providerName = Database.DatabaseType.GetProviderName();
        var isSqlite = providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        if (TableExists(Constants.Database.AnalyzerScrollSample) is false)
        {
            Create.Table<AnalyzerScrollSampleDto>().Do();

            if (!isSqlite)
            {
                // FK to customizerVisitorProfile.key
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerScrollSample}] " +
                    $"ADD CONSTRAINT [FK_analyzerScrollSample_VisitorProfile] " +
                    $"FOREIGN KEY ([visitorProfileKey]) " +
                    $"REFERENCES [customizerVisitorProfile]([key])");

                // CHECK constraint — bucket must be one of the four
                // permitted milestone values.
                Database.Execute(
                    $"ALTER TABLE [{Constants.Database.AnalyzerScrollSample}] " +
                    $"ADD CONSTRAINT [CK_analyzerScrollSample_bucket] " +
                    $"CHECK ([bucket] IN (25, 50, 75, 100))");

                // Unique index on (pageviewKey, bucket) — FR-003
                // idempotency at the DB layer. Slice-003's
                // UniqueConstraintViolationDetector discriminates this
                // from generic SQL errors so the handler can re-throw
                // as ScrollSampleDuplicateException.
                Database.Execute(
                    "CREATE UNIQUE NONCLUSTERED INDEX [UX_analyzerScrollSample_pageviewBucket] " +
                    $"ON [{Constants.Database.AnalyzerScrollSample}] " +
                    "([pageviewKey], [bucket])");
            }
        }

        return Task.CompletedTask;
    }
}
