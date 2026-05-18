using Analyzer.Analytics;
using Analyzer.Features.Events.Infrastructure.Dispatcher;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Events.Application;

public sealed class AnalyzerEventReceiptWriteQueueTests
{
    [Fact]
    public void TryEnqueue_ReturnsTrueWhenRoomAvailable()
    {
        var queue = BuildQueue(capacity: 4);
        var op = NewOp();

        queue.TryEnqueue(op).Should().BeTrue();
    }

    [Fact]
    public void TryEnqueue_ReturnsFalseWhenAtCapacity()
    {
        const int capacity = 3;
        var queue = BuildQueue(capacity);

        for (int i = 0; i < capacity; i++)
        {
            queue.TryEnqueue(NewOp()).Should().BeTrue();
        }

        queue.TryEnqueue(NewOp()).Should().BeFalse();
    }

    [Fact]
    public async Task Reader_ExposesEnqueuedItemsInOrder()
    {
        var queue = BuildQueue(capacity: 4);
        var first = NewOp();
        var second = NewOp();

        queue.TryEnqueue(first).Should().BeTrue();
        queue.TryEnqueue(second).Should().BeTrue();

        var read1 = await queue.Reader.ReadAsync(TestContext.Current.CancellationToken);
        var read2 = await queue.Reader.ReadAsync(TestContext.Current.CancellationToken);

        read1.Should().BeSameAs(first);
        read2.Should().BeSameAs(second);
    }

    private static AnalyzerEventReceiptWriteQueue BuildQueue(int capacity)
    {
        var options = Options.Create(new AnalyzerWriteQueueOptions
        {
            WriteQueueCapacity = capacity,
        });
        return new AnalyzerEventReceiptWriteQueue(options);
    }

    private static AnalyzerEventReceiptWriteOp NewOp() =>
        new(new AnalyticsEventReceipt(
            Id: Guid.NewGuid(),
            PageviewKey: Guid.NewGuid(),
            VisitorProfileKey: Guid.NewGuid(),
            ReceivedUtc: DateTimeOffset.UtcNow));
}
