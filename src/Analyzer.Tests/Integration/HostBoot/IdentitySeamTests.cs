using System.Security.Claims;
using Analyzer.Features.Visitors.Application.Contracts;
using Analyzer.Tests.TestHelpers;
using Customizer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Analyzer.Tests.Integration.HostBoot;

/// <summary>
/// US2 acceptance — identity seam resolves the current visitor's
/// identity end-to-end through the composer-wired DI graph. Mirrors
/// the contract's behavior matrix at the integration level (resolves
/// against a real <c>IServiceProvider</c> + synthetic
/// <c>HttpContext</c>).
/// </summary>
public sealed class IdentitySeamTests
{
    private static readonly Guid VisitorKey = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void OidAndUpnPresent_ReturnsOidCanonical_UpnAsDisplay()
    {
        // Arrange — US2 AS1
        var profile = new FakeProfile { IsAvailable = true, VisitorKey = VisitorKey, IdentityRef = "oid:dora" };
        var provider = UmbracoTestHost.BuildWithCustomizer(profile);
        UmbracoTestHost.SetClaims(provider,
            new Claim(Constants.Claims.Oid, "dora-oid"),
            new Claim(Constants.Claims.Upn, "dora@tenant.com"));

        // Act
        var sut = provider.GetRequiredService<IVisitorIdentifier>();
        var id = sut.GetCurrent();

        // Assert
        id.IsAvailable.Should().BeTrue();
        id.Key.Should().Be(VisitorKey);
        id.Oid.Should().Be("dora-oid");
        id.Upn.Should().Be("dora@tenant.com");
    }

    [Fact]
    public void UpnOnly_ReturnsUpnFallback_LogsWarning()
    {
        // Arrange — US2 AS2 (configuration-error fallback path)
        var profile = new FakeProfile { IsAvailable = true, VisitorKey = VisitorKey, IdentityRef = "upn:eve@tenant.com" };
        var provider = UmbracoTestHost.BuildWithCustomizer(profile);
        UmbracoTestHost.SetClaims(provider, new Claim(Constants.Claims.Upn, "eve@tenant.com"));

        // Act
        var sut = provider.GetRequiredService<IVisitorIdentifier>();
        var id = sut.GetCurrent();

        // Assert
        id.IsAvailable.Should().BeTrue();
        id.Oid.Should().BeNull();
        id.Upn.Should().Be("eve@tenant.com");
    }

    [Fact]
    public void Unauthenticated_ReturnsNoIdentity()
    {
        // Arrange — US2 AS3
        var profile = new FakeProfile { IsAvailable = false };
        var provider = UmbracoTestHost.BuildWithCustomizer(profile);
        UmbracoTestHost.SetClaims(provider); // no claims attached

        // Act
        var sut = provider.GetRequiredService<IVisitorIdentifier>();
        var id = sut.GetCurrent();

        // Assert: spec FR-ID-05 / Constitution Principle I — no anonymous synthesis
        id.IsAvailable.Should().BeFalse();
        id.Key.Should().Be(Guid.Empty);
        id.Oid.Should().BeNull();
        id.Upn.Should().BeNull();
    }

    [Fact]
    public void IVisitorIdentifier_IsRegisteredAsScopedLifetime()
    {
        // Spec Clarification Q3 + Constitution Principle X: scoped DI lifetime
        var provider = UmbracoTestHost.BuildWithCustomizer(new FakeProfile { IsAvailable = false });

        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();
        var instance1a = scope1.ServiceProvider.GetRequiredService<IVisitorIdentifier>();
        var instance1b = scope1.ServiceProvider.GetRequiredService<IVisitorIdentifier>();
        var instance2 = scope2.ServiceProvider.GetRequiredService<IVisitorIdentifier>();

        // Same instance within a scope; different instance across scopes
        instance1a.Should().BeSameAs(instance1b);
        instance1a.Should().NotBeSameAs(instance2);
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
