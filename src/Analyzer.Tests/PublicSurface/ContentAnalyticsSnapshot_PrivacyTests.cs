using Analyzer.Reporting.ContentAnalytics;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.PublicSurface;

/// <summary>
/// Slice 008 / T014 — pins the privacy invariant of
/// <see cref="ContentAnalyticsSnapshot"/>: no property name may
/// substring-match a reserved identity field token. Future slices
/// that need to surface per-visitor data MUST gate that addition
/// behind <c>IIndividualDataAccessCheck</c> AND extend the reserved
/// list here together with the success-criterion update.
/// </summary>
public sealed class ContentAnalyticsSnapshot_PrivacyTests
{
    private static readonly string[] ReservedIdentityTokens =
    {
        "upn",
        "oid",
        "email",
        "identityref",
        "displayname",
    };

    [Fact]
    public void Snapshot_property_names_contain_no_reserved_identity_tokens()
    {
        var offenders = typeof(ContentAnalyticsSnapshot)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => ReservedIdentityTokens
                .Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        offenders.Should().BeEmpty(
            "ContentAnalyticsSnapshot must not expose per-visitor identity data. " +
            "If a future slice needs to surface such a field, gate it behind " +
            "IIndividualDataAccessCheck and update both ReservedIdentityTokens here " +
            "and the SC-005 acceptance criteria together.");
    }
}
