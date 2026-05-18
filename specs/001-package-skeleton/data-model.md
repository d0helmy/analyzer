# Data Model — Package Skeleton

**Feature**: `001-package-skeleton`
**Date**: 2026-05-18

## Summary

**Slice 001 owns no persistent data.** This document records the read-through identity reference Analyzer takes against Customizer's substrate, plus the in-memory value type returned by `IVisitorIdentifier`. There are no migrations, no Analyzer-owned tables, and no cascade-step registrations in this slice.

This matches Constitution Principle IV applied vacuously: Principle IV binds slices that *introduce* Analyzer-owned event/side tables; slice 001 introduces none. The first Analyzer-owned table is `analyzerSession` in slice 003.

## Entities

### `VisitorIdentity` *(in-memory value type; no persistence)*

The return shape of `IVisitorIdentifier.GetCurrent()`. A `readonly record struct` (C# 12+) constructed at the seam boundary by projecting `Customizer.Features.Visitors.Application.Contracts.IPersonalizationProfile`.

| Field | Type | Source | Notes |
|---|---|---|---|
| `IsAvailable` | `bool` | `IPersonalizationProfile.IsAvailable` | `false` on the degraded path (no `oid`/`upn` claim; unauthenticated request). Downstream code MUST check this before reading the other members. |
| `Key` | `Guid` | `IPersonalizationProfile.VisitorKey` | The canonical visitor key — same value Customizer's tables FK on. `Guid.Empty` when `IsAvailable == false`. |
| `Oid` | `string?` | parsed from `IdentityRef` (`oid:<guid>` prefix) | The EntraID `oid` claim, if present. Drives the `oid`-first invariant (Constitution Principle I). `null` on the `upn`-fallback configuration-error case. |
| `Upn` | `string?` | claims or parsed from `IdentityRef` (`upn:<value>` prefix) | The EntraID UPN — used as the display form (audit logs, backoffice surfaces) and as the configuration-error fallback when `oid` is absent. `null` when the request has no UPN claim. |
| `IsAnonymized` | `bool` | parsed from `IdentityRef` (`anonymized:` prefix) | `true` once Customizer has cascade-anonymised this visitor. Downstream code MUST suppress UPN display when `true`. Slice 001 has no UPN-displaying surface, so this is documented for symmetry — no code branch exercises it yet. |

### Validation / invariants

- `IsAvailable == true` ⇒ `Key != Guid.Empty`.
- `IsAvailable == true` ⇒ exactly one of `(Oid, Upn)` is non-null at minimum (typically both).
- `IsAvailable == false` ⇒ `Key == Guid.Empty` ∧ `Oid == null` ∧ `Upn == null`.
- `IsAnonymized == true` ⇒ both `Oid` and `Upn` are `null` (Customizer's anonymisation overwrites `IdentityRef` to `anonymized:<key>`).

### State transitions

`VisitorIdentity` is immutable per request; no state transitions during the request lifetime. Across the visitor's full life:

- `available` ↔ `unavailable`: per-request transition driven by whether the current request carries an authenticated EntraID context.
- `available` → `anonymized`: irreversible, triggered by an operator-initiated erasure action in Customizer (slice 003/007). Analyzer registers no cascade steps in slice 001 because it owns no tables to cascade into.

## Relationships

```
[ Customizer-owned ]                        [ Analyzer slice-001 ]
─────────────────────────                   ──────────────────────────
customizerVisitorProfile                    (no Analyzer-owned table)
  Key (Guid, PK)               ────read────► IVisitorIdentifier.GetCurrent()
  IdentityRef (string)         ────read────► VisitorIdentity { Key, Oid, Upn, ... }
  ...                                         (returned to callers; no storage)

IPersonalizationProfile  ─────────injected────► VisitorIdentifier (scoped)
  IsAvailable                  ─read─►          VisitorIdentity.IsAvailable
  VisitorKey                   ─read─►          VisitorIdentity.Key
  IdentityRef                  ─parse─►         VisitorIdentity.Oid / .Upn / .IsAnonymized
```

**Direction is read-only.** Analyzer never writes to `customizerVisitorProfile`. Analyzer never holds a reference to a Customizer row across requests — the seam is scoped per-request and re-resolves on each new `HttpContext`.

## Volume / scale assumptions

- 1 instance per HTTP request (scoped DI lifetime; spec Clarification Q3).
- Identity resolution is O(1) given Customizer's per-request state is already populated by Customizer's `PageviewCaptureMiddleware` (slice-003); Analyzer only projects.
- No new index, no new query, no new write — Analyzer's slice-001 read-through adds zero load to Customizer's substrate.

## Future-slice extension points (informative)

Future Analyzer-owned entities, each FK'd to the substrate (per Constitution Principle IV and inter-product contract D11):

- `analyzerSession` — slice 003 (D11)
- `analyzerCustomEvent` — slice 004
- `analyzerVideoEvent` — slice 007
- `analyzerFormsEvent` — slice 008
- `analyzerScrollSample` — slice 009
- `analyzerSearchEvent` — slice 010
- (per-EntraID enrichment side table, if §6 item 2 resolves to option (b)) — slice 014

None of these are introduced or referenced in slice 001 code.
