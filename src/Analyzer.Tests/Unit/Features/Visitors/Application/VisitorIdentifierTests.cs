using System.Security.Claims;
using Analyzer.Features.Visitors.Application;
using Analyzer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Visitors.Application;

/// <summary>
/// Unit tests for <see cref="VisitorIdentifier"/> — covers all five
/// branches of <c>contracts/IVisitorIdentifier.md</c>'s behavior matrix:
///   oid+upn, upn-only (warning logged), oid-only, no-identity,
///   anonymized.
/// Spec SC-006 references this file as the authoritative branch
/// coverage.
/// </summary>
public sealed class VisitorIdentifierTests
{
    private static readonly Guid VisitorKey = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void OidAndUpnPresent_ReturnsAvailable_OidCanonical_UpnAsDisplay()
    {
        // Arrange
        var (sut, _) = BuildSut(
            profile: new FakeProfile { IsAvailable = true, VisitorKey = VisitorKey, IdentityRef = "oid:abc" },
            claims: new[]
            {
                new Claim(Constants.Claims.Oid, "abc-oid-value"),
                new Claim(Constants.Claims.Upn, "alice@tenant.com"),
            });

        // Act
        var id = sut.GetCurrent();

        // Assert
        id.IsAvailable.Should().BeTrue();
        id.Key.Should().Be(VisitorKey);
        id.Oid.Should().Be("abc-oid-value");
        id.Upn.Should().Be("alice@tenant.com");
        id.IsAnonymized.Should().BeFalse();
    }

    [Fact]
    public void UpnOnly_ReturnsAvailable_LogsWarningOnce()
    {
        // Arrange — upn claim only, no oid; simulates a misconfigured
        // external-login provider (Constitution Principle I fallback path)
        var sink = new CapturingLoggerProvider();
        var (sut, _) = BuildSut(
            profile: new FakeProfile { IsAvailable = true, VisitorKey = VisitorKey, IdentityRef = "upn:bob@tenant.com" },
            claims: new[] { new Claim(Constants.Claims.Upn, "bob@tenant.com") },
            loggerProvider: sink);

        // Act
        var id = sut.GetCurrent();

        // Assert: returns Available with null Oid and populated Upn; warning emitted
        id.IsAvailable.Should().BeTrue();
        id.Oid.Should().BeNull();
        id.Upn.Should().Be("bob@tenant.com");
        id.IsAnonymized.Should().BeFalse();

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("oid")
            && e.Message.Contains("upn"));
    }

    [Fact]
    public void OidOnly_ReturnsAvailable_NoWarning()
    {
        var sink = new CapturingLoggerProvider();
        var (sut, _) = BuildSut(
            profile: new FakeProfile { IsAvailable = true, VisitorKey = VisitorKey, IdentityRef = "oid:carol" },
            claims: new[] { new Claim(Constants.Claims.Oid, "carol-oid-value") },
            loggerProvider: sink);

        var id = sut.GetCurrent();

        id.IsAvailable.Should().BeTrue();
        id.Oid.Should().Be("carol-oid-value");
        id.Upn.Should().BeNull();
        id.IsAnonymized.Should().BeFalse();
        sink.Entries.Where(e => e.Level == LogLevel.Warning).Should().BeEmpty();
    }

    [Fact]
    public void NoIdentity_ReturnsNotAvailable_NoLog()
    {
        var sink = new CapturingLoggerProvider();
        var (sut, _) = BuildSut(
            profile: new FakeProfile { IsAvailable = false },
            claims: Array.Empty<Claim>(),
            loggerProvider: sink);

        var id = sut.GetCurrent();

        id.IsAvailable.Should().BeFalse();
        id.Key.Should().Be(Guid.Empty);
        id.Oid.Should().BeNull();
        id.Upn.Should().BeNull();
        id.IsAnonymized.Should().BeFalse();
        sink.Entries.Should().BeEmpty();
    }

    [Fact]
    public void Anonymized_ReturnsAvailable_OidAndUpnNull()
    {
        var (sut, _) = BuildSut(
            profile: new FakeProfile
            {
                IsAvailable = true,
                VisitorKey = VisitorKey,
                IdentityRef = $"anonymized:{VisitorKey}",
                IsAnonymized = true,
            },
            // Claims would be irrelevant; even if present, the anonymised
            // branch suppresses both.
            claims: new[]
            {
                new Claim(Constants.Claims.Oid, "should-be-suppressed"),
                new Claim(Constants.Claims.Upn, "should-be-suppressed"),
            });

        var id = sut.GetCurrent();

        id.IsAvailable.Should().BeTrue();
        id.Key.Should().Be(VisitorKey);
        id.Oid.Should().BeNull();
        id.Upn.Should().BeNull();
        id.IsAnonymized.Should().BeTrue();
    }

    // ---- helpers ----

    private static (VisitorIdentifier Sut, IHttpContextAccessor Accessor) BuildSut(
        FakeProfile profile,
        Claim[] claims,
        ILoggerProvider? loggerProvider = null)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Length > 0 ? "Test" : null)),
            },
        };
        ILogger<VisitorIdentifier> logger = loggerProvider is not null
            ? new LoggerFactory(new[] { loggerProvider }).CreateLogger<VisitorIdentifier>()
            : NullLogger<VisitorIdentifier>.Instance;
        return (new VisitorIdentifier(profile, accessor, logger), accessor);
    }

    private sealed class FakeProfile : IPersonalizationProfile
    {
        public bool IsAvailable { get; init; }
        public Guid VisitorKey { get; init; } = Guid.Empty;
        public string IdentityRef { get; init; } = string.Empty;
        public int VisitCount { get; init; }
        public DateTimeOffset ProfileCreatedUtc { get; init; }
        public DateTimeOffset LastSeenUtc { get; init; }
        public bool IsAnonymized { get; init; }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
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
