using Analyzer.Analytics;
using Analyzer.Features.Forms.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Application;

/// <summary>
/// Slice 005 / T024 — asserts the audit-log scope carries the
/// load-bearing named properties (research §R6 + FR-009).
/// </summary>
public sealed class AnalyzerFormEventAuditorTests
{
    [Fact]
    public void Audit_emits_one_log_entry_with_named_properties()
    {
        var captured = new CapturingLogger();
        var auditor = new AnalyzerFormEventAuditor(captured);

        var actor = new VisitorIdentity(
            IsAvailable: true,
            Key: Guid.NewGuid(),
            Oid: "oid-abc",
            Upn: "user@example.com",
            IsAnonymized: false);
        var eventKey = Guid.NewGuid();
        var formKey = Guid.NewGuid();
        var receivedUtc = new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero);

        auditor.Audit(actor, eventKey, formKey, AnalyzerFormEventType.Start, receivedUtc);

        captured.Calls.Should().HaveCount(1);
        captured.Calls[0].Level.Should().Be(LogLevel.Information);
        captured.Calls[0].State.Should().Contain(("AuditAction", Constants.AuditLog.FormEventCapture));
        captured.Calls[0].State.Should().Contain(("ActorUpn", "user@example.com"));
        captured.Calls[0].State.Should().Contain(("ActorOid", "oid-abc"));
        captured.Calls[0].State.Should().Contain(("EventKey", eventKey));
        captured.Calls[0].State.Should().Contain(("FormKey", formKey));
        captured.Calls[0].State.Should().Contain(("EventType", AnalyzerFormEventType.Start));
        captured.Calls[0].State.Should().Contain(("ReceivedUtc", receivedUtc));
    }

    private sealed class CapturingLogger : ILogger<AnalyzerFormEventAuditor>
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
