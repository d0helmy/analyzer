using Analyzer.Composers;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.HostBoot;

/// <summary>
/// US1 acceptance — operator installs Analyzer alongside Customizer.
/// Verifies clean-boot path (Customizer wired in → composer succeeds
/// → IVisitorIdentifier resolvable) and fail-fast path (Customizer
/// absent → AnalyzerCompositionException).
/// </summary>
public sealed class ComposerSmokeTests
{
    [Fact]
    public void WithCustomizer_ComposesSuccessfully_RegistersIVisitorIdentifier()
    {
        // Arrange + Act: BuildWithCustomizer runs AnalyzerComposer.ConfigureServices
        var provider = UmbracoTestHost.BuildWithCustomizer(new FakeProfile { IsAvailable = false });

        // Assert: IVisitorIdentifier is registered and resolvable (US1 AS1)
        var sut = provider.GetService<IVisitorIdentifier>();
        sut.Should().NotBeNull("AnalyzerComposer must register IVisitorIdentifier when Customizer is present");
    }

    [Fact]
    public void WithoutCustomizer_FailsFast_ThrowsAnalyzerCompositionException()
    {
        // Arrange: service collection WITHOUT Customizer's IPersonalizationProfile
        var services = UmbracoTestHost.BuildWithoutCustomizer();

        // Act + Assert: AnalyzerComposer.ConfigureServices must throw a single
        // explicit error naming Customizer (US1 AS2, FR-002, Constitution III)
        var act = () => AnalyzerComposer.ConfigureServices(services);

        act.Should().Throw<AnalyzerCompositionException>()
            .Which.Message.Should().Contain("Customizer")
            .And.Contain("INTER-PRODUCT-CONTRACT.md");
    }

    [Fact]
    public void WithoutCustomizer_FailsFast_RegistersNoAnalyzerServices()
    {
        // Arrange
        var services = UmbracoTestHost.BuildWithoutCustomizer();
        int beforeCount = services.Count;

        // Act
        try
        {
            AnalyzerComposer.ConfigureServices(services);
        }
        catch (AnalyzerCompositionException)
        {
            // expected
        }

        // Assert: the composer aborted before any AddScoped/AddHttpContextAccessor
        // call ran. (Verifies the spec's "no partial registrations" semantic.)
        services.Should().HaveCount(beforeCount,
            "fail-fast path must not register any Analyzer services");
        services.Should().NotContain(sd => sd.ServiceType == typeof(IVisitorIdentifier));
    }

    [Fact]
    public void WithCustomizer_HostBoots_NoAnalyzerErrorsInComposition()
    {
        // US1 AS3: composing with Customizer present should not throw
        var act = () =>
        {
            var provider = UmbracoTestHost.BuildWithCustomizer(new FakeProfile { IsAvailable = false });
            _ = provider.GetService<IVisitorIdentifier>();
        };

        act.Should().NotThrow();
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
}
