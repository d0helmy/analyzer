namespace Analyzer.Analytics;

/// <summary>
/// Slice 005 — immutable projection of an <c>analyzerFormEvent</c>
/// row. Returned by
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestFormEvents"/>
/// for in-process consumers within the request scope.
/// </summary>
/// <remarks>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyticsCustomEvent"/>. Breaking changes PROHIBITED
/// outside a MAJOR release (Constitution Principle X).
/// </remarks>
/// <param name="EventKey">
/// Publicly-exposed stable identifier; matches the DB row's
/// <c>eventKey</c>. Returned by the management endpoint's HTTP 202.
/// </param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty.
/// </param>
/// <param name="SessionKey">
/// Soft FK to <see cref="AnalyticsSession.SessionKey"/>. NULL allowed
/// (pre-sessions cohort + back-pressure-drop posture, matching slice
/// 002 receipt).
/// </param>
/// <param name="FormKey">Umbraco Forms <c>Form.Id</c>. Non-empty.</param>
/// <param name="ContentKey">
/// Umbraco content node hosting the form. Non-FK — tombstone tolerance
/// per slice-002 precedent (content may be deleted; the row stays).
/// </param>
/// <param name="EventType">
/// <see cref="AnalyzerFormEventType"/> discriminator.
/// </param>
/// <param name="ElapsedMsFromImpression">
/// Set only when <see cref="EventType"/> ==
/// <see cref="AnalyzerFormEventType.Start"/>. NULL otherwise.
/// </param>
/// <param name="ElapsedMsFromStart">
/// Set only when <see cref="EventType"/> ∈ { <c>Success</c>,
/// <c>Abandon</c> }. NULL otherwise.
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request, or — for
/// <c>Abandon</c> rows — when the sweeper materialised the row.
/// Sourced from injected <see cref="System.TimeProvider"/>.
/// </param>
public sealed record AnalyticsFormEvent(
    Guid EventKey,
    Guid VisitorProfileKey,
    Guid? SessionKey,
    Guid FormKey,
    Guid ContentKey,
    AnalyzerFormEventType EventType,
    int? ElapsedMsFromImpression,
    int? ElapsedMsFromStart,
    DateTimeOffset ReceivedUtc);
