namespace Analyzer.Analytics;

/// <summary>
/// Slice 006 — scroll-depth milestone bucket. Byte-backed for storage
/// parity with the <c>analyzerScrollSample.bucket</c> column
/// (<c>tinyint</c>); explicit underlying values keep wire-format
/// stability across MAJOR enum-member additions (none currently
/// planned).
/// </summary>
/// <remarks>
/// Public + pinned. Lives in <c>Analyzer.Analytics</c> alongside
/// <see cref="AnalyzerFormEventType"/> and
/// <see cref="AnalyticsScrollSample"/>. Breaking changes (renaming,
/// removing, or re-numbering members) PROHIBITED outside a MAJOR
/// release (Constitution Principle X).
/// </remarks>
public enum AnalyzerScrollBucket : byte
{
    /// <summary>Visitor crossed the 25 % depth threshold.</summary>
    Quarter = 25,

    /// <summary>Visitor crossed the 50 % depth threshold.</summary>
    Half = 50,

    /// <summary>Visitor crossed the 75 % depth threshold.</summary>
    ThreeQuarters = 75,

    /// <summary>
    /// Visitor reached the bottom (100 % depth). Also emitted on
    /// page-ready for short pages where the document is no taller
    /// than the viewport (research §R3); buckets 25 / 50 / 75 are
    /// skipped on the short-page path.
    /// </summary>
    Full = 100,
}
