using Analyzer.Analytics;
using Analyzer.Features.Search.Domain;

namespace Analyzer.Features.Search.Application;

/// <summary>
/// Slice 007 — orchestrates one in-request internal-search submission
/// capture. Public seam that
/// <c>AnalyzerSearchEventManagementController</c> depends on, so the
/// implementing class can keep its internal-typed dependencies
/// (normaliser, resolver, repository, state-store, auditor) hidden
/// from the public surface.
/// </summary>
/// <remarks>
/// Internal contract — not pinned (matches slice-006's
/// <c>IAnalyzerScrollEventCaptureHandler</c>'s internal-only
/// treatment). Replacement implementations are out of scope at slice
/// 007; this interface is the controller's compile-time seam, not an
/// extension point. The public extension point is
/// <see cref="IAnalyzerSearchQueryNormaliser"/>.
/// </remarks>
public interface IAnalyzerSearchEventCaptureHandler
{
    /// <summary>
    /// Identity gate → normalisation → visitor-bound pageview check →
    /// session resolution → repo insert → state-store append → audit.
    /// Returns the persisted <see cref="AnalyticsSearchEvent"/>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">
    /// When the actor's identity is unavailable or
    /// <see cref="Guid.Empty"/>. Controller maps to 401/403.
    /// </exception>
    /// <exception cref="AnalyzerSearchPayloadValidationException">
    /// When semantic payload invariants fail (empty raw query, empty
    /// normalised output, pageviewKey not bound to actor). Controller
    /// maps to 400.
    /// </exception>
    Task<AnalyticsSearchEvent> HandleAsync(
        AnalyzerSearchEventCapture command,
        CancellationToken ct);
}
