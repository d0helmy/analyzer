using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Reporting.Application.Authorization;

/// <summary>
/// Default <see cref="IIndividualDataAccessCheck"/>. Resolves the
/// configured user-group alias from
/// <see cref="AnalyzerReportingOptions"/> (the post-configurator
/// substitutes the fallback when the option is empty) and matches
/// the principal's role claims with ordinal comparison.
/// </summary>
/// <remarks>
/// Umbraco's <c>BackOfficeClaimsPrincipalFactory</c> projects each of
/// the signed-in user's user-group aliases as an ASP.NET Core role
/// claim (claim type <see cref="ClaimTypes.Role"/>). Reading the role
/// claim avoids taking a hard dependency on Umbraco's user-manager
/// abstractions in the read-side gate.
/// </remarks>
internal sealed class DefaultIndividualDataAccessCheck : IIndividualDataAccessCheck
{
    private readonly IOptions<AnalyzerReportingOptions> _options;

    public DefaultIndividualDataAccessCheck(IOptions<AnalyzerReportingOptions> options)
    {
        _options = options;
    }

    public bool IsAuthorised(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var configured = _options.Value.IndividualDataUserGroupAlias;
        var groupAlias = string.IsNullOrWhiteSpace(configured)
            ? AnalyzerReportingOptionsPostConfigurator.FallbackGroupAlias
            : configured;

        foreach (var claim in principal.Claims)
        {
            if (string.Equals(claim.Type, ClaimTypes.Role, StringComparison.Ordinal)
                && string.Equals(claim.Value, groupAlias, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
