using Analyzer.Analytics;
using Analyzer.Features.Events.Infrastructure.Persistence;
using Analyzer.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Scoping;
using Xunit;

namespace Analyzer.Tests.Integration.Events;

/// <summary>
/// #62 — DB-rooted proof that <see cref="AnalyzerEventReceiptRepository.InsertAsync"/>
/// tolerates duplicate dispatch via the central
/// <c>UniqueConstraintViolationDetector</c> (fixed in #59). Live SQL
/// Server execution is required: <c>SqlException.Number</c> is read via
/// reflection inside the detector, so any provider-property regression
/// would only surface here — not in unit tests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AnalyzerEventReceiptRepositoryIdempotencyTests : AnalyzerIntegrationTestBase
{
    [Fact]
    public async Task Duplicate_insert_tolerated_one_row_persists_and_debug_log_emitted()
    {
        var visitor = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);

        var receipt = new AnalyticsEventReceipt(
            Id: Guid.NewGuid(),
            PageviewKey: Guid.NewGuid(),
            VisitorProfileKey: visitor,
            ReceivedUtc: DateTimeOffset.UtcNow);

        var sink = new CapturingLoggerProvider();
        var repo = new AnalyzerEventReceiptRepository(
            Services.GetRequiredService<IScopeProvider>(),
            new CapturingLogger<AnalyzerEventReceiptRepository>(sink.Entries));

        var ct = TestContext.Current.CancellationToken;

        // First insert succeeds.
        await repo.InsertAsync(receipt, ct);

        // Second insert with the same Id (and PageviewKey) collides on
        // both PK and UX_analyzerEventReceipt_pageviewKey — the central
        // detector must catch either, the catch must complete the scope,
        // and the handler must log the "Duplicate dispatch tolerated"
        // debug line. No exception escapes.
        var act = async () => await repo.InsertAsync(receipt, ct);
        await act.Should().NotThrowAsync();

        Count(visitor).Should().Be(1, "duplicate dispatch must be a no-op");

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Debug &&
            e.Message.Contains("Duplicate dispatch tolerated", StringComparison.Ordinal));
    }

    private int Count(Guid visitor)
    {
        using var scope = ScopeProvider.CreateScope();
        var count = scope.Database.ExecuteScalar<int>(
            $"SELECT COUNT(*) FROM {Constants.Database.AnalyzerEventReceipt} WHERE visitorProfileKey = @0",
            visitor);
        scope.Complete();
        return count;
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries;
        public CapturingLogger(List<LogEntry> entries) => _entries = entries;
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
