namespace Analyzer.Analytics;

/// <summary>
/// Slice 005 — immutable projection of an
/// <c>analyzerFormFieldEvent</c> row. Returned by
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestFormFieldEvents"/>.
/// </summary>
/// <remarks>
/// Public + pinned. Privacy invariant: <see cref="HadValue"/> is the
/// ONLY property derived from field content; field values themselves
/// are never captured.
/// </remarks>
/// <param name="EventKey">Publicly-exposed stable identifier.</param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty.
/// </param>
/// <param name="SessionKey">
/// Soft FK to <see cref="AnalyticsSession.SessionKey"/>. NULL allowed.
/// </param>
/// <param name="FormKey">Umbraco Forms <c>Form.Id</c>.</param>
/// <param name="FieldKey">Umbraco Forms <c>Field.Id</c>.</param>
/// <param name="EventType">
/// <see cref="AnalyzerFormFieldEventType"/> discriminator.
/// </param>
/// <param name="HadValue">
/// Set only when <see cref="EventType"/> ==
/// <see cref="AnalyzerFormFieldEventType.FieldUnfocus"/>. The single
/// privacy-safe signal derived from what the user typed
/// (<c>element.value.length &gt; 0</c>, evaluated client-side).
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request.
/// </param>
public sealed record AnalyticsFormFieldEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid FieldKey,
    AnalyzerFormFieldEventType EventType,
    bool? HadValue,
    DateTimeOffset ReceivedUtc);
