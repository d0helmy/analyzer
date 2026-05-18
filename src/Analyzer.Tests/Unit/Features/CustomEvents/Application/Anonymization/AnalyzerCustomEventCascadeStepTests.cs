using Analyzer.Features.CustomEvents.Application.Anonymization;
using Analyzer.Features.CustomEvents.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.CustomEvents.Application.Anonymization;

/// <summary>
/// Slice 004 / T037 — orchestrator unit tests for
/// <see cref="AnalyzerCustomEventCascadeStep"/>. Verifies:
/// repository is invoked with the correct visitor key; <c>Guid.Empty</c>
/// short-circuits; idempotent re-runs are no-ops.
/// </summary>
public sealed class AnalyzerCustomEventCascadeStepTests
{
    [Fact]
    public async Task ExecuteAsync_invokes_repo_with_visitor_key()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepository();
        var step = NewStep(repo);

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(1);
        repo.LastDeleteKey.Should().Be(visitor);
    }

    [Fact]
    public async Task ExecuteAsync_short_circuits_on_empty_guid()
    {
        var repo = new FakeRepository();
        var step = NewStep(repo);

        await step.ExecuteAsync(Guid.Empty, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_on_repeated_invocation()
    {
        var visitor = Guid.NewGuid();
        var repo = new FakeRepository();
        var step = NewStep(repo);

        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);
        await step.ExecuteAsync(visitor, TestContext.Current.CancellationToken);

        repo.DeleteCalls.Should().Be(2,
            "the cascade-step should always delegate to the repo; idempotency is the repo's no-op on zero rows");
        repo.LastDeleteKey.Should().Be(visitor);
    }

    private static AnalyzerCustomEventCascadeStep NewStep(FakeRepository repo) =>
        new(repo, NullLogger<AnalyzerCustomEventCascadeStep>.Instance);

    private sealed class FakeRepository : IAnalyzerCustomEventRepository
    {
        public int DeleteCalls { get; private set; }
        public Guid LastDeleteKey { get; private set; }

        public Task InsertAsync(AnalyzerCustomEventDto dto, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct)
        {
            DeleteCalls++;
            LastDeleteKey = visitorProfileKey;
            return Task.CompletedTask;
        }
    }
}
