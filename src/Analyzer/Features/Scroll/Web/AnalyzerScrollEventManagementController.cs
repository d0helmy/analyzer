using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Scroll.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Analyzer.Features.Scroll.Web;

/// <summary>
/// Slice 006 — management endpoint for the per-pageview scroll-
/// milestone capture (25 / 50 / 75 / 100 %). Mirrors slice-004's
/// <c>AnalyzerCustomEventController</c> + slice-005's
/// <c>AnalyzerFormEventManagementController</c> four-corner
/// Principle-VII gate (auth + anti-forgery + validation + audit).
/// Route: <c>POST /umbraco/management/api/v1/analyzer/scroll-event/milestone</c>.
/// </summary>
[ApiController]
[BackOfficeRoute("analyzer/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(AnalyzerApiConstants.ApiName)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = AnalyzerApiConstants.ApiName)]
public sealed class AnalyzerScrollEventManagementController : ControllerBase
{
    private readonly IVisitorIdentifier _visitorIdentifier;
    private readonly IAnalyzerScrollEventCaptureHandler _handler;
    private readonly TimeProvider _timeProvider;

    public AnalyzerScrollEventManagementController(
        IVisitorIdentifier visitorIdentifier,
        IAnalyzerScrollEventCaptureHandler handler,
        TimeProvider timeProvider)
    {
        _visitorIdentifier = visitorIdentifier;
        _handler = handler;
        _timeProvider = timeProvider;
    }

    [HttpPost("scroll-event/milestone")]
    [ProducesResponseType<AnalyzerScrollEventResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<AnalyzerScrollEventDuplicateResponse>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Milestone(
        [FromBody] AnalyzerScrollEventPayload payload,
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

        var command = new AnalyzerScrollEventCapture(
            Actor: actor,
            PageviewKey: payload.PageviewKey,
            ContentKey: payload.ContentKey,
            Bucket: payload.Bucket,
            UserAgent: userAgent,
            ReceivedUtc: _timeProvider.GetUtcNow());

        try
        {
            var eventKey = await _handler
                .HandleAsync(command, cancellationToken)
                .ConfigureAwait(false);
            return Accepted(new AnalyzerScrollEventResponse { EventKey = eventKey });
        }
        catch (AnalyzerScrollPayloadValidationException ex)
        {
            ModelState.AddModelError(ex.PropertyName, ex.Message);
            return BadRequest(new ValidationProblemDetails(ModelState));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized();
        }
        catch (ScrollSampleDuplicateException)
        {
            // 409 idempotency-rejection — the visitor already crossed
            // this (pageview, bucket) tuple; the unique-index
            // UX_analyzerScrollSample_pageviewBucket prevented a
            // duplicate row. Handler has already audited as Duplicate.
            return Conflict(new AnalyzerScrollEventDuplicateResponse());
        }
    }
}
