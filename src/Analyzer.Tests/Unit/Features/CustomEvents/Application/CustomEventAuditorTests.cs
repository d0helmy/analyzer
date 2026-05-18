using Analyzer.Features.CustomEvents.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Analyzer.Tests.Unit.Features.CustomEvents.Application;

/// <summary>
/// Slice 004 / T043 — verifies <see cref="CustomEventAuditor"/> emits
/// a single <c>LogInformation</c> entry carrying the named properties
/// the audit log contract pins (FR-008, research §5):
/// <c>AuditAction</c>, <c>ActorUpn</c>, <c>ActorOid</c>,
/// <c>EventKey</c>, <c>Category</c>, <c>Action</c>, <c>ReceivedUtc</c>.
/// </summary>
public sealed class CustomEventAuditorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Audit_emits_structured_log_with_named_properties()
    {
        var logger = new CapturingLogger();
        var auditor = new CustomEventAuditor(logger);
        var actor = new VisitorIdentity(true, Guid.NewGuid(), "oid-123", "user@example.com", false);
        var eventKey = Guid.NewGuid();

        auditor.Audit(actor, eventKey, "engagement", "click", T0);

        logger.Entries.Should().HaveCount(1);
        var entry = logger.Entries[0];
        entry.LogLevel.Should().Be(LogLevel.Information);

        var formatted = entry.Format();
        formatted.Should().Contain(Constants.AuditLog.CustomEventCapture);
        formatted.Should().Contain("user@example.com");
        formatted.Should().Contain("oid-123");
        formatted.Should().Contain(eventKey.ToString());
        formatted.Should().Contain("engagement");
        formatted.Should().Contain("click");

        // Named property assertions — the structured-logging contract.
        entry.Properties.Should().ContainKey("AuditAction");
        entry.Properties.Should().ContainKey("ActorUpn");
        entry.Properties.Should().ContainKey("ActorOid");
        entry.Properties.Should().ContainKey("EventKey");
        entry.Properties.Should().ContainKey("Category");
        entry.Properties.Should().ContainKey("Action");
        entry.Properties.Should().ContainKey("ReceivedUtc");
    }

    /// <summary>
    /// Minimal <see cref="ILogger{T}"/> that captures the message
    /// template + named property values so tests can inspect the
    /// structured-logging shape.
    /// </summary>
    private sealed class CapturingLogger : ILogger<CustomEventAuditor>
    {
        public List<CapturedEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var props = new Dictionary<string, object?>();
            if (state is IReadOnlyList<KeyValuePair<string, object?>> list)
            {
                foreach (var pair in list)
                {
                    props[pair.Key] = pair.Value;
                }
            }
            Entries.Add(new CapturedEntry(
                logLevel,
                formatter(state, exception),
                props));
        }

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }

    private sealed record CapturedEntry(
        LogLevel LogLevel,
        string Message,
        Dictionary<string, object?> Properties)
    {
        public string Format() => Message;
    }
}
