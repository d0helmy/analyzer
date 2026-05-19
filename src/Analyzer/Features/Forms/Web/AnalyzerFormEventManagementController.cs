using Analyzer.Features.Forms.Application;
using Analyzer.Features.Forms.Domain;
using Analyzer.Features.Visitors.Application.Contracts;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Analyzer.Features.Forms.Web;

/// <summary>
/// Slice 005 — management endpoint for the per-form lifecycle capture
/// (<c>Impression</c> / <c>Start</c> / <c>Success</c>). Mirrors slice
/// 004's <c>AnalyzerCustomEventController</c> four-corner Principle-VII
/// gate (auth + anti-forgery + validation + audit). Route:
/// <c>POST /umbraco/management/api/v1/analyzer/form-event/lifecycle</c>.
/// </summary>
/// <remarks>
/// Field-level capture (<c>FieldFocus</c> / <c>FieldUnfocus</c>) is
/// out of scope for US1; US2 will add a <c>/field</c> action and the
/// corresponding field handler. The controller class is sized for
/// both routes so US2 can extend in place without a second
/// composition.
/// </remarks>
[ApiController]
[BackOfficeRoute("analyzer/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(AnalyzerApiConstants.ApiName)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = AnalyzerApiConstants.ApiName)]
public sealed class AnalyzerFormEventManagementController : ControllerBase
{
    private readonly IVisitorIdentifier _visitorIdentifier;
    private readonly IAnalyzerFormEventCaptureHandler _lifecycleHandler;
    private readonly TimeProvider _timeProvider;

    public AnalyzerFormEventManagementController(
        IVisitorIdentifier visitorIdentifier,
        IAnalyzerFormEventCaptureHandler lifecycleHandler,
        TimeProvider timeProvider)
    {
        _visitorIdentifier = visitorIdentifier;
        _lifecycleHandler = lifecycleHandler;
        _timeProvider = timeProvider;
    }

    [HttpPost("form-event/lifecycle")]
    [ProducesResponseType<AnalyzerFormEventResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Lifecycle(
        [FromBody] AnalyzerFormEventPayload payload,
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

        var command = new AnalyzerFormEventCapture(
            Actor: actor,
            FormKey: payload.FormKey,
            ContentKey: payload.ContentKey,
            EventType: payload.EventType,
            ElapsedMsFromImpression: payload.ElapsedMsFromImpression,
            ElapsedMsFromStart: payload.ElapsedMsFromStart,
            UserAgent: userAgent,
            ReceivedUtc: _timeProvider.GetUtcNow());

        try
        {
            var eventKey = await _lifecycleHandler
                .HandleAsync(command, cancellationToken)
                .ConfigureAwait(false);
            return Accepted(new AnalyzerFormEventResponse { EventKey = eventKey });
        }
        catch (AnalyzerFormPayloadValidationException ex)
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
