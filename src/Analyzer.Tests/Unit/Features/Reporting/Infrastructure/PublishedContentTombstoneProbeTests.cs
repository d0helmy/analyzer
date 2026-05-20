using Analyzer.Features.Reporting.Infrastructure;
using FluentAssertions;
using Umbraco.Cms.Core.Models.PublishedContent;
using Xunit;

namespace Analyzer.Tests.Unit.Features.Reporting.Infrastructure;

/// <summary>
/// Slice 008 / T023 — pins the contract that
/// <see cref="PublishedContentTombstoneProbe"/> reports
/// "currently tombstoned" iff the published-content lookup returns
/// null. The probe is constructed with a lookup function so the
/// production wiring's <see cref="Umbraco.Cms.Core.Web.IUmbracoContextAccessor"/>
/// dependency stays out of the test surface.
/// </summary>
public sealed class PublishedContentTombstoneProbeTests
{
    private static readonly Guid ContentKey = Guid.NewGuid();

    [Fact]
    public void Lookup_returns_null_then_probe_reports_tombstoned()
    {
        var probe = new PublishedContentTombstoneProbe(_ => (IPublishedContent?)null);

        probe.IsTombstoned(ContentKey).Should().BeTrue();
    }

    [Fact]
    public void Lookup_returns_content_then_probe_reports_live()
    {
        var probe = new PublishedContentTombstoneProbe(_ => StubContent);

        probe.IsTombstoned(ContentKey).Should().BeFalse();
    }

    [Fact]
    public void Lookup_receives_the_requested_content_key()
    {
        Guid? captured = null;
        var probe = new PublishedContentTombstoneProbe(key =>
        {
            captured = key;
            return null;
        });

        probe.IsTombstoned(ContentKey);

        captured.Should().Be(ContentKey);
    }

    private static IPublishedContent StubContent => new MinimalPublishedContent();

    private sealed class MinimalPublishedContent : IPublishedContent
    {
        public int Id => 1;
        public string Name => "Stub";
        public string? UrlSegment => "stub";
        public int SortOrder => 0;
        public int Level => 1;
        public string Path => "-1,1";
        public int? TemplateId => null;
        public int CreatorId => 0;
        public DateTime CreateDate => DateTime.UtcNow;
        public int WriterId => 0;
        public DateTime UpdateDate => DateTime.UtcNow;
        public Guid Key => ContentKey;
        public IPublishedContentType ContentType => null!;
        public IEnumerable<IPublishedProperty> Properties => Array.Empty<IPublishedProperty>();
        public IPublishedContent? Parent => null;
        public IEnumerable<IPublishedContent> Children => Array.Empty<IPublishedContent>();
        public IEnumerable<IPublishedContent> ChildrenForAllCultures => Array.Empty<IPublishedContent>();
        public IReadOnlyDictionary<string, PublishedCultureInfo> Cultures =>
            new Dictionary<string, PublishedCultureInfo>();
        public PublishedItemType ItemType => PublishedItemType.Content;
        public bool IsDraft(string? culture = null) => false;
        public bool IsPublished(string? culture = null) => true;
        public IPublishedProperty? GetProperty(string alias) => null;
    }
}
