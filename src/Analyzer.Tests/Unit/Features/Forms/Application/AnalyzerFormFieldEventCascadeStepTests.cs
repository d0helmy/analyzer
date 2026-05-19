using Analyzer.Features.Forms.Application.Anonymization;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Application;

public sealed class AnalyzerFormFieldEventCascadeStepTests
{
    [Fact]
    public async Task EmptyVisitorKey_skips_delete()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerFormFieldEventCascadeStep(
            repo, NullLogger<AnalyzerFormFieldEventCascadeStep>.Instance);

        await step.ExecuteAsync(Guid.Empty, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task NonEmptyVisitorKey_invokes_DeleteByVisitorKey_once()
    {
        var repo = new FakeRepository();
        var step = new AnalyzerFormFieldEventCascadeStep(
            repo, NullLogger<AnalyzerFormFieldEventCascadeStep>.Instance);
        var visitor = Guid.NewGuid();

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(1);
        repo.LastVisitor.Should().Be(visitor);
    }

    private sealed class FakeRepository : IAnalyzerFormFieldEventRepository
    {
        public int DeleteCalls { get; private set; }
        public Guid LastVisitor { get; private set; }

        public Task InsertAsync(AnalyzerFormFieldEventDto dto, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
        {
            DeleteCalls++;
            LastVisitor = visitorProfileKey;
            return Task.CompletedTask;
        }
    }
}
