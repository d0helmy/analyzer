namespace Analyzer.Features.Reporting.Infrastructure;

/// <summary>
/// Slice 008 — abstraction over the
/// <see cref="Umbraco.Cms.Core.PublishedCache.IPublishedContentCache"/>
/// lookup that decides whether a content GUID is currently
/// tombstoned (unpublished or recycled). Defined behind an interface
/// so the query service is unit-testable without spinning up
/// Umbraco's published-content cache.
/// </summary>
internal interface IPublishedContentTombstoneProbe
{
    /// <summary>
    /// Returns <c>true</c> when the content node identified by
    /// <paramref name="contentKey"/> is absent from the published-
    /// content cache (deleted, unpublished, or never existed).
    /// </summary>
    bool IsTombstoned(Guid contentKey);
}
