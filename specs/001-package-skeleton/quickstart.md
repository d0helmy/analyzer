# Quickstart — Package Skeleton (slice 001)

**Feature**: `001-package-skeleton`
**Date**: 2026-05-18

This document tells a fresh agent (or developer) how to build, install, and smoke-test slice 001 end-to-end. Outcome: a host Umbraco 17.x site boots cleanly with Analyzer + Customizer wired in, `IVisitorIdentifier` resolves identity from claims, and the Analyzer backoffice bundle loads in the backoffice.

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | Server build |
| Node | 20.17+ (LTS) | Client build under `src/Analyzer/Client/` |
| An Umbraco CMS 17.x host | 17.3.5 (pinned) | Reference Analyzer + Customizer as project refs or NuGet refs |
| Customizer | ≥ slice-011 (commit `05e989c`) | Required at composition; Analyzer fails fast without it |

## Build

```bash
# from repo root
dotnet restore
dotnet build Analyzer.slnx

# backoffice client (one-shot)
cd src/Analyzer/Client
npm install
npm run build      # emits → ../wwwroot/App_Plugins/Analyzer/analyzer.js

# or watch for development
npm run watch      # pair with a host Umbraco site referencing src/Analyzer/ as a ProjectReference
```

## Install into a host Umbraco 17.x site

For local development:

1. In your host's `.csproj`, add a `<ProjectReference>` to `src/Analyzer/Analyzer.csproj` AND to `../customizer/src/Customizer/Customizer.csproj`.
2. Run the host: `dotnet run --project <host.csproj>`.
3. Expected: clean boot. The application log shows `AnalyzerComposer.Compose` registering services. No errors.

For a fresh test of fail-fast behavior (User Story 1 acceptance scenario 2):

1. Configure the host with **only** Analyzer (no Customizer reference).
2. Start the host.
3. Expected: startup fails at composition with `AnalyzerCompositionException: Customizer is a required runtime dependency of Analyzer. See docs/INTER-PRODUCT-CONTRACT.md §1.`. No partial Analyzer registrations remain.

## Smoke tests (manual, per User Story)

### US1 — Operator installs Analyzer alongside Customizer

```bash
# from repo root, with a configured host that references both packages:
dotnet run --project <host.csproj>
```

Open the host's root URL. The page renders. The application log contains:

```
info: Analyzer.Composers.AnalyzerComposer[0]
      Analyzer composed. Customizer presence verified via IPersonalizationProfile.
```

No `AnalyzerCompositionException` is thrown. No exception traces involving Analyzer in the log.

### US2 — Identity seam resolves visitor identity

In an Umbraco controller or middleware in the host site, inject `IVisitorIdentifier`:

```csharp
using Analyzer.Features.Visitors.Application.Contracts;

public class TestController : Controller
{
    private readonly IVisitorIdentifier _visitor;
    public TestController(IVisitorIdentifier visitor) => _visitor = visitor;

    public IActionResult Whoami()
    {
        var v = _visitor.GetCurrent();
        return Json(new { v.IsAvailable, v.Key, v.Oid, v.Upn });
    }
}
```

Visit `/whoami` while authenticated via the host's EntraID external login provider. Expected response shape:

```json
{
  "IsAvailable": true,
  "Key": "00000000-0000-0000-0000-000000000001",
  "Oid": "<your-entraid-oid>",
  "Upn": "<your-upn@tenant>"
}
```

Visit `/whoami` from an unauthenticated context (e.g. browser without an EntraID session). Expected:

```json
{
  "IsAvailable": false,
  "Key": "00000000-0000-0000-0000-000000000000",
  "Oid": null,
  "Upn": null
}
```

### US3 — Backoffice bundle loads in the host backoffice

1. Open the host's backoffice (typically `/umbraco`).
2. Open browser DevTools → Network tab → filter on `analyzer`.
3. Refresh. Confirm:
   - `GET /App_Plugins/Analyzer/umbraco-package.json` → HTTP 200
   - `GET /App_Plugins/Analyzer/analyzer.js` → HTTP 200
4. Open the DevTools Console. Run:
   ```js
   window.Analyzer
   ```
   Expected output: `{ version: "0.1.0" }` (or whatever version is published).
5. Confirm zero JavaScript console errors mentioning `analyzer.js` or `App_Plugins/Analyzer`.

## Automated tests

```bash
# from repo root
dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj
```

Runs:
- Unit tests for `IVisitorIdentifier` (all five branches per the contract).
- Integration tests via `UmbracoTestHost` — clean boot, fail-fast on missing Customizer, identity seam end-to-end.
- Bundle integration test — `App_Plugins/Analyzer/analyzer.js` returns HTTP 200 from the integration host.

```bash
# client bundle test (Vitest)
cd src/Analyzer/Client
npm run test
```

Asserts `window.Analyzer = { version }` is set after bundle import.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| `AnalyzerCompositionException: Customizer is a required runtime dependency` at startup | Customizer not installed or composer not registered | Ensure Customizer is referenced and Customizer's composer runs before Analyzer's (Umbraco runs composers in registration order; Analyzer does not depend on a specific order, but Customizer must be in the graph). |
| `/whoami` returns `IsAvailable=false` for an authenticated user | External-login provider in the host is not emitting `oid` or `upn` claims | Configure the host's EntraID external-login provider to map claims `oid` and `upn` (or `preferred_username`). See `FR-IDP-02`. |
| `IsAvailable=true` but `Oid=null` and a warning log appears | Host's external-login provider emits `upn` but not `oid` | This is the configuration-error fallback path (Constitution Principle I). Either fix the provider config to emit `oid`, or accept the fallback and acknowledge the warning. |
| `GET /App_Plugins/Analyzer/analyzer.js` → 404 | Client bundle not built, or RCL not serving static assets | Run `npm run build` from `src/Analyzer/Client/`. Confirm `wwwroot/App_Plugins/Analyzer/analyzer.js` exists in the published RCL. |
| `window.Analyzer` is undefined in the console | Bundle loaded but `index.ts` didn't run, or wrong path | Check DevTools Network for the actual URL fetched; confirm `umbraco-package.json` points at the right entrypoint. |

## What's NOT in slice 001 (deferred)

Per spec and Clarifications:

- No event recording of any kind (FR-008). Slice 002 introduces `IAnalyticsEventStateProvider` and the pageview subscription.
- No `analyzer.send()` client API (Clarification Q4). Slice 004.
- No management endpoints; no backoffice route prefix pinned (Clarification Q5). Slice 005.
- No per-content-node Analytics content app. Slice 005.
- No Analyzer-owned tables, no cascade-step registrations (vacuous per Constitution Principle IV). Slice 003 introduces `analyzerSession`.
- No public-surface pinning tests (Clarification Q2). Slice 002.
