using Analyzer.Features.Scroll.Application.Anonymization;
using Analyzer.Features.Scroll.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Scroll.Application;

/// <summary>
/// Slice 006 / T027 — unit tests for
/// <see cref="AnalyzerScrollSampleCascadeStep"/>. The integration
/// tests (T037 / T038) cover the outer-scope rollback path against
/// a real SQL Server.
/// </summary>
public sealed class AnalyzerScrollSampleCascadeStepTests
{
    [Fact]
    public async Task EmptyVisitorKey_skips_delete()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerScrollSampleCascadeStep(
            repo, NullLogger<AnalyzerScrollSampleCascadeStep>.Instance);

        await step.ExecuteAsync(Guid.Empty, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task NonEmptyVisitorKey_invokes_DeleteByVisitor_once()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerScrollSampleCascadeStep(
            repo, NullLogger<AnalyzerScrollSampleCascadeStep>.Instance);
        var visitor = Guid.NewGuid();

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(1);
        repo.LastVisitor.Should().Be(visitor);
    }

    [Fact]
    public async Task RepositoryThrows_bubbles_exception()
    {
        var repo = new FakeRepository { ThrowOnDelete = new InvalidOperationException("DB down") };
        var step = new AnalyzerScrollSampleCascadeStep(
            repo, NullLogger<AnalyzerScrollSampleCascadeStep>.Instance);

        var act = async () =>
            await step.ExecuteAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB down");
    }

    private sealed class FakeRepository : IAnalyzerScrollSampleRepository
    {
        public int DeleteCalls { get; private set; }
        public Guid LastVisitor { get; private set; }
        public Exception? ThrowOnDelete { get; set; }

        public Task InsertAsync(AnalyzerScrollSampleDto dto, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorAsync(Guid visitorProfileKey, CancellationToken ct)
        {
            DeleteCalls++;
            LastVisitor = visitorProfileKey;
            if (ThrowOnDelete is not null) throw ThrowOnDelete;
            return Task.CompletedTask;
        }

        public Task<int> CountByVisitorAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.FromResult(0);
    }
}
