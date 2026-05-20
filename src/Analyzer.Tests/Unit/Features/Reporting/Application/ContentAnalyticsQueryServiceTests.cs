using Analyzer.Features.Reporting.Application;
using Analyzer.Features.Reporting.Domain;
using Analyzer.Features.Reporting.Infrastructure;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Reporting.Application;

/// <summary>
/// Slice 008 / T024 — pins the projection-to-snapshot composition
/// rules. The query service returns <c>null</c> only when both the
/// capture tables AND the published-content cache report "unknown".
/// </summary>
public sealed class ContentAnalyticsQueryServiceTests
{
    private static readonly DateTimeOffset WindowEnd =
        new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_captures_and_tombstoned_returns_null_for_404()
    {
        var sut = NewService(
            repository: new StubRepository(EmptyProjection()),
            probe: new StubProbe(isTombstoned: true));

        var result = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PublishedContentWithNoCapturesReturnsSnapshotWithZeros()
    {
        // US2 / T040 — explicit guarantee that the (no captures + cache
        // hit) branch returns a snapshot with all-zero metrics rather
        // than null. Pins the spec invariant that empty-state is never
        // a 404.
        var sut = NewService(
            repository: new StubRepository(EmptyProjection()),
            probe: new StubProbe(isTombstoned: false));

        var result = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Pageviews24h.Should().Be(0);
        result.Pageviews7d.Should().Be(0);
        result.Pageviews30d.Should().Be(0);
        result.UniqueVisitors30d.Should().Be(0);
        result.AvgTimeOnPageSeconds30d.Should().BeNull();
        result.IsContentCurrentlyTombstoned.Should().BeFalse();
        result.TopReferrers30d.Should().BeEmpty();
    }

    [Fact]
    public async Task Live_content_with_zero_captures_returns_snapshot_with_zeros()
    {
        var sut = NewService(
            repository: new StubRepository(EmptyProjection()),
            probe: new StubProbe(isTombstoned: false));
        var contentKey = Guid.NewGuid();

        var result = await sut.GetAsync(contentKey, CancellationToken.None);

        result.Should().NotBeNull();
        result!.ContentKey.Should().Be(contentKey);
        result.Pageviews24h.Should().Be(0);
        result.Pageviews7d.Should().Be(0);
        result.Pageviews30d.Should().Be(0);
        result.UniqueVisitors30d.Should().Be(0);
        result.AvgTimeOnPageSeconds30d.Should().BeNull();
        result.IsContentCurrentlyTombstoned.Should().BeFalse();
        result.TopReferrers30d.Should().BeEmpty();
    }

    [Fact]
    public async Task Tombstoned_content_with_captures_returns_snapshot_with_flag()
    {
        var projection = new ContentAnalyticsProjection(
            Pageviews24h: 2,
            Pageviews7d: 10,
            Pageviews30d: 30,
            UniqueVisitors30d: 5,
            AvgTimeOnPageSeconds30d: 42,
            HasAnyCaptureRow: true);
        var sut = NewService(
            repository: new StubRepository(projection),
            probe: new StubProbe(isTombstoned: true));

        var result = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsContentCurrentlyTombstoned.Should().BeTrue();
        result.Pageviews30d.Should().Be(30);
        result.AvgTimeOnPageSeconds30d.Should().Be(42);
    }

    [Fact]
    public async Task Snapshot_windowEndUtc_matches_time_provider()
    {
        var sut = NewService(
            repository: new StubRepository(EmptyProjection()),
            probe: new StubProbe(isTombstoned: false));

        var result = await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result!.WindowEndUtc.Should().Be(WindowEnd);
    }

    [Fact]
    public async Task Repository_receives_window_end_from_time_provider()
    {
        var repo = new StubRepository(EmptyProjection());
        var sut = NewService(repository: repo, probe: new StubProbe(isTombstoned: false));

        await sut.GetAsync(Guid.NewGuid(), CancellationToken.None);

        repo.LastWindowEndUtc.Should().Be(WindowEnd);
    }

    private static ContentAnalyticsQueryService NewService(
        IContentAnalyticsRepository repository,
        IPublishedContentTombstoneProbe probe)
        => new(repository, probe, new FixedClock(WindowEnd));

    private static ContentAnalyticsProjection EmptyProjection() => new(
        Pageviews24h: 0,
        Pageviews7d: 0,
        Pageviews30d: 0,
        UniqueVisitors30d: 0,
        AvgTimeOnPageSeconds30d: null,
        HasAnyCaptureRow: false);

    private sealed class StubRepository : IContentAnalyticsRepository
    {
        private readonly ContentAnalyticsProjection _projection;
        public DateTimeOffset? LastWindowEndUtc { get; private set; }

        public StubRepository(ContentAnalyticsProjection projection) => _projection = projection;

        public Task<ContentAnalyticsProjection> GetAsync(
            Guid contentKey,
            DateTimeOffset windowEndUtc,
            CancellationToken ct)
        {
            LastWindowEndUtc = windowEndUtc;
            return Task.FromResult(_projection);
        }
    }

    private sealed class StubProbe : IPublishedContentTombstoneProbe
    {
        private readonly bool _isTombstoned;
        public StubProbe(bool isTombstoned) => _isTombstoned = isTombstoned;
        public bool IsTombstoned(Guid contentKey) => _isTombstoned;
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
