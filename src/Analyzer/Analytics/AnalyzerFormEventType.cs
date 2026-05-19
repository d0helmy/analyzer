namespace Analyzer.Analytics;

/// <summary>
/// Slice 005 — discriminator for <see cref="AnalyticsFormEvent"/>.
/// Public + pinned via <c>PublicSurfacePinningTests</c>. Byte-backed
/// for a stable wire representation (matches the DB column type).
/// </summary>
public enum AnalyzerFormEventType : byte
{
    /// <summary>
    /// Form scrolled into the viewport (client-side
    /// <c>IntersectionObserver</c>). Passive — does not advance
    /// <c>analyzerSession.lastActivityUtc</c>.
    /// </summary>
    Impression = 0,

    /// <summary>
    /// First <c>focus</c> on any field within the form. Touches
    /// session activity (engagement model from slice 004).
    /// </summary>
    Start = 1,

    /// <summary>
    /// Form submit (client-side <c>submit</c> event dispatched and not
    /// cancelled by an earlier listener; server-side rejection of
    /// the underlying POST does NOT roll this row back).
    /// </summary>
    Success = 2,

    /// <summary>
    /// Materialised by <c>AnalyzerSessionSweeperService</c> when a
    /// session closes with a <see cref="Start"/> row but no
    /// <see cref="Success"/> row for the same
    /// <c>(visitorKey, formKey, sessionKey)</c> tuple. Never POSTed
    /// by the client.
    /// </summary>
    Abandon = 3,
}
