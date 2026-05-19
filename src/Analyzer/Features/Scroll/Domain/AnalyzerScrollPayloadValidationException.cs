namespace Analyzer.Features.Scroll.Domain;

/// <summary>
/// Slice 006 — thrown by the scroll-event capture handler when the
/// payload fails handler-level validation (FR-006 / FR-007). The
/// management controller maps to HTTP 400. Internal to the Scroll
/// feature; not part of the pinned public surface.
/// </summary>
/// <remarks>
/// Distinct from model-binding validation (<c>[ApiController]</c> +
/// DataAnnotations) which the controller handles via
/// <c>ValidationProblemDetails</c>. This exception covers semantic
/// invariants the binder cannot express — e.g. "the bucket value
/// must be one of the four defined enum members" (defence in depth
/// against undefined <c>(AnalyzerScrollBucket)42</c> casts), and
/// "<c>PageviewKey</c> must be non-empty".
/// </remarks>
internal sealed class AnalyzerScrollPayloadValidationException : Exception
{
    public AnalyzerScrollPayloadValidationException(
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
