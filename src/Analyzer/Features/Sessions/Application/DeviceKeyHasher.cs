using System.Security.Cryptography;
using System.Text;

namespace Analyzer.Features.Sessions.Application;

/// <summary>
/// Slice 003 — derives a stable per-request <c>deviceKey</c> from the
/// raw <c>User-Agent</c> header carried on
/// <c>Customizer.Features.Visitors.Domain.Pageview.UserAgent</c>
/// (cross-product prerequisite shipped at Customizer
/// <c>5273c38</c>; inter-product contract §6 item 2).
/// </summary>
/// <remarks>
/// <para>
/// Truncated SHA-256 over the UTF-8 bytes of the trimmed UA string;
/// returns the first 8 bytes as a 16-hex lowercase string. UA
/// cardinality per organisation is ≤ a few hundred (Chrome, Edge,
/// Firefox, Safari × major version × OS family); 64-bit hash space
/// makes birthday-paradox collisions astronomically unlikely at this
/// scale (research §5).
/// </para>
/// <para>
/// Null or whitespace UA hashes the empty string deterministically,
/// producing a fixed sentinel device key. Rare in practice (every
/// real HTTP client sends a UA); the resolver tolerates it.
/// </para>
/// <para>
/// NOT a public extension surface — internal static helper. Callers
/// (the resolver) source the UA from <c>notification.Pageview.UserAgent</c>,
/// NOT from <c>IHttpContextAccessor</c> (lesson #40 — unreliable
/// under Customizer's fire-and-forget dispatch).
/// </para>
/// </remarks>
internal static class DeviceKeyHasher
{
    /// <summary>
    /// Compute a stable 16-hex device-key from the supplied User-Agent
    /// string. Null / whitespace tolerated; returns the deterministic
    /// empty-string sentinel hash.
    /// </summary>
    public static string Compute(string? userAgent)
    {
        var normalised = (userAgent ?? string.Empty).Trim();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalised));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
