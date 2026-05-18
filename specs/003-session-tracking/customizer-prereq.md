# Customizer-side prerequisite for Analyzer slice 003

**Audience**: a Customizer-repo agent or developer-session about to execute the Customizer-side work needed before Analyzer slice 003 can ship.
**Customizer repo**: `../customizer/` (clone path from Analyzer) or `https://github.com/d0helmy/customizer`.
**Cross-product driver**: Analyzer slice 003 (`specs/003-session-tracking/`) needs `User-Agent` carried on the `PageviewCaptured` notification so its session resolver can derive a stable `deviceKey` per visitor-device combination. Without this prerequisite, slice 003's `deviceKey` source (`HttpContext.Request.Headers.UserAgent`) is unreliable — the handler typically runs on a `Task.Run` thread after the request scope is disposed, returning null UA, which would collapse all of a visitor's pageviews into one device-keyed session.
**Decision reference**: Analyzer's `/speckit-analyze` finding **C1** identified this gap; the user chose **Path A** (cross-product Customizer change) over Path B (Analyzer-only re-scope).

---

## Goal

Add `UserAgent` as an in-memory positional record parameter on Customizer's `Pageview` domain record (slot 10, default null). Capture the value synchronously on the request thread inside `PageviewCaptureMiddleware`. **Do not persist** — the value is request-only; Analyzer hashes it to a 16-hex `deviceKey` before persistence. `PageviewDto` does not change, and no migration is needed.

The change is **MINOR-additive** per the Customizer constitution's positional-record additivity rule (slice-007 UTM trio precedent + `PageviewPositionalRecordAdditivityTests` guard).

---

## TODO

### Setup

- [ ] Confirm Customizer working tree clean on `main` (or current dev branch). `git status` shows no uncommitted changes you don't want to lose.
- [ ] Create feature branch: `git checkout -b cross-product/pageview-user-agent` (suggested name; matches Analyzer-side reference).

### File edits

- [ ] **`src/Customizer/Features/Visitors/Domain/Pageview.cs`** — add `string? UserAgent = null` as the 10th positional record parameter (slot 10, after `UtmCampaign`). Include an XML `<param name="UserAgent">` block citing the inter-product contract §6 item 2 (the new prerequisite). Mark explicitly: **in-memory only — NOT persisted by `PageviewDto`**. Final record shape:

      ```csharp
      public sealed record Pageview(
          Guid Key,
          Guid VisitorProfileKey,
          Guid ContentKey,
          PageviewSegmentSet Segments,
          bool WasContentTombstoned,
          DateTimeOffset RequestUtc,
          string? UtmSource = null,
          string? UtmMedium = null,
          string? UtmCampaign = null,
          string? UserAgent = null);
      ```

- [ ] **`src/Customizer/Middleware/PageviewCaptureMiddleware.cs`** — two changes:

  1. Before constructing the `Pageview`, capture UA: `var userAgent = ExtractUserAgent(context);` (next to the existing `var (utmSource, utmMedium, utmCampaign) = UtmExtractor.Extract(context.Request.Query);` line).
  2. Append `UserAgent: userAgent` to the `new Pageview(...)` constructor invocation as the last argument (after `UtmCampaign: utmCampaign`).
  3. Add a private helper at the bottom of the class (mirrors `PersonalizationResolutionFilter.ExtractUserAgent` at `src/Customizer/Features/Resolution/Pipeline/PersonalizationResolutionFilter.cs:88-96`):

      ```csharp
      /// <summary>
      /// Cross-product prerequisite for Analyzer slice 003 (inter-product
      /// contract §6 item 2). Returns the raw <c>User-Agent</c> request
      /// header — captured synchronously on the request thread so the
      /// fire-and-forget <c>PageviewCaptured</c> dispatch carries a
      /// usable UA even when subscribers run after the request scope is
      /// disposed. Null when the header is absent or empty.
      /// Mirrors <c>PersonalizationResolutionFilter.ExtractUserAgent</c>.
      /// </summary>
      internal static string? ExtractUserAgent(HttpContext context)
      {
          var uaHeader = context.Request.Headers.UserAgent;
          if (uaHeader.Count == 0)
          {
              return null;
          }
          var value = uaHeader.ToString();
          return string.IsNullOrEmpty(value) ? null : value;
      }
      ```

