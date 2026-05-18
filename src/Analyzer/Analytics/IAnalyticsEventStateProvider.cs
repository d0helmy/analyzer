namespace Analyzer.Analytics;

/// <summary>
/// Per-request read surface for Analyzer-side captured event state.
/// Deliberately distinct from Customizer's pinned
/// <see cref="Customizer.Analytics.IAnalyticsStateProvider"/> — both
/// interfaces can be injected into the same consumer without
/// name-collision (Constitution Principle III + inter-product contract
/// D3).
/// </summary>
/// <remarks>
/// <para>
/// Public + pinned. Members grow additively per slice:
/// </para>
/// <list type="bullet">
///   <item>Slice 002 — <see cref="CurrentRequestReceipt"/>.</item>
///   <item>Slice 003 — session-state members.</item>
///   <item>Slice 004 — <c>CurrentRequestCustomEvents</c>.</item>
///   <item>Slice 007 — <c>CurrentVideoState</c>.</item>
/// </list>
/// <para>
/// Breaking changes to existing members are PROHIBITED outside MAJOR
/// releases (Constitution Principle X). The slice-002 pinning baseline
/// captures the current shape.
/// </para>
/// <para>
/// Registered with <b>scoped</b> DI lifetime — one instance per HTTP
/// request scope, matching Customizer's pinned
/// <c>IAnalyticsStateProvider</c> for symmetry.
/// </para>
/// </remarks>
public interface IAnalyticsEventStateProvider
{
    /// <summary>
    /// The current request's captured event-receipt, or <c>null</c>
    /// when Analyzer's subscriber has not yet completed for this
    /// request.
    /// </summary>
    /// <remarks>
    /// On pageview requests this property is typically <c>null</c>:
    /// Customizer publishes the underlying notification via a
    /// <c>Task.Run</c> fire-and-forget dispatch, so the handler may
    /// complete after the request thread has already produced the
    /// response. On in-request dispatches at later slices (e.g.
    /// custom events fired in-page), it populates reliably because
    /// the dispatch runs synchronously inside the request scope.
    /// </remarks>
    AnalyticsEventReceipt? CurrentRequestReceipt { get; }
}
