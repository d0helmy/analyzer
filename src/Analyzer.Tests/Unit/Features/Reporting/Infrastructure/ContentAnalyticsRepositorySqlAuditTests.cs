using System.Reflection;
using Analyzer.Features.Reporting.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Reporting.Infrastructure;

/// <summary>
/// Slice 008 / T046 — defends the privacy invariant at the SQL
/// boundary: the repository's hard-coded query string must never
/// reference <c>identityRef</c>. A refactor that accidentally JOINs
/// through <c>customizerVisitorProfile.identityRef</c> would
/// breach FR-RPT-009 + SC-005 even if the C# DTO assertions still
/// pass.
/// </summary>
public sealed class ContentAnalyticsRepositorySqlAuditTests
{
    [Fact]
    public void Repository_sql_does_not_reference_identityRef_column()
    {
        var sql = typeof(ContentAnalyticsRepository)
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as string;

        sql.Should().NotBeNullOrEmpty(
            "ContentAnalyticsRepository.Sql is the canonical query string and must remain inspectable for this audit");

        sql!.ToLowerInvariant().Should().NotContain("identityref",
            "the projection MUST never select or join on identityRef — that column is PII per FR-RPT-009");
    }

    [Fact]
    public void Repository_sql_selects_visitorProfileFk_for_distinct_visitor_count()
    {
        var sql = typeof(ContentAnalyticsRepository)
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null) as string;

        sql!.ToLowerInvariant().Should().Contain("visitorprofilefk",
            "unique visitor count must use the surrogate FK (anonymisation-preserved per SC-004)");
        sql.ToLowerInvariant().Should().Contain("count(distinct visitorprofilefk)",
            "the DISTINCT count must use the surrogate FK column");
    }
}
