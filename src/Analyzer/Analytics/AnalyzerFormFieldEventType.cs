namespace Analyzer.Analytics;

/// <summary>
/// Slice 005 — discriminator for <see cref="AnalyticsFormFieldEvent"/>.
/// Public + pinned via <c>PublicSurfacePinningTests</c>. Byte-backed
/// for a stable wire representation.
/// </summary>
public enum AnalyzerFormFieldEventType : byte
{
    /// <summary>Field gained focus.</summary>
    FieldFocus = 0,

    /// <summary>
    /// Field lost focus. <see cref="AnalyticsFormFieldEvent.HadValue"/>
    /// is the only signal derived from the user's keystrokes;
    /// values themselves are never transmitted (privacy invariant,
    /// SC-003).
    /// </summary>
    FieldUnfocus = 1,
}
