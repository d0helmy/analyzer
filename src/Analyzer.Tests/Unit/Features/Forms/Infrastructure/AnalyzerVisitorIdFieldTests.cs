using Analyzer.Features.Forms.Infrastructure.UmbracoForms;
using Analyzer.Features.Visitors.Application.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Umbraco.Forms.Core.Enums;
using Umbraco.Forms.Core.Models;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Forms.Infrastructure;

/// <summary>
/// Slice 005 / T063 + T064 — Umbraco-Forms field-type metadata
/// (Id, Name, RenderInputType, DataType) + ConvertToRecord submit
/// hook conformance.
/// </summary>
public sealed class AnalyzerVisitorIdFieldTests
{
    [Fact]
    public void Metadata_carries_stable_slice_005_owned_id()
    {
        var field = new AnalyzerVisitorIdField();
        field.Id.Should().Be(new Guid("00000005-0000-0000-0000-000000000001"));
        field.Name.Should().Be("Analyzer Visitor ID");
        field.Icon.Should().Be("icon-user");
        field.DataType.Should().Be(FieldDataType.String);
        field.RenderInputType.Should().Be(RenderInputType.Single);
        field.SupportsRegex.Should().BeFalse();
    }

    [Fact]
    public void ConvertToRecord_writes_visitor_key_from_IVisitorIdentifier()
    {
        var visitor = Guid.NewGuid();
        var identity = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var httpContext = BuildHttpContext(new StubIdentifier(identity));

        var field = new AnalyzerVisitorIdField();
        var result = field.ConvertToRecord(new Field(), new object[] { "client-supplied-value" }, httpContext);

        result.Should().HaveCount(1);
        result.First().Should().Be(visitor.ToString());
    }

    [Fact]
    public void ConvertToRecord_overwrites_client_supplied_value()
    {
        var visitor = Guid.NewGuid();
        var identity = new VisitorIdentity(true, visitor, "oid-1", "user@example.com", false);
        var httpContext = BuildHttpContext(new StubIdentifier(identity));

        var field = new AnalyzerVisitorIdField();
        var clientValue = "spoofed";
        var result = field.ConvertToRecord(new Field(), new object[] { clientValue }, httpContext);

        result.First().Should().NotBe(clientValue);
        result.First().Should().Be(visitor.ToString());
    }

    [Fact]
    public void ConvertToRecord_writes_GuidEmpty_when_identifier_unavailable()
    {
        var unavailable = default(VisitorIdentity);
        var httpContext = BuildHttpContext(new StubIdentifier(unavailable));

        var field = new AnalyzerVisitorIdField();
        var result = field.ConvertToRecord(new Field(), Array.Empty<object>(), httpContext);

        result.Should().HaveCount(1);
        result.First().Should().Be(Guid.Empty.ToString());
    }

    [Fact]
    public void ConvertToRecord_writes_GuidEmpty_when_identifier_not_registered()
    {
        // Misconfig: no IVisitorIdentifier in RequestServices.
        var httpContext = BuildHttpContext(identifier: null);

        var field = new AnalyzerVisitorIdField();
        var result = field.ConvertToRecord(new Field(), Array.Empty<object>(), httpContext);

        result.First().Should().Be(Guid.Empty.ToString());
    }

    private static HttpContext BuildHttpContext(IVisitorIdentifier? identifier)
    {
        var services = new ServiceCollection();
        if (identifier is not null)
        {
            services.AddSingleton(identifier);
        }
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        var ctx = new DefaultHttpContext { RequestServices = sp };
        return ctx;
    }

    private sealed class StubIdentifier : IVisitorIdentifier
    {
        private readonly VisitorIdentity _identity;
        public StubIdentifier(VisitorIdentity identity) => _identity = identity;
        public VisitorIdentity GetCurrent() => _identity;
    }
}
