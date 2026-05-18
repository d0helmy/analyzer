# Contract — `IVisitorIdentifier`

**Feature**: `001-package-skeleton`
**Date**: 2026-05-18
**Stability**: public; first version of an Analyzer-pinned surface (formal pinning tests deferred to slice 002 per spec Clarification Q2).

The `IVisitorIdentifier` is the single identity seam Analyzer exposes to its own future slices and (potentially) to third-party Analyzer extensions. It is the in-process surface that every event-recording slice (002+) will consume to attribute events to a visitor.

## Namespace

```
Analyzer.Features.Visitors.Application.Contracts.IVisitorIdentifier
```

Deliberately distinct from Customizer's `Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile` and `Customizer.Features.Visitors.Application.Contracts.IAnalyticsStateProvider` (Constitution Principle III — no name collision with Customizer's pinned surface).

## Shape

```csharp
namespace Analyzer.Features.Visitors.Application.Contracts;

/// <summary>
/// Per-request identity seam. Returns the current request's visitor
/// identity (oid-first / upn-fallback, per Constitution Principle I)
/// projected from Customizer's already-resolved
/// <see cref="Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile"/>.
/// </summary>
public interface IVisitorIdentifier
{
    /// <summary>
    /// Resolve the current request's visitor identity.
    /// </summary>
    /// <returns>
    /// A populated <see cref="VisitorIdentity"/> when the current
    /// request carries an authenticated EntraID context; otherwise
    /// a <see cref="VisitorIdentity"/> whose
    /// <see cref="VisitorIdentity.IsAvailable"/> is <c>false</c>
    /// (no anonymous-fallback synthesis — see <c>FR-ID-05</c>).
    /// </returns>
    VisitorIdentity GetCurrent();
}
```

```csharp
namespace Analyzer.Features.Visitors.Application.Contracts;

/// <summary>
/// Immutable, request-scoped identity value. See
/// <c>specs/001-package-skeleton/data-model.md</c> for invariants.
/// </summary>
public readonly record struct VisitorIdentity(
    bool IsAvailable,
    Guid Key,
    string? Oid,
    string? Upn,
    bool IsAnonymized);
```

## Behavior

### Inputs

`IVisitorIdentifier` takes no caller-provided arguments. Internally, the implementation depends on:

1. `IPersonalizationProfile` (Customizer; per-request DI surface) — the canonical source of `IsAvailable`, `VisitorKey`, `IdentityRef`.
2. (Optional, implementation-internal) `ILogger<VisitorIdentifier>` for the `upn`-fallback warning log.

### Outputs

| Condition | `IsAvailable` | `Key` | `Oid` | `Upn` | `IsAnonymized` | Side effect |
|---|---|---|---|---|---|---|
| Authenticated; `oid` and `upn` both present | `true` | non-empty Guid | `<oid value>` | `<upn value>` | `false` | none |
| Authenticated; `upn` present, `oid` absent (configuration error) | `true` | non-empty Guid | `null` | `<upn value>` | `false` | **Warning log** emitted once per request: `"EntraID claim missing 'oid' for upn={upn}; falling back to upn as canonical key. Configure external-login provider to emit 'oid'."` |
| Authenticated; `oid` present, `upn` absent | `true` | non-empty Guid | `<oid value>` | `null` | `false` | none (rare; not a warning) |
| Unauthenticated request | `false` | `Guid.Empty` | `null` | `null` | `false` | none — caller MUST NOT proceed with event recording (`FR-ID-05`, Principle I) |
| Visitor previously anonymised (Customizer's erasure cascade ran) | `true` | non-empty Guid | `null` | `null` | `true` | none — caller MUST NOT display `Upn`/`Oid` (both already `null`); downstream displays use the `Key` Guid only |

### Determinism / idempotence

- Resolution is **idempotent within a request**: calling `GetCurrent()` multiple times in the same request returns the same `VisitorIdentity` value. Underlying `IPersonalizationProfile` is implementation-promised to do likewise (Customizer guarantee).
- Resolution is **scoped per request**: a new `HttpContext` always re-projects from `IPersonalizationProfile`; nothing is cached across requests.

### Threading

- The implementation is scoped (per-request DI lifetime; spec Clarification Q3); not thread-safe and not required to be — the scope itself enforces single-thread-per-request semantics in ASP.NET Core / Umbraco's request pipeline.

### Failure modes

- **Customizer's `IPersonalizationProfile` is not registered**: cannot reach this contract because `AnalyzerComposer` fails fast at startup (FR-002) — Analyzer would not have started.
- **`IPersonalizationProfile.IsAvailable` throws**: implementation contract says it never does (`IPersonalizationProfile` "MUST NOT throw on access" per Customizer's `IPersonalizationProfile` doc). Analyzer treats a hypothetical throw as a programmer error in Customizer and surfaces it; does not catch.
- **`IdentityRef` is malformed (unknown prefix)**: implementation returns `IsAvailable = false` and emits a warning log; treated as the degraded path. Realistically unreachable since Customizer constructs `IdentityRef` programmatically.

## Acceptance verification

This contract is tested at slice 001 by:

- **Unit** (`tests/Analyzer.Tests/Unit/Features/Visitors/Application/VisitorIdentifierTests.cs`):
  - `Given_OidAndUpn_Returns_Available_OidCanonical`
  - `Given_UpnWithoutOid_Returns_Available_LogsWarning`
  - `Given_OidWithoutUpn_Returns_Available_NoWarning`
  - `Given_NoIdentity_Returns_NotAvailable`
  - `Given_Anonymized_Returns_Available_NullOidAndUpn`
- **Integration** (`tests/Analyzer.Tests/Integration/HostBoot/IdentitySeamTests.cs`):
  - End-to-end resolution against a real Umbraco service collection with Customizer wired in; covers User Story 2 acceptance scenarios 1, 2, 3.

## Stability

- **MINOR additions**: new members on `VisitorIdentity` (additive fields) are permitted in MINOR releases.
- **MAJOR changes**: removing or re-typing existing members; renaming the contract or value type; changing the namespace.
- Public-surface pinning tests (Customizer-style `PublicSurfacePinningTests`) are deferred to slice 002. At that point, this contract's signature locks in.

## Out of scope for this contract

- Recording events keyed by the returned identity (slice 002+).
- Mapping `Oid` / `Upn` to richer EntraID claims (`department`, `officeLocation`, etc.) — that is `IEventDimensionExtractor`'s job (slice 002+).
- Role-gating which user groups can read `Upn` from analytics surfaces — that is the consumers' job (slice 005+ Analytics content app, slice 012+ reports).