- [ ] **`src/Customizer/Features/Visitors/Persistence/PageviewDto.cs`** — `ToPageview(...)` constructor call gains `, UserAgent: null` as the trailing argument. The read-side reconstruction has no source for `UserAgent` (no request UA available off the persisted DTO). DO NOT add a column to `PageviewDto`. DO NOT add a migration. The UA is in-memory only.

- [ ] **`src/Customizer.Tests/Unit/Visitors/PageviewPositionalRecordAdditivityTests.cs`** — add two new tests mirroring the existing UTM tests:

      ```csharp
      [Fact]
      public void Nine_arg_ctor_defaults_UserAgent_to_null()
      {
          var pv = new Pageview(
              Key: Guid.NewGuid(),
              VisitorProfileKey: Guid.NewGuid(),
              ContentKey: Guid.NewGuid(),
              Segments: PageviewSegmentSet.Empty,
              WasContentTombstoned: false,
              RequestUtc: DateTimeOffset.UtcNow,
              UtmSource: "newsletter",
              UtmMedium: "email",
              UtmCampaign: "spring-launch");

          pv.UserAgent.Should().BeNull();
      }

      [Fact]
      public void Ten_arg_ctor_round_trips_UserAgent()
      {
          var pv = new Pageview(
              Key: Guid.NewGuid(),
              VisitorProfileKey: Guid.NewGuid(),
              ContentKey: Guid.NewGuid(),
              Segments: PageviewSegmentSet.Empty,
              WasContentTombstoned: false,
              RequestUtc: DateTimeOffset.UtcNow,
              UtmSource: null,
              UtmMedium: null,
              UtmCampaign: null,
              UserAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

          pv.UserAgent.Should().Be("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
      }
      ```

  Also: the class XML doc comment currently says "slice 007 (T024) — asserts the three new UTM positional record parameters". Update to mention the slice-007 UTM trio AND the cross-product slice's UserAgent addition both being covered.

### Optional but recommended

- [ ] **Middleware unit test** — extend `src/Customizer.Tests/Unit/Visitors/PageviewCaptureMiddlewareTests.cs` (or equivalent) with a single test: "Pageview captured carries the request User-Agent header." Construct a fake `HttpContext` with `Request.Headers.UserAgent = "TestUA/1.0"`; invoke the middleware; assert the queued `Pageview.UserAgent == "TestUA/1.0"`. Also assert null case: no UA header → `pv.UserAgent == null`.

- [ ] **PageviewCapturedNotifierTests** — if there's an existing test exercising the round-trip from middleware to notifier subscriber, extend it to assert UA round-trips through the notification. If no such test exists, skip.

### Build + verify

- [ ] `dotnet build` clean — 0 errors.
- [ ] `dotnet run --project src/Customizer.Tests/Customizer.Tests.csproj -- ...` (Customizer's CI invocation pattern; check Customizer's CLAUDE.md or recent commits for exact form) — all tests pass.
- [ ] **Pinning snapshot**: `src/Customizer.Tests/Snapshots/SegmentRulesPublicSurface.txt` should NOT change. `Customizer.Features.Visitors.Domain.Pageview` is not in the `PinnedNamespaces` list (`src/Customizer.Tests/Unit/SegmentRules/PublicSurfacePinningTests.cs:21-46`), so the change is invisible to the snapshot. The `PageviewCaptured` ctor line references `Pageview` by type-name only (line 893 of the snapshot), with no positional-shape detail. **Verify**: run `PublicSurfacePinningTests.SnapshotMatchesBaseline` — it should pass without regeneration. If it fails, investigate; you may have inadvertently added Pageview to a pinned namespace. Do NOT silently regenerate the snapshot to make it pass.

### Inter-product contract update

