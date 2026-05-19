using Analyzer.Features.Scroll.Domain;

namespace Analyzer.Features.Scroll.Application;

/// <summary>
/// Slice 006 — orchestrates one in-request scroll-milestone capture.
/// Public seam that <c>AnalyzerScrollEventManagementController</c>
/// depends on, so the implementing class can keep its internal-typed
/// dependencies (resolver, repository, state-store, auditor) hidden
/// from the public surface.
/// </summary>
/// <remarks>
/// Pinning scope: lives in <c>Analyzer.Features.Scroll.Application</c>,
/// outside the pinned <c>Analyzer.Analytics</c> namespace. Replacement
/// implementations are out of scope at slice 006; this interface is
/// the controller's compile-time seam, not an extension point.
/// </remarks>
public interface IAnalyzerScrollEventCaptureHandler
{
    /// <summary>
    /// Identity gate → payload validation → session resolution → repo
    /// insert → state-store append → audit. Returns the new row's
    /// <c>eventKey</c>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// When the actor's identity is unavailable or
    /// <see cref="Guid.Empty"/>. Controller maps to 401/403.
    /// </exception>
    /// <exception cref="AnalyzerScrollPayloadValidationException">
    /// When semantic payload invariants fail. Controller maps to 400.
    /// </exception>
    /// <exception cref="ScrollSampleDuplicateException">
    /// When the unique-index
    /// <c>UX_analyzerScrollSample_pageviewBucket</c> rejects the row
    /// because a same-tuple entry already exists. Controller maps to
    /// 409 and the auditor emits a <c>Duplicate</c>-tagged entry; the
    /// state store is NOT mutated (no row landed for this request).
    /// </exception>
    Task<Guid> HandleAsync(AnalyzerScrollEventCapture command, CancellationToken ct);
}
