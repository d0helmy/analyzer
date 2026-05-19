using Analyzer.Features.Forms.Domain;

namespace Analyzer.Features.Forms.Application;

/// <summary>
/// Slice 005 — orchestrates one in-request form-lifecycle capture
/// (<c>Impression</c> / <c>Start</c> / <c>Success</c>). Public seam
/// that <c>AnalyzerFormEventManagementController</c> depends on, so
/// the implementing class can keep its internal-typed dependencies
/// (resolver, repository, state-store, auditor) hidden from the
/// public surface.
/// </summary>
/// <remarks>
/// Pinning scope: lives in <c>Analyzer.Features.Forms.Application</c>,
/// outside the pinned <c>Analyzer.Analytics</c> namespace. Replacement
/// implementations are out of scope at slice 005; this interface is
/// the controller's compile-time seam, not an extension point.
/// </remarks>
public interface IAnalyzerFormEventCaptureHandler
{
    /// <summary>
    /// Identity gate → payload validation → session resolution → repo
    /// insert → state-store append → audit. Returns the new row's
    /// <c>eventKey</c>.
    /// </summary>
    Task<Guid> HandleAsync(AnalyzerFormEventCapture command, CancellationToken ct);
}
