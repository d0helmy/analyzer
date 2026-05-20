using System.Security.Claims;
using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Application.Authorization;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Reporting.Application.Authorization;

/// <summary>
/// Slice 008 / T012 — covers the ten behavioural scenarios pinned in
/// <c>contracts/IIndividualDataAccessCheck.md § Test plan</c>.
/// </summary>
public sealed class DefaultIndividualDataAccessCheckTests
{
    private const string FallbackAlias = "Analytics.IndividualData";
    private const string CustomAlias = "Analytics.PerVisitor";

    [Fact]
    public void Principal_with_matching_role_claim_is_authorised()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(roleClaims: CustomAlias);

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Principal_with_multiple_role_claims_one_matching_is_authorised()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(
            roleClaims: new[] { "editor", CustomAlias, "admin" });

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Principal_with_no_role_claim_is_denied()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(roleClaims: Array.Empty<string>());

        sut.IsAuthorised(principal).Should().BeFalse();
    }

    [Fact]
    public void Principal_with_mismatching_role_claim_is_denied()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(roleClaims: "editor");

        sut.IsAuthorised(principal).Should().BeFalse();
    }

    [Fact]
    public void Configured_alias_is_honoured_when_set()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(roleClaims: CustomAlias);

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Null_alias_falls_back_to_default()
    {
        var sut = NewCheck(configuredAlias: null);
        var principal = NewAuthenticatedPrincipal(roleClaims: FallbackAlias);

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Whitespace_alias_falls_back_to_default()
    {
        var sut = NewCheck(configuredAlias: "   ");
        var principal = NewAuthenticatedPrincipal(roleClaims: FallbackAlias);

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Empty_alias_falls_back_to_default()
    {
        var sut = NewCheck(configuredAlias: string.Empty);
        var principal = NewAuthenticatedPrincipal(roleClaims: FallbackAlias);

        sut.IsAuthorised(principal).Should().BeTrue();
    }

    [Fact]
    public void Comparison_is_case_sensitive()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = NewAuthenticatedPrincipal(roleClaims: CustomAlias.ToUpperInvariant());

        sut.IsAuthorised(principal).Should().BeFalse();
    }

    [Fact]
    public void Anonymous_principal_is_denied()
    {
        var sut = NewCheck(configuredAlias: CustomAlias);
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        sut.IsAuthorised(principal).Should().BeFalse();
    }

    private static DefaultIndividualDataAccessCheck NewCheck(string? configuredAlias)
    {
        var options = Options.Create(new AnalyzerReportingOptions
        {
            IndividualDataUserGroupAlias = configuredAlias,
        });
        return new DefaultIndividualDataAccessCheck(options);
    }

    private static ClaimsPrincipal NewAuthenticatedPrincipal(string roleClaims)
        => NewAuthenticatedPrincipal(new[] { roleClaims });

    private static ClaimsPrincipal NewAuthenticatedPrincipal(IEnumerable<string> roleClaims)
    {
        var claims = roleClaims.Select(r => new Claim(ClaimTypes.Role, r));
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }
}
