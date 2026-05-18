using Analyzer.Analytics;
using Analyzer.Features.Events.Application.Anonymization;
using Analyzer.Features.Events.Infrastructure.Persistence;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Events.Application.Anonymization;

public sealed class AnalyzerEventReceiptCascadeStepTests
{
    [Fact]
    public async Task DelegatesToRepositoryWithSuppliedKey()
    {
        var repo = new RecordingRepository();
        var step = new AnalyzerEventReceiptCascadeStep(repo);
        var visitorKey = Guid.NewGuid();

        await step.ExecuteAsync(visitorKey, TestContext.Current.CancellationToken);

        repo.LastDeletedKey.Should().Be(visitorKey);
        repo.DeleteCalls.Should().Be(1);
    }

    [Fact]
    public async Task ZeroRowsIsNoOp()
    {
        // The contract is a fire-and-forget delete; the step doesn't
        // peek at row counts. "Zero rows" is observed at the repository
        // layer (DELETE returns affected-count 0), which is success.
        var repo = new RecordingRepository();
        var step = new AnalyzerEventReceiptCascadeStep(repo);

        Func<Task> act = () => step.ExecuteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        var repo = new ThrowingRepository(new OperationCanceledException());
        var step = new AnalyzerEventReceiptCascadeStep(repo);

        Func<Task> act = () => step.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class RecordingRepository : IAnalyzerEventReceiptRepository
    {
        public Guid LastDeletedKey { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task InsertAsync(AnalyticsEventReceipt receipt, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
        {
            LastDeletedKey = visitorProfileKey;
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingRepository : IAnalyzerEventReceiptRepository
    {
        private readonly Exception _ex;
        public ThrowingRepository(Exception ex) => _ex = ex;

        public Task InsertAsync(AnalyticsEventReceipt receipt, CancellationToken ct) =>
            Task.FromException(_ex);

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.FromException(_ex);
    }
}
