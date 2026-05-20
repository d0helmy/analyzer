using System.Security.Claims;

namespace Analyzer.Features.Reporting.Application.Authorization;

/// <summary>
/// Slice 008 — role-gate primitive for per-visitor (individual-level)
/// data. The MVP slice ships no fields to filter; the check exists
/// so the future per-visitor drill-down slice can land its UI
/// without re-plumbing authorisation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal in MVP</b> per Spec Clarifications §4. Promotion to a
/// public extension surface is the responsibility of the slice that
/// introduces the first per-visitor field. See
/// <c>contracts/IIndividualDataAccessCheck.md</c>.
/// </para>
/// </remarks>
internal interface IIndividualDataAccessCheck
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="principal"/> carries
    /// a user-group claim matching the configured alias. Otherwise
    /// <c>false</c>, including the anonymous-principal case.
    /// </summary>
    bool IsAuthorised(ClaimsPrincipal principal);
}
