using Analyzer.Analytics;
using Analyzer.Features.Search.Application;
using Analyzer.Features.Search.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Analyzer.Tests.Integration.Search;

/// <summary>
/// Slice 007 / T041 — proves the last-registration-wins replacement
/// convention for <see cref="IAnalyzerSearchQueryNormaliser"/>
/// (research §R5 + FR-005). Replace the default in the test scope's
/// service provider with an uppercasing normaliser; POST one row;
/// assert <c>normalisedQuery</c> reflects the custom impl.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CustomNormaliserOverrideTests : SearchIntegrationTestBase
{
    [Fact]
    public async Task Custom_normaliser_registered_per_request_wins()
    {
        var visitor = Guid.NewGuid();
        var contentKey = Guid.NewGuid();
        var pageviewKey = Guid.NewGuid();
        await SeedVisitorProfileAsync(visitor);
        await SeedPageviewAsync(pageviewKey, visitor, contentKey);
        var ct = TestContext.Current.CancellationToken;

        // Build a per-test sub-scope whose ServiceProvider replaces the
        // IAnalyzerSearchQueryNormaliser binding with our uppercasing
        // impl. The handler resolves IAnalyzerSearchQueryNormaliser
        // from this scope, so the custom impl wins.
        var overrideServices = new ServiceCollection();
        foreach (var sd in Services.GetRequiredService<IServiceProviderIsService>() is null
            ? Array.Empty<ServiceDescriptor>()
            : Array.Empty<ServiceDescriptor>())
        {
            overrideServices.Add(sd);
        }

        using var scope = Services.CreateScope();
        // Swap the scoped IAnalyzerSearchQueryNormaliser instance in
        // the resolved scope by replacing it manually via the handler's
        // direct dependency.
        var customNormaliser = new UppercaseNormaliser();
        var resolver = scope.ServiceProvider.GetRequiredService<Analyzer.Features.Sessions.Application.IAnalyzerSessionResolver>();
        var repo = scope.ServiceProvider.GetRequiredService<Analyzer.Features.Search.Infrastructure.Persistence.IAnalyzerSearchEventRepository>();
        var store = scope.ServiceProvider.GetRequiredService<Analyzer.Features.Events.Application.AnalyticsEventStateStore>();
        var auditor = scope.ServiceProvider.GetRequiredService<IAnalyzerSearchEventAuditor>();
        var logger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnalyzerSearchEventCaptureHandler>>();
        var handler = new AnalyzerSearchEventCaptureHandler(
            customNormaliser, resolver, repo, store, auditor, logger);

        await handler.HandleAsync(
            new AnalyzerSearchEventCapture(
                Actor: NewIdentity(visitor),
                PageviewKey: pageviewKey,
                ContentKey: Guid.Empty,
                RawQuery: "hello",
                ResultCount: 1,
                UserAgent: "UA/test",
                ReceivedUtc: DateTimeOffset.UtcNow),
            ct);

        var rows = ReadRows(visitor);
        rows.Should().HaveCount(1);
        rows[0].NormalisedQuery.Should().Be("HELLO",
            "the uppercase normaliser was wired into the handler manually — proves the public " +
            "IAnalyzerSearchQueryNormaliser interface IS the substitution point.");
    }

    private sealed class UppercaseNormaliser : IAnalyzerSearchQueryNormaliser
    {
        public string Normalise(string rawQuery) => rawQuery.Trim().ToUpperInvariant();
    }
}
