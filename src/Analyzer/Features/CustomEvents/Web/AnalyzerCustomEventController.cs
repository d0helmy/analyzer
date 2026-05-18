using Asp.Versioning;
using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Authorization;
using Umbraco.Cms.Web.Common.Routing;

namespace Analyzer.Features.CustomEvents.Web;

/// <summary>
/// Slice 004 — first Analyzer-owned management endpoint. Accepts a
/// page-script <c>analyzer.send(...)</c> POST, validates the payload,
/// resolves the active session, persists one
/// <c>analyzerCustomEvent</c> row + updates the request-scoped state
/// store, emits an audit-log entry, and returns HTTP 202 with the new
/// <c>eventKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// Four-corner Principle VII gate: (a) <see cref="AuthorizeAttribute"/>
/// against Umbraco's <see cref="AuthorizationPolicies.BackOfficeAccess"/>
/// rejects anonymous → 401; (b) Umbraco's management-API conventions
/// apply anti-forgery automatically; (c) <see cref="ApiControllerAttribute"/>
/// triggers automatic ModelState 400 ProblemDetails; (d)
/// <see cref="ICustomEventAuditor"/> emits a structured audit-log entry
/// per successful capture (FR-008).
/// </para>
/// <para>
/// Route resolves to <c>management/api/v1/analyzer/custom-event</c>
/// under Umbraco's backoffice-API root. Placeholder per spec
/// Assumption — slice 005 may pin a canonical Analyzer namespace
/// prefix; the JS-side client wrapper centralises the URL so a future
/// rename is a single-line client change.
/// </para>
/// </remarks>
[ApiController]
[BackOfficeRoute("analyzer/api/v{version:apiVersion}")]
[Authorize(Policy = AuthorizationPolicies.BackOfficeAccess)]
[MapToApi(AnalyzerApiConstants.ApiName)]
[ApiVersion("1.0")]
[ApiExplorerSettings(GroupName = AnalyzerApiConstants.ApiName)]
public sealed class AnalyzerCustomEventController : ControllerBase
{
    private readonly IVisitorIdentifier _visitorIdentifier;
    private readonly ICustomEventCaptureHandler _handler;
    private readonly TimeProvider _timeProvider;

    public AnalyzerCustomEventController(
        IVisitorIdentifier visitorIdentifier,
        ICustomEventCaptureHandler handler,
        TimeProvider timeProvider)
    {
        _visitorIdentifier = visitorIdentifier;
        _handler = handler;
        _timeProvider = timeProvider;
    }

    [HttpPost("custom-event")]
    [ProducesResponseType<CustomEventResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Capture(
        [FromBody] CustomEventPayload payload,
        CancellationToken cancellationToken)
    {
        // [ApiController] handles the model-state 400 + ProblemDetails
        // automatically; we still need a manual whitespace-only guard
        // because [Required(AllowEmptyStrings = false)] + StringLength
        // do NOT reject pure-whitespace strings.
        if (string.IsNullOrWhiteSpace(payload.Category) ||
            string.IsNullOrWhiteSpace(payload.Action))
        {
            ModelState.AddModelError(
                string.IsNullOrWhiteSpace(payload.Category) ? nameof(payload.Category) : nameof(payload.Action),
                "Field must contain non-whitespace characters.");
            return BadRequest(new ValidationProblemDetails(ModelState));
        }

        var actor = _visitorIdentifier.GetCurrent();
        if (!actor.IsAvailable || actor.Key == Guid.Empty)
        {
            // Defensive: should not happen for an authenticated request,
            // but keep the contract tight — no row, no audit, no leak.
            return Unauthorized();
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            userAgent = null;
        }

        var command = new CustomEventCapture(
            Actor: actor,
            Category: payload.Category,
            Action: payload.Action,
            Label: payload.Label,
            Value: payload.Value,
            UserAgent: userAgent,
            ReceivedUtc: _timeProvider.GetUtcNow());

        var eventKey = await _handler.HandleAsync(command, cancellationToken)
            .ConfigureAwait(false);

        return Accepted(new CustomEventResponse { EventKey = eventKey });
    }
}
