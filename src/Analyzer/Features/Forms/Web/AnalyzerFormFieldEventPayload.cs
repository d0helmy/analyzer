using Analyzer.Analytics;

namespace Analyzer.Features.Forms.Web;

/// <summary>
/// Slice 005 US2 — inbound JSON payload for the field-event
/// management endpoint. Same privacy invariant as
/// <see cref="AnalyzerFormEventPayload"/>: no property may carry
/// field content; <see cref="HadValue"/> is the single permitted
/// signal derived from what the user typed.
/// </summary>
public sealed class AnalyzerFormFieldEventPayload
{
    public Guid FormKey { get; init; }

    public Guid FieldKey { get; init; }

    public AnalyzerFormFieldEventType EventType { get; init; }

    public bool? HadValue { get; init; }
}
