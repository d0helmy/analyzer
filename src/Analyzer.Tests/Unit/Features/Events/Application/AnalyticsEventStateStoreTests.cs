using Analyzer.Analytics;
using Analyzer.Features.Events.Application;
using FluentAssertions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Events.Application;

/// <summary>
/// Slice 004 / T015 — verifies the slice-004 additive extension to the
/// scoped <see cref="AnalyticsEventStateStore"/>:
/// <see cref="AnalyticsEventStateStore.AppendCustomEvent"/> grows the
/// list in append order;
/// <see cref="AnalyticsEventStateStore.CurrentRequestCustomEvents"/>
/// returns a stable read-only view; fresh stores return an empty list
/// (never null).
/// </summary>
public sealed class AnalyticsEventStateStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Fresh_store_returns_empty_custom_events_list()
    {
        var store = new AnalyticsEventStateStore();

        store.CurrentRequestCustomEvents.Should().NotBeNull();
        store.CurrentRequestCustomEvents.Should().BeEmpty();
    }

    [Fact]
    public void AppendCustomEvent_grows_list_in_append_order()
    {
        var store = new AnalyticsEventStateStore();
        var first = NewEvent("engagement", "click", "header-cta");
        var second = NewEvent("engagement", "click", "footer-cta");

        store.AppendCustomEvent(first);
        store.AppendCustomEvent(second);

        store.CurrentRequestCustomEvents.Should().HaveCount(2);
        store.CurrentRequestCustomEvents[0].Should().Be(first);
        store.CurrentRequestCustomEvents[1].Should().Be(second);
    }

    [Fact]
    public void CurrentRequestCustomEvents_reads_reflect_subsequent_appends()
    {
        // The read-only view returned by `AsReadOnly()` is a live wrapper
        // over the underlying List<T> — subsequent appends are visible
        // through the same reference. Multiple in-scope reads see the
        // same growing list.
        var store = new AnalyticsEventStateStore();
        var view = store.CurrentRequestCustomEvents;

        view.Should().BeEmpty();

        store.AppendCustomEvent(NewEvent("nav", "click", null));
        view.Should().HaveCount(1);
    }

    [Fact]
    public void AppendCustomEvent_throws_on_null()
    {
        var store = new AnalyticsEventStateStore();

        Action act = () => store.AppendCustomEvent(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static AnalyticsCustomEvent NewEvent(string category, string action, string? label) =>
        new(
            EventKey: Guid.NewGuid(),
            SessionKey: Guid.NewGuid(),
            VisitorProfileKey: Guid.NewGuid(),
            ReceiptKey: null,
            Category: category,
            Action: action,
            Label: label,
            Value: null,
            ReceivedUtc: T0);
}
