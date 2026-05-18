namespace Analyzer.Features.Events.Infrastructure.Dispatcher;

/// <summary>
/// Runtime-tunable knobs for the bounded write queue + dispatcher.
/// Bound from the <c>Analyzer:WriteQueue</c> configuration section by
/// <see cref="Analyzer.Composers.AnalyzerComposer"/> via
/// <c>IOptions&lt;T&gt;</c>. Mirrors Customizer's <c>VisitorOptions</c>
/// shape for the dispatcher-relevant subset.
/// </summary>
internal sealed class AnalyzerWriteQueueOptions
{
    /// <summary>
    /// Capacity of the bounded queue. When full, <c>TryEnqueue</c>
    /// returns <c>false</c> and the caller logs the drop
    /// (at-most-once delivery; spec Clarifications Q2).
    /// </summary>
    public int WriteQueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// Maximum receipts per batch the dispatcher flushes in one DB
    /// round-trip.
    /// </summary>
    public int FlushBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time the dispatcher waits before flushing a partial
    /// batch, in milliseconds.
    /// </summary>
    public int FlushIntervalMs { get; set; } = 250;
}
