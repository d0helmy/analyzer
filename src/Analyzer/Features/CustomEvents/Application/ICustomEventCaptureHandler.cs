namespace Analyzer.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 — orchestrates one in-request custom-event capture.
/// Public seam that <see cref="Web.AnalyzerCustomEventController"/>
/// depends on, so the implementing class can keep its internal-typed
/// dependencies (resolver, repository, state-store, auditor) hidden
/// from the public surface.
/// </summary>
/// <remarks>
/// Pinning scope: lives in <c>Analyzer.Features.CustomEvents.Application</c>,
/// outside the slice-002 pinned <c>Analyzer.Analytics</c> namespace.
/// Replacement implementations are out of scope at slice 004; this
/// interface is the controller's compile-time seam, not an extension
/// point.
/// </remarks>
public interface ICustomEventCaptureHandler
{
    /// <summary>
    /// Resolve the active session, insert the row, update the request-
    /// scoped state store, emit the audit-log entry. Returns the new
    /// row's <c>eventKey</c> for the HTTP 202 response body.
    /// </summary>
    Task<Guid> HandleAsync(CustomEventCapture command, CancellationToken ct);
}
