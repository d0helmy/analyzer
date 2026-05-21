using Analyzer.Features.Reporting.Application;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace Analyzer.Features.Reporting.Web;

/// <summary>
/// Slice 008 — read-side management endpoint backing the per-content-
/// node Analytics content app. Route:
/// <c>GET /umbraco/management/api/v1/analyzer/content-analytics/{contentKey}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Behavioural contract pinned in
/// <c>contracts/AnalyzerContentAnalyticsManagementController.md</c>.
/// 200 with snapshot when the GUID is known to capture OR cache;
/// 404 problem-details when neither knows it. No audit logging on
/// the read path (Principle VII).
/// </para>
/// </remarks>
[ApiController]
[VersionedApiBackOfficeRoute(AnalyzerApiConstants.ApiName)]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(AnalyzerApiConstants.ApiName)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = AnalyzerApiConstants.ApiName)]
public sealed class AnalyzerContentAnalyticsManagementController : ControllerBase
{
    internal const string ProblemTypeNotFound =
        "https://docs.umbraco.com/problem-details/analyzer/content-analytics/not-found";

    private readonly IContentAnalyticsQueryService _queryService;

    public AnalyzerContentAnalyticsManagementController(
        IContentAnalyticsQueryService queryService)
    {
        _queryService = queryService;
    }

    [HttpGet(Constants.ManagementApi.ContentAnalyticsPath + "/{contentKey:guid}")]
    [MapToApiVersion("1.0")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        [FromRoute] Guid contentKey,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";

        var snapshot = await _queryService
            .GetAsync(contentKey, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            var problem = new ProblemDetails
            {
                Type = ProblemTypeNotFound,
                Title = "Content node not found",
                Status = StatusCodes.Status404NotFound,
                Detail = "No content node or capture data found for the supplied contentKey.",
            };
            problem.Extensions["contentKey"] = contentKey;
            return new ObjectResult(problem)
            {
                StatusCode = StatusCodes.Status404NotFound,
                ContentTypes = { "application/problem+json" },
            };
        }

        return Ok(snapshot);
    }
}
