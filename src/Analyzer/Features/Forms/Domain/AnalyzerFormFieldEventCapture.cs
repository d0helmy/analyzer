using Analyzer.Analytics;
using Analyzer.Features.Visitors.Application.Contracts;

namespace Analyzer.Features.Forms.Domain;

/// <summary>
/// Slice 005 — in-process command passed from
/// <c>AnalyzerFormEventManagementController</c> to
/// <c>AnalyzerFormFieldEventCaptureHandler</c>. Symmetric to
/// <see cref="AnalyzerFormEventCapture"/>; carries the field-level
/// payload.
/// </summary>
/// <param name="Actor">EntraID-resolved visitor identity.</param>
/// <param name="FormKey">Umbraco Forms <c>Form.Id</c>. Non-empty.</param>
/// <param name="FieldKey">Umbraco Forms <c>Field.Id</c>. Non-empty.</param>
/// <param name="EventType">
/// <see cref="AnalyzerFormFieldEventType.FieldFocus"/> or
/// <see cref="AnalyzerFormFieldEventType.FieldUnfocus"/>.
/// </param>
/// <param name="HadValue">
/// Required on <c>FieldUnfocus</c>; MUST be null on <c>FieldFocus</c>.
/// Derived client-side from <c>element.value.length &gt; 0</c>;
/// values themselves are never sent.
/// </param>
/// <param name="UserAgent">Raw <c>User-Agent</c> header.</param>
/// <param name="ReceivedUtc">When the endpoint observed the request.</param>
public sealed record AnalyzerFormFieldEventCapture(
    VisitorIdentity Actor,
    Guid FormKey,
    Guid FieldKey,
    AnalyzerFormFieldEventType EventType,
    bool? HadValue,
    string? UserAgent,
    DateTimeOffset ReceivedUtc);
