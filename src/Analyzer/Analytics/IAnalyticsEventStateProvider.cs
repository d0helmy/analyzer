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
///   <item>Slice 003 — <see cref="CurrentSession"/>.</item>
///   <item>Slice 004 — <c>CurrentRequestCustomEvents</c>.</item>
///   <item>Slice 005 — <c>CurrentRequestFormEvents</c> +
///     <c>CurrentRequestFormFieldEvents</c>.</item>
///   <item>Slice 007 — <c>CurrentVideoState</c>.</item>
/// </list>
/// <para>
/// Breaking changes to existing members are PROHIBITED outside MAJOR
/// releases (Constitution Principle X). The pinning baseline regenerates
/// on each additive change with a Sync Impact note in the slice spec.
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

    /// <summary>
    /// Slice 003 — the current request's resolved session, or
    /// <c>null</c> when the subscriber has not yet completed. Same
    /// scoping semantics as <see cref="CurrentRequestReceipt"/>:
    /// typically <c>null</c> on the pageview request itself; reliably
    /// populated on in-request consumer flows at later slices.
    /// </summary>
    AnalyticsSession? CurrentSession { get; }

    /// <summary>
    /// Slice 004 — the custom events captured in the current request
    /// scope. Empty list when none captured (never null). The list
    /// grows as the page script makes multiple <c>analyzer.send(...)</c>
    /// calls during the same request lifecycle.
    /// </summary>
    /// <remarks>
    /// Slice 004 is the first slice where this state-provider is
    /// reliably populated for in-request consumers — the management
    /// endpoint that captures custom events runs synchronously on the
    /// request thread (vs slice-002's fire-and-forget pageview
    /// dispatch where the state-provider is typically null per the
    /// slice-002/003 caveats).
    /// </remarks>
    IReadOnlyList<AnalyticsCustomEvent> CurrentRequestCustomEvents { get; }

    /// <summary>
    /// Slice 005 — the form lifecycle events
    /// (<see cref="AnalyzerFormEventType.Impression"/> /
    /// <see cref="AnalyzerFormEventType.Start"/> /
    /// <see cref="AnalyzerFormEventType.Success"/>) captured in the
    /// current request scope. Empty list when none captured (never
    /// null). <see cref="AnalyzerFormEventType.Abandon"/> rows are
    /// materialised by the sweeper and never visible through this
    /// per-request projection.
    /// </summary>
    IReadOnlyList<AnalyticsFormEvent> CurrentRequestFormEvents { get; }

    /// <summary>
    /// Slice 005 — the form field events
    /// (<see cref="AnalyzerFormFieldEventType.FieldFocus"/> /
    /// <see cref="AnalyzerFormFieldEventType.FieldUnfocus"/>) captured
    /// in the current request scope. Empty list when none captured
    /// (never null).
    /// </summary>
    IReadOnlyList<AnalyticsFormFieldEvent> CurrentRequestFormFieldEvents { get; }
}
