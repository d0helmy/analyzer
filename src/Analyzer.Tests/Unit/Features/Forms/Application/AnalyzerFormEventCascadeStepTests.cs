using Analyzer.Features.Forms.Application.Anonymization;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Application;

/// <summary>
/// Slice 005 / T029 — unit tests for
/// <see cref="AnalyzerFormEventCascadeStep"/>. The integration tests
/// (T038 / T039) cover the outer-scope rollback path against a real
/// SQL Server.
/// </summary>
public sealed class AnalyzerFormEventCascadeStepTests
{
    [Fact]
    public async Task EmptyVisitorKey_skips_delete()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerFormEventCascadeStep(
            repo, NullLogger<AnalyzerFormEventCascadeStep>.Instance);

        await step.ExecuteAsync(Guid.Empty, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task NonEmptyVisitorKey_invokes_DeleteByVisitorKey_once()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerFormEventCascadeStep(
            repo, NullLogger<AnalyzerFormEventCascadeStep>.Instance);
        var visitor = Guid.NewGuid();

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(1);
        repo.LastVisitor.Should().Be(visitor);
    }

    private sealed class FakeRepository : IAnalyzerFormEventRepository
    {
        public int DeleteCalls { get; private set; }
        public Guid LastVisitor { get; private set; }

        public Task InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
        {
            DeleteCalls++;
            LastVisitor = visitorProfileKey;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UnclosedStartTuple>> ListUnclosedStartsForSessionsAsync(
            IReadOnlyCollection<Guid> sessionKeys,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<UnclosedStartTuple>>(Array.Empty<UnclosedStartTuple>());

        public Task InsertAbandonsBulkAsync(
            IReadOnlyList<AnalyzerFormEventDto> abandons,
            CancellationToken ct) => Task.CompletedTask;
    }
}
