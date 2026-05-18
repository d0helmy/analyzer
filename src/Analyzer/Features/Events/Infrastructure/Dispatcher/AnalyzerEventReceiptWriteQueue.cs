using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Analyzer.Features.Events.Infrastructure.Dispatcher;

/// <summary>
/// Bounded producer/consumer queue feeding
/// <see cref="AnalyzerEventReceiptWriteDispatcher"/>. Mirrors
/// Customizer's <c>VisitorWriteQueue</c> shape verbatim
/// (<see cref="BoundedChannelFullMode.Wait"/> + <c>TryWrite</c>): when
/// the channel is full, <c>TryWrite</c> returns <c>false</c> immediately
/// and the caller logs the drop. <c>DropWrite</c> mode would silently
/// succeed and discard internally, hiding the drop from the caller —
/// the wrong shape for spec Clarifications Q2's
/// at-most-once-with-drop-log contract.
/// </summary>
internal sealed class AnalyzerEventReceiptWriteQueue
{
    private readonly Channel<AnalyzerEventReceiptWriteOp> _channel;

    public AnalyzerEventReceiptWriteQueue(IOptions<AnalyzerWriteQueueOptions> options)
    {
        var capacity = Math.Max(1, options.Value.WriteQueueCapacity);
        Capacity = capacity;
        _channel = Channel.CreateBounded<AnalyzerEventReceiptWriteOp>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
    }

    public int Capacity { get; }

    /// <summary>
    /// Best-effort enqueue. Returns <c>false</c> when the queue is at
    /// capacity. Callers are expected to <c>LogWarning</c> the drop
    /// with structured fields (research §9).
    /// </summary>
    public bool TryEnqueue(AnalyzerEventReceiptWriteOp op)
    {
        ArgumentNullException.ThrowIfNull(op);
        return _channel.Writer.TryWrite(op);
    }

    public ChannelReader<AnalyzerEventReceiptWriteOp> Reader => _channel.Reader;

    public void Complete() => _channel.Writer.TryComplete();
}
