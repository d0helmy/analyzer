using Analyzer.Analytics;
using Analyzer.Features.Forms.Application.Abandonment;
using Analyzer.Features.Forms.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Application;

/// <summary>
/// Slice 005 / T032 — unit tests for
/// <see cref="AnalyzerFormAbandonmentMaterialiser"/>. The integration
/// test (T040) covers idempotency + anonymisation-skip against a real
/// SQL Server; these unit tests pin the in-process behaviour.
/// </summary>
public sealed class AnalyzerFormAbandonmentMaterialiserTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EmptyClosedBatch_is_a_no_op()
    {
        var repo = new FakeRepository();
        var materialiser = new AnalyzerFormAbandonmentMaterialiser(
            repo, NullLogger<AnalyzerFormAbandonmentMaterialiser>.Instance);

        await materialiser.MaterialiseAsync(
            Array.Empty<Guid>(),
            T0,
            TestContext.Current.CancellationToken);

        repo.ListCalls.Should().Be(0);
        repo.LastAbandons.Should().BeNull();
    }

    [Fact]
    public async Task ZeroUnclosedStartTuples_is_a_no_op_insert()
    {
        var repo = new FakeRepository();
        var materialiser = new AnalyzerFormAbandonmentMaterialiser(
            repo, NullLogger<AnalyzerFormAbandonmentMaterialiser>.Instance);

        await materialiser.MaterialiseAsync(
            new[] { Guid.NewGuid() },
            T0,
            TestContext.Current.CancellationToken);

        repo.ListCalls.Should().Be(1);
        repo.LastAbandons.Should().BeNull();
    }

    [Fact]
    public async Task OneAbandonPerUnclosedTuple_with_correct_elapsedMsFromStart()
    {
        var visitor = Guid.NewGuid();
        var session = Guid.NewGuid();
        var form = Guid.NewGuid();
        var content = Guid.NewGuid();
        var startUtc = T0.AddSeconds(-30);
        var closeUtc = T0;

        var repo = new FakeRepository
        {
            NextListResult = new[]
            {
                new UnclosedStartTuple(session, form, visitor, content, startUtc),
            },
        };
        var materialiser = new AnalyzerFormAbandonmentMaterialiser(
            repo, NullLogger<AnalyzerFormAbandonmentMaterialiser>.Instance);

        await materialiser.MaterialiseAsync(
            new[] { session },
            closeUtc,
            TestContext.Current.CancellationToken);

        repo.LastAbandons.Should().HaveCount(1);
        var abandon = repo.LastAbandons![0];
        abandon.EventType.Should().Be((byte)AnalyzerFormEventType.Abandon);
        abandon.VisitorProfileKey.Should().Be(visitor);
        abandon.SessionKey.Should().Be(session);
        abandon.FormKey.Should().Be(form);
        abandon.ContentKey.Should().Be(content);
        abandon.ReceivedUtc.Should().Be(closeUtc);
        abandon.ElapsedMsFromStart.Should().Be(30_000);
        abandon.ElapsedMsFromImpression.Should().BeNull();
        abandon.EventKey.Should().NotBe(Guid.Empty);
        abandon.Id.Should().NotBe(Guid.Empty);
    }

    private sealed class FakeRepository : IAnalyzerFormEventRepository
    {
        public int ListCalls { get; private set; }
        public IReadOnlyList<UnclosedStartTuple> NextListResult { get; set; } =
            Array.Empty<UnclosedStartTuple>();
        public IReadOnlyList<AnalyzerFormEventDto>? LastAbandons { get; private set; }

        public Task InsertAsync(AnalyzerFormEventDto dto, CancellationToken ct) =>
            Task.CompletedTask;

        public Task DeleteByVisitorKeyAsync(Guid visitorProfileKey, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<UnclosedStartTuple>> ListUnclosedStartsForSessionsAsync(
            IReadOnlyCollection<Guid> sessionKeys,
            CancellationToken ct)
        {
            ListCalls++;
            return Task.FromResult(NextListResult);
        }

        public Task InsertAbandonsBulkAsync(
            IReadOnlyList<AnalyzerFormEventDto> abandons,
            CancellationToken ct)
        {
            LastAbandons = abandons;
            return Task.CompletedTask;
        }
    }
}
