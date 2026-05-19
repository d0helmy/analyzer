namespace Analyzer.Features.Forms.Domain;

/// <summary>
/// Slice 005 — thrown by the form-event capture handlers when the
/// payload fails handler-level validation (FR-006 / FR-007). The
/// management controller maps to HTTP 400. Internal to the Forms
/// feature; not part of the pinned public surface.
/// </summary>
/// <remarks>
/// Distinct from model-binding validation (<c>[ApiController]</c> +
/// DataAnnotations) which the controller handles via
/// <c>ValidationProblemDetails</c>. This exception covers semantic
/// invariants the binder cannot express — e.g. "the timing-slot
/// columns must match the event-type discriminator".
/// </remarks>
internal sealed class AnalyzerFormPayloadValidationException : Exception
{
    public AnalyzerFormPayloadValidationException(
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
