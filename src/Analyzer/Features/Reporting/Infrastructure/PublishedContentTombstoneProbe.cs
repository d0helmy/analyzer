using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Analyzer.Features.Reporting.Infrastructure;

/// <summary>
/// Default <see cref="IPublishedContentTombstoneProbe"/>. Resolves
/// the published-content cache via
/// <see cref="IUmbracoContextAccessor"/> and consults the GUID-keyed
/// lookup. A cache miss is interpreted as "currently tombstoned"
/// (research §R4).
/// </summary>
/// <remarks>
/// O(1) in-memory cache lookup — no DB hit. When no Umbraco context
/// is available (e.g. the request was issued outside a backoffice
/// request lifetime), the probe falls back to <c>true</c> (treats the
/// content as tombstoned), which is the safer default for the read-
/// side reporting endpoint: the query service still returns a
/// snapshot when any historical capture rows exist.
/// </remarks>
internal sealed class PublishedContentTombstoneProbe : IPublishedContentTombstoneProbe
{
    private readonly Func<Guid, IPublishedContent?> _lookup;

    public PublishedContentTombstoneProbe(IUmbracoContextAccessor contextAccessor)
        : this(key => LookupViaContext(contextAccessor, key))
    {
    }

    internal PublishedContentTombstoneProbe(Func<Guid, IPublishedContent?> lookup)
    {
        _lookup = lookup;
    }

    public bool IsTombstoned(Guid contentKey) => _lookup(contentKey) is null;

    private static IPublishedContent? LookupViaContext(IUmbracoContextAccessor accessor, Guid contentKey)
    {
        if (!accessor.TryGetUmbracoContext(out var context) || context is null)
        {
            return null;
        }
        return context.Content?.GetById(contentKey);
    }
}