- [ ] **`../analyzer/docs/INTER-PRODUCT-CONTRACT.md`** — update §6 (Customizer-side prerequisites) to add item 2: "`Pageview.UserAgent` field carried on the `PageviewCaptured` notification." Reference the cross-product driver (Analyzer slice 003) and the Customizer commit that lands the change. Phrase as additive — the existing §6 item 1 (PageviewCaptured publish) remains unchanged.

  Alternatively: if the contract is more authoritatively maintained in the Customizer repo, mirror the §6 list update there.

### Commit + PR

- [ ] Commit on the `cross-product/pageview-user-agent` branch with a message like:

      ```
      cross-product: add UserAgent to Pageview record (Analyzer slice 003 prereq)

      Adds `string? UserAgent` as the 10th positional record param on
      `Pageview` (slot 10, default null). `PageviewCaptureMiddleware`
      captures `Request.Headers.UserAgent` on the request thread and
      threads it through to the `PageviewCaptured` notification.

      Request-only field — NOT persisted by `PageviewDto`. Analyzer
      slice 003 hashes UA to a truncated SHA-256 device-key before
      persistence.

      MINOR-additive per the slice-007 UTM trio precedent. Pinning
      snapshot unchanged (Pageview is not in the pinned namespace list).

      Satisfies inter-product contract §6 item 2 (added in same change).

      Co-Authored-By: [as appropriate]
      ```

- [ ] Push branch; open PR.
- [ ] Merge via `gh pr merge --rebase --delete-branch` (Customizer's org convention; matches Analyzer's PR merge pattern — preserves narrative).

### Coordination with Analyzer

- Once Customizer's PR is merged, capture the merged commit SHA — Analyzer's slice-003 plan + research + spec all reference it as the prerequisite anchor.
- Analyzer's slice 003 implementation depends on this commit being on Customizer's `main` AND the Analyzer-side `ProjectReference` resolving to the post-merge Customizer state.
- The Analyzer integration tests will fail until this PR is merged; the unit tests (which use a fake `notification.Pageview.UserAgent`) will pass against a stub.

---

## Why no schema / migration

`UserAgent` is an in-memory request-only field. Persisting it on `customizerPageview` would:

1. Introduce a Customizer-side privacy surface (UA strings sit at the boundary of PII in some jurisdictions).
2. Require a migration + back-fill story.
3. Provide no Analyzer-side value — Analyzer only ever hashes the raw UA into a truncated `deviceKey` before its own persistence, and that hash is the only durable form needed.

Keeping UA in-memory keeps Customizer's privacy posture neutral (its `CLAUDE.md` reads "Customizer makes no jurisdiction declaration… no PII collection beyond the EntraID identity"). Analyzer is the consumer that declares the US-jurisdiction posture; if a future Analyzer slice ever needs to expose UA on its public surface, that's a downstream design.

## What about the `Customizer.Features.Resolution.Application.Contracts.ResolutionRequest.UserAgent` field?

That's a different code path — `ResolutionRequest` is consumed by `PersonalizationResolutionFilter` for segment-rule evaluation against the live request, NOT for downstream notification subscribers. The two UA fields are conceptually parallel (both source from `Request.Headers.UserAgent`); the `Pageview.UserAgent` addition mirrors `ResolutionRequest.UserAgent`'s precedent at the domain-record level rather than re-using the request DTO.

## Time estimate

- 4 file edits + 2 unit tests + 1 optional middleware test: ~30 minutes of focused editing.
- Build + run tests + pinning snapshot verification: ~10 minutes.
- Inter-product contract update: ~10 minutes.
- PR + merge: ~10 minutes.

**Total: ~1 hour.**

## When you're done

Capture the merged commit SHA and post it back to the Analyzer slice-003 session (or update `.remember/remember.md` with the SHA). Analyzer's slice-003 artifacts (spec, research, plan, contracts, tasks) all need a single-pointer update from "TBD prereq SHA" to the actual SHA before slice 003 can proceed to `/speckit-implement`.

## ✅ Status (2026-05-18)

**SHIPPED at Customizer `5273c38` on `main`.** All 5 files match this TODO. Full suite passes (767/755 — 2 new additivity tests added). Analyzer slice-003 implementation can proceed.
