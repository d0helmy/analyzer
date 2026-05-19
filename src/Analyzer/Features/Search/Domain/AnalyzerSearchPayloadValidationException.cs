namespace Analyzer.Features.Search.Domain;

/// <summary>
/// Slice 007 — thrown by the search-event capture handler when the
/// payload fails handler-level validation (FR-006 / FR-008 — including
/// the empty-normalised-output guard and the visitor-bound pageview
/// check). The management controller maps to HTTP 400 with a
/// <c>ValidationProblemDetails</c> body. Internal to the Search
/// feature; not part of the pinned public surface.
/// </summary>
/// <remarks>
/// Distinct from model-binding validation (<c>[ApiController]</c> +
/// DataAnnotations) which the controller handles via
/// <c>ValidationProblemDetails</c>. This exception covers semantic
/// invariants the binder cannot express — e.g. "the normaliser
/// produced an empty output for a non-empty input" (defence in depth
/// against a custom <c>IAnalyzerSearchQueryNormaliser</c> that
/// collapses input to nothing), and "the supplied <c>pageviewKey</c>
/// does not belong to the resolved visitor".
/// </remarks>
internal sealed class AnalyzerSearchPayloadValidationException : Exception
{
    public AnalyzerSearchPayloadValidationException(
        string propertyName,
        string message)
        : base(message)
    {
        PropertyName = propertyName;
    }

    /// <summary>
    /// The payload property that violated the invariant. Mapped onto
    /// <c>ModelStateDictionary</c> by the controller so the client
    /// sees a 400 Problem Details with the right error key.
    /// </summary>
    public string PropertyName { get; }
}
