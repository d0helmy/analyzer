namespace Analyzer.Analytics;

/// <summary>
/// One operator-defined engagement event recorded by a client-side
/// <c>analyzer.send(...)</c> call. The consumer-facing projection of an
/// <c>analyzerCustomEvent</c> row, surfaced through
/// <see cref="IAnalyticsEventStateProvider.CurrentRequestCustomEvents"/>
/// for in-process consumers within the same request scope.
/// </summary>
/// <remarks>
/// <para>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyticsEventReceipt"/>, <see cref="AnalyticsSession"/>,
/// and <see cref="IAnalyticsEventStateProvider"/>. Breaking changes are
/// PROHIBITED outside a MAJOR release (Constitution Principle X).
/// </para>
/// </remarks>
/// <param name="EventKey">
/// Publicly-exposed stable identifier; matches the DB row's
/// <c>eventKey</c>. Returned by the management endpoint's HTTP 202 body.
/// </param>
/// <param name="SessionKey">
/// Hard FK to <see cref="AnalyticsSession.SessionKey"/>. Always
/// non-empty.
/// </param>
/// <param name="VisitorProfileKey">
/// Hard FK to <c>customizerVisitorProfile.Key</c>. Always non-empty.
/// </param>
/// <param name="ReceiptKey">
/// Soft FK to <see cref="AnalyticsEventReceipt.Id"/>. Null in 99% of
/// real flows (page-script POST is a separate HTTP request from the
/// page render); populated only in the rare in-request co-capture case.
/// </param>
/// <param name="Category">
/// Operator-defined. 1..64 chars, non-whitespace-only.
/// </param>
/// <param name="Action">
/// Operator-defined. 1..64 chars, non-whitespace-only.
/// </param>
/// <param name="Label">
/// Operator-defined. Up to 256 chars when present.
/// </param>
/// <param name="Value">
/// Operator-defined numeric. Up to <c>decimal(18,4)</c> precision.
/// </param>
/// <param name="ReceivedUtc">
/// When the management endpoint observed the request. Sourced from
/// injected <see cref="System.TimeProvider"/>.
/// </param>
public sealed record AnalyticsCustomEvent(
    Guid EventKey,
    Guid SessionKey,
    Guid VisitorProfileKey,
    Guid? ReceiptKey,
    string Category,
    string Action,
    string? Label,
    decimal? Value,
    DateTimeOffset ReceivedUtc);
