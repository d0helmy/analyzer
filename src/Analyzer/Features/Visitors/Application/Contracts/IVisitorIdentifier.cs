namespace Analyzer.Features.Visitors.Application.Contracts;

/// <summary>
/// Per-request identity seam. Returns the current request's visitor
/// identity (<c>oid</c>-first / <c>upn</c>-fallback per Constitution
/// Principle I) projected from Customizer's already-resolved
/// <see cref="Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile"/>
/// combined with raw EntraID claims read from the current
/// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> (so both
/// <see cref="VisitorIdentity.Oid"/> and <see cref="VisitorIdentity.Upn"/>
/// can be surfaced simultaneously, which Customizer's <c>IdentityRef</c>
/// prefix-string does not allow).
/// </summary>
/// <remarks>
/// <para>
/// Public extension contract for Analyzer slice 001. Stability:
/// preview at slice 001; public-surface pinning lands at slice 002
/// (spec Clarification Q2; Constitution Principle X).
/// </para>
/// <para>
/// Registered with <b>scoped</b> DI lifetime (spec Clarification Q3) —
/// one instance per HTTP request, matching <c>HttpContext</c> lifetime.
/// </para>
/// </remarks>
public interface IVisitorIdentifier
{
    /// <summary>
    /// Resolve the current request's visitor identity.
    /// </summary>
    /// <returns>
    /// A populated <see cref="VisitorIdentity"/> when the current
    /// request carries an authenticated EntraID context; otherwise a
    /// <see cref="VisitorIdentity"/> whose
    /// <see cref="VisitorIdentity.IsAvailable"/> is <c>false</c>
    /// (no anonymous-fallback synthesis — see <c>FR-ID-05</c>).
    /// </returns>
    VisitorIdentity GetCurrent();
}
