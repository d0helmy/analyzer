namespace Analyzer.Features.Visitors.Application.Contracts;

/// <summary>
/// Immutable, request-scoped visitor identity. Returned by
/// <see cref="IVisitorIdentifier.GetCurrent"/>.
/// </summary>
/// <param name="IsAvailable">
/// <c>true</c> when the current request carries an authenticated EntraID
/// context. <c>false</c> on the degraded path (no <c>oid</c>/<c>upn</c>
/// claim; unauthenticated request). Downstream code MUST check this
/// before reading the other members.
/// </param>
/// <param name="Key">
/// Canonical Customizer-assigned visitor key. <see cref="System.Guid.Empty"/>
/// when <see cref="IsAvailable"/> is <c>false</c>.
/// </param>
/// <param name="Oid">
/// EntraID <c>oid</c> claim (immutable object identifier), when the
/// host's external-login provider surfaces it. <c>null</c> on the
/// <c>upn</c>-fallback configuration-error case, on anonymisation, and
/// when <see cref="IsAvailable"/> is <c>false</c>.
/// </param>
/// <param name="Upn">
/// EntraID <c>upn</c> claim — the human-readable display form. <c>null</c>
/// after anonymisation and when <see cref="IsAvailable"/> is <c>false</c>;
/// also <c>null</c> in the unusual case where only <c>oid</c> is
/// surfaced (rare; not a warning condition).
/// </param>
/// <param name="IsAnonymized">
/// <c>true</c> once Customizer's erasure cascade has anonymised this
/// visitor. Downstream code MUST suppress UPN display when this is
/// <c>true</c> (<see cref="Oid"/> and <see cref="Upn"/> are already
/// <c>null</c> in this state).
/// </param>
public readonly record struct VisitorIdentity(
    bool IsAvailable,
    Guid Key,
    string? Oid,
    string? Upn,
    bool IsAnonymized);
