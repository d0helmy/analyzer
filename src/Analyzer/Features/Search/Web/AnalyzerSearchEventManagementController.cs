using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Web.Common.Authorization;

namespace Analyzer.Features.Search.Web;

/// <summary>
/// Slice 007 — management endpoint for the per-pageview internal-
/// search submission capture. Mirrors slice-004's
/// <c>AnalyzerCustomEventController</c> + slice-006's
/// <c>AnalyzerScrollEventManagementController</c> four-corner
/// Principle-VII gate (auth + anti-forgery + validation + audit).
/// Route: <c>POST /umbraco/management/api/v1/analyzer/search-event</c>.
/// </summary>
/// <remarks>
/// <para>
/// No 409 / duplicate path — search events have no idempotency unique
/// index per research §R7 (re-running the same search is a distinct
/// engagement signal per spec Edge Cases).
/// </para>
/// <para>
/// <b>Visitor-bound <c>pageviewKey</c> check</b> (research §R3 +
/// FR-008): the handler verifies the supplied <c>pageviewKey</c>
/// belongs to the resolved visitor. Strengthens defence vs slice 006
/// because search queries are PII per FR-SRC-04.
/// </para>
/// <para>
/// <b>Audit-log PII redaction</b> (FR-009 + SC-006): the per-success
/// audit-log entry carries no <c>rawQuery</c> or <c>normalisedQuery</c>
/// — only correlation identifiers, actor, and result count.
/// </para>
/// </remarks>
[ApiController]
[VersionedApiBackOfficeRoute(AnalyzerApiConstants.ApiName)]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(AnalyzerApiConstants.ApiName)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = AnalyzerApiConstants.ApiName)]
public sealed class AnalyzerSearchEventManagementController : ControllerBase
{
    private readonly IVisitorIdentifier _visitorIdentifier;
    private readonly IAnalyzerSearchEventCaptureHandler _handler;
    private readonly TimeProvider _timeProvider;

    public AnalyzerSearchEventManagementController(
        IVisitorIdentifier visitorIdentifier,
        IAnalyzerSearchEventCaptureHandler handler,
        TimeProvider timeProvider)
    {
        _visitorIdentifier = visitorIdentifier;
        _handler = handler;
        _timeProvider = timeProvider;
    }

    [HttpPost("search-event")]
    [ProducesResponseType<AnalyzerSearchEventResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Capture(
        [FromBody] AnalyzerSearchEventPayload payload,
        CancellationToken cancellationToken)
    {
        var actor = _visitorIdentifier.GetCurrent();
        if (!actor.IsAvailable || actor.Key == Guid.Empty)
        {
            return Unauthorized();
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        // Trim defensively — domain MUST NOT trust upstream validation
        // (Principle VII). The client also trims before POSTing.
        var rawQuery = (payload.Query ?? string.Empty).Trim();

        // ContentKey is server-set inside the handler from the
        // validated pageview lookup (defends against forged content-key
        // correlations per controller contract). The command's
        // ContentKey slot is unused on the inbound path.
        var command = new AnalyzerSearchEventCapture(
            Actor: actor,
            PageviewKey: payload.PageviewKey,
            ContentKey: Guid.Empty,
            RawQuery: rawQuery,
            ResultCount: payload.ResultCount,
            UserAgent: userAgent,
            ReceivedUtc: _timeProvider.GetUtcNow());

        try
        {
            var projection = await _handler
                .HandleAsync(command, cancellationToken)
                .ConfigureAwait(false);
            return Accepted(new AnalyzerSearchEventResponse { EventKey = projection.EventKey });
        }
        catch (AnalyzerSearchPayloadValidationException ex)
        {
            ModelState.AddModelError(ex.PropertyName, ex.Message);
            return BadRequest(new ValidationProblemDetails(ModelState));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
    }
}
