using Analyzer.Analytics;
using Analyzer.Features.Scroll.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Scroll.Application;

/// <summary>
/// Slice 006 / T022 — asserts the audit-log scope carries the
/// load-bearing named properties on the Accepted (202) and Duplicate
/// (409) paths (research §R8 + FR-006).
/// </summary>
public sealed class AnalyzerScrollEventAuditorTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AuditAccepted_emits_one_information_entry_with_Disposition_Accepted()
    {
        var captured = new CapturingLogger();
        var auditor = new AnalyzerScrollEventAuditor(captured);

        var actor = NewActor();
        var eventKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();

        auditor.AuditAccepted(actor, eventKey, pageviewKey, AnalyzerScrollBucket.Half, T0);

        captured.Calls.Should().HaveCount(1);
        captured.Calls[0].Level.Should().Be(LogLevel.Information);
        captured.Calls[0].State.Should().Contain(("AuditAction", Constants.AuditLog.ScrollEventCapture));
        captured.Calls[0].State.Should().Contain(("ActorUpn", "user@example.com"));
        captured.Calls[0].State.Should().Contain(("ActorOid", "oid-abc"));
        captured.Calls[0].State.Should().Contain(("EventKey", eventKey));
        captured.Calls[0].State.Should().Contain(("PageviewKey", pageviewKey));
        captured.Calls[0].State.Should().Contain(("Bucket", AnalyzerScrollBucket.Half));
        captured.Calls[0].State.Should().Contain(("Disposition", "Accepted"));
        captured.Calls[0].State.Should().Contain(("ReceivedUtc", T0));
    }

    [Fact]
    public void AuditDuplicate_emits_one_entry_with_Disposition_Duplicate()
    {
        var captured = new CapturingLogger();
        var auditor = new AnalyzerScrollEventAuditor(captured);

        var actor = NewActor();
        var attempted = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();

        auditor.AuditDuplicate(actor, attempted, pageviewKey, AnalyzerScrollBucket.Quarter, T0);

        captured.Calls.Should().HaveCount(1);
        captured.Calls[0].Level.Should().Be(LogLevel.Information);
        captured.Calls[0].State.Should().Contain(("Disposition", "Duplicate"));
        captured.Calls[0].State.Should().Contain(("EventKey", attempted));
        captured.Calls[0].State.Should().Contain(("Bucket", AnalyzerScrollBucket.Quarter));
    }

    private static VisitorIdentity NewActor() =>
        new(IsAvailable: true,
            Key: Guid.NewGuid(),
            Oid: "oid-abc",
            Upn: "user@example.com",
            IsAnonymized: false);

    private sealed class CapturingLogger : ILogger<AnalyzerScrollEventAuditor>
    {
        public List<(LogLevel Level, List<(string Key, object? Value)> State)> Calls { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var props = new List<(string, object?)>();
            if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
            {
                foreach (var kv in kvs)
                {
                    props.Add((kv.Key, kv.Value));
                }
            }
            Calls.Add((logLevel, props));
        }
    }
}
