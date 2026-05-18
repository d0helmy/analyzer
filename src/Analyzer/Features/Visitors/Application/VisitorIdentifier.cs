using Analyzer.Features.Visitors.Application.Contracts;
using Customizer.Features.Visitors.Application.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Analyzer.Features.Visitors.Application;

/// <summary>
/// Default <see cref="IVisitorIdentifier"/>. Projects Customizer's
/// <see cref="IPersonalizationProfile"/> (canonical visitor key,
/// <c>IsAvailable</c>, <c>IsAnonymized</c>) combined with raw EntraID
/// claims read from <see cref="IHttpContextAccessor"/> (raw
/// <c>oid</c> and <c>upn</c> values, which Customizer's prefix-encoded
/// <c>IdentityRef</c> can only surface one of at a time).
/// </summary>
internal sealed class VisitorIdentifier : IVisitorIdentifier
{
    private readonly IPersonalizationProfile _profile;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<VisitorIdentifier> _logger;

    public VisitorIdentifier(
        IPersonalizationProfile profile,
        IHttpContextAccessor httpContextAccessor,
        ILogger<VisitorIdentifier> logger)
    {
        _profile = profile;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public VisitorIdentity GetCurrent()
    {
        // FR-ID-05 + Constitution Principle I: no anonymous fallback.
        if (!_profile.IsAvailable)
        {
            return default;
        }

        Guid key = _profile.VisitorKey;

        // Customizer's erasure cascade has run — surface IsAnonymized
        // with null oid/upn so downstream code MUST NOT display either.
        if (_profile.IsAnonymized)
        {
            return new VisitorIdentity(
                IsAvailable: true,
                Key: key,
                Oid: null,
                Upn: null,
                IsAnonymized: true);
        }

        // Read both claims independently — Customizer's IdentityRef
        // only carries one (oid: OR upn:), and Analyzer's contract
        // expects both to be surfaced when both are present.
        var user = _httpContextAccessor.HttpContext?.User;
        string? oid = user?.FindFirst(Constants.Claims.Oid)?.Value
                      ?? user?.FindFirst(Constants.Claims.OidShort)?.Value;
        string? upn = user?.FindFirst(Constants.Claims.Upn)?.Value
                      ?? user?.FindFirst(Constants.Claims.UpnShort)?.Value
                      ?? user?.FindFirst(Constants.Claims.PreferredUsername)?.Value;

        // upn-fallback configuration-error path (Constitution Principle I).
        if (oid is null && upn is not null)
        {
            _logger.LogWarning(
                "EntraID claim missing 'oid' for upn={Upn}; falling back to upn as canonical key. Configure external-login provider to emit 'oid'.",
                upn);
        }

        return new VisitorIdentity(
            IsAvailable: true,
            Key: key,
            Oid: oid,
            Upn: upn,
            IsAnonymized: false);
    }
}
