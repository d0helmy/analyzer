# Quickstart — Slice 002: Pageview Subscription

**Feature**: `002-pageview-subscription`
**Audience**: a developer (or future agent session) opening this slice to implement, review, or extend it.
**Prereq**: slice 001 already on `main` (`ab4285c`); Customizer's `PageviewCaptured` notification shipped at `05e989c`; Docker Desktop running for the Aspire SQL container.

This quickstart is the load-bearing "you have the spec + plan, now what" doc. It gives you the **shortest path** from a fresh clone to a green slice-002 implementation, with the gotchas that aren't obvious from reading the contracts.

---

## TL;DR

```bash
# 1. Prereqs
docker info >/dev/null                                  # Docker must be running
dotnet --version                                        # need .NET 10 SDK

# 2. Clone + branch
git clone git@github.com:d0helmy/analyzer.git
cd analyzer
git checkout 002-pageview-subscription                  # branch already pushed once slice's committed

# 3. Boot the dev SQL container (Aspire AppHost; persistent volume per slice-001 lesson #19)
dotnet run --project aspire/Analyzer.AppHost --launch-profile https &
# Container takes ~5 s on a warm volume, minutes on first pull.

# 4. Build + run all tests (slice 002 corpus + slice 001 corpus + pinning baseline)
dotnet build Analyzer.slnx
dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "Category!=Perf"

# 5. Perf-smoke (opt-in; CI-only by default)
dotnet test src/Analyzer.Tests/Analyzer.Tests.csproj --filter "Category=Perf"

# 6. Verify in browser
# Aspire dashboard URL printed at boot, e.g. https://localhost:17120/login?t=<token>
# Open https://localhost:44364/umbraco — default install creds for dev:
#   dev@analyzer.local / 1234567890aA!
# Render any front-end page as the dev user; query analyzerEventReceipt to confirm:
#   SELECT TOP 10 * FROM analyzerEventReceipt ORDER BY receivedUtc DESC;
# Should show one row per rendered page.
```

---

## Reading order (if you want to understand before changing anything)

1. **`spec.md`** — the why. Three user stories (P1 subscribe-and-record, P2 cascade-delete on anonymisation, P3 state provider + pinning), Clarifications Q1/Q2/Q3.
2. **`plan.md`** — the what + where. Technical Context, Constitution Check (all 10 gates pass), Project Structure (the tree of new files).
3. **`research.md`** — the why-this-way. Each design decision (queue pattern, cascade semantic, migration pattern, etc.) with alternatives + the Customizer source-file reference that grounds it.
4. **`data-model.md`** — concrete schema: columns, types, indexes, FKs, NPoco DTO shape, the immutable `AnalyticsEventReceipt` record.
5. **`contracts/*.md`** — what each new type does and how it's tested.
6. **`tasks.md`** (produced by `/speckit-tasks`, NOT this command) — the ordered task list.

---

## The five things that aren't obvious from the docs

### 1. The handler runs on a `Task.Run` thread, NOT the request thread.

Customizer's `PageviewCapturedNotifier.Notify` wraps the `IEventAggregator.PublishAsync` in a `_ = Task.Run(async () => …)`. Your handler's `HandleAsync` executes on a thread-pool thread *after* the request thread has typically already returned a response. Consequences:

- **`IHttpContextAccessor.HttpContext` may be `null` or disposed.** The opportunistic state-store update (`PageviewCapturedHandler.md` → `TryUpdateInFlightStateStore`) MUST swallow `ObjectDisposedException` and `InvalidOperationException`. Don't fight this — embrace it. `IAnalyticsEventStateProvider.CurrentRequestReceipt` is documented as typically-null on the pageview request itself.
- **The request scope's services may be unreachable.** Don't resolve scoped DI through `IHttpContextAccessor`; use the injected `IServiceScopeFactory` to create a fresh scope for any repository work.
- **You can't propagate exceptions back to the request thread.** They're already disconnected by `Task.Run`. Customizer's notifier swallows; your handler swallows. Defence in depth.

### 2. The unique-index-collision on duplicate dispatch is **expected**, not a bug.

Customizer's contract on `PageviewCaptured` explicitly says handlers SHOULD be idempotent. Duplicate dispatches happen in practice under unusual `Task.Run` scheduling pressure. The DB's unique index on `pageviewKey` is your enforcement mechanism. Catch `SqlException` with `Number == 2627 || Number == 2601` (SQL Server unique-violation) and log it at `LogDebug` — NOT warning, because in a healthy system it's still rare and we don't want operators waking up to grep for it.

### 3. The cascade step hard-DELETES; it does NOT re-key.

An earlier draft of the spec asserted "re-key, not delete" — that was wrong, it's been corrected, but if you read older notes around this slice you may see the older wording. **The actual pattern**: Customizer's `GoalReachedCascadeStep` deletes rows by visitor key inside the outer scope; Analyzer matches that precedent. The CCPA right-to-delete obligation favours hard-delete anyway. If you find yourself wanting to preserve aggregate counts via a soft-delete flag, **stop and read `research.md` §3** — that design has been considered and rejected.

### 4. Integration tests need a real SQL Server, NOT a SQLite seam.

Cascade-step rollback semantics under Customizer's `IScopeProvider.CreateScope()` outer scope are NOT faithfully reproduced on SQLite (NPoco's nested-scope-enlistment behaviour differs across providers). SC-006 makes this explicit; the test base falls back to `Testcontainers.MsSql` if the Aspire container isn't running, but the local-dev path is "just run the AppHost in another terminal." The AppHost's persistent volume means first-run pulls the MSSQL image once (~1.5 GB, minutes); subsequent runs are seconds.

### 5. The pinning baseline must be regenerated **deliberately**, never silently.

`PublicSurfacePinningTests.SnapshotMatchesBaseline` fails any time `IAnalyticsEventStateProvider` / `IVisitorIdentifier` / `BaseVisitorIdentifier` change shape. The fix path is:

- **Intentional change**: regenerate the baseline file (run `dotnet test --filter "PublicSurfacePinningTests" --logger "trx;regen"` or whatever the test exposes); add a `Sync Impact`-style note to the slice spec or release notes; commit baseline + spec amendment together.
- **Unintentional change**: revert the change. The pinning test is doing its job.

Never `git checkout` the baseline without also reverting the change that caused the diff.

---

## File-creation order (suggested for implementation)

Driven by dependency direction; avoids "can't build yet because X depends on Y that doesn't exist." If you follow `tasks.md` (from `/speckit-tasks`) the ordering is already locked; this list is just the conceptual progression.

1. **Constants + Configuration**
   - `Constants.Database.AnalyzerEventReceipt`
   - `AnalyzerWriteQueueOptions`
2. **Domain + DTO**
   - `Analyzer.Features.Events.Domain.AnalyticsEventReceipt`
   - `Analyzer.Features.Events.Infrastructure.Persistence.AnalyzerEventReceiptDto`
3. **Repository**
   - `IAnalyzerEventReceiptRepository`
   - `AnalyzerEventReceiptRepository` (insert + delete-by-visitor)
4. **Queue + dispatcher**
   - `AnalyzerEventReceiptWriteOp`
   - `AnalyzerEventReceiptWriteQueue`
   - `AnalyzerEventReceiptWriteDispatcher`
5. **State store + provider**
   - `AnalyticsEventStateStore` (internal)
   - `IAnalyticsEventStateProvider` (public) + `AnalyticsEventStateProvider` (internal impl)
6. **Handler**
   - `PageviewCapturedHandler`
7. **Cascade step**
   - `AnalyzerEventReceiptCascadeStep`
8. **Migration + plan + schema composer**
   - `M0001_AddAnalyzerEventReceiptTable`
   - `AnalyzerMigrationPlan`
   - `AnalyzerSchemaComposer`
9. **Composer wiring**
   - Extend `AnalyzerComposer.Compose` to register: handler, queue, dispatcher hosted service, state store, state provider, cascade step, options.
10. **Tests** (each phase tests the just-built layer)
    - Unit tests as you go (`PageviewCapturedHandlerTests`, `AnalyzerEventReceiptCascadeStepTests`, etc.)
    - Integration tests after the composer is wired (`EndToEndCaptureTests`, `CascadeDeleteTests`, etc.)
    - Pinning test + baseline last (so all public types exist)
    - Perf smoke test last (opt-in trait)

---

## Verifying you're done

| Check | How |
|---|---|
| All FRs covered | Cross-reference `spec.md` FR-001..FR-012 against `tasks.md` — every FR has at least one task. |
| All SCs measurable | SC-001/004 via integration tests, SC-002 via the perf-smoke test, SC-003 via the cascade integration test, SC-005 via the pinning test, SC-006 across the test corpus, SC-007 by inspection (does slice 003 have setup work? — answer must be "no"). |
| Constitution gates green | `plan.md` Constitution Check section — all 10 PASS, no Complexity Tracking entries. |
| Slice-002 spec corrections landed | The clarifications session in `spec.md` reflects the corrected cascade-step semantic (hard delete, not re-key). |
| Local dev verified | Open `https://localhost:44364/umbraco`, render any page as the dev user, query `analyzerEventReceipt` — exactly one new row appears within a second. |
| Anonymisation flow verified | Invoke Customizer's anonymise-visitor command (via the backoffice or a SQL-driven test seed), then re-query `analyzerEventReceipt WHERE visitorProfileKey = <key>` — should return 0 rows. |

---

## When something goes wrong

| Symptom | Likely cause | Fix |
|---|---|---|
| `NU1102: Unable to find package Umbraco.Cms.Web.Backoffice` | Reading older notes; that package doesn't exist for 17.x. | Use `Umbraco.Cms.Web.Common` (slice 001 lesson #1). |
| `NU1605: detected package downgrade Microsoft.Extensions.Logging.Abstractions` | Explicit pin below Umbraco's transitive `10.0.4`. | Remove the explicit pin (slice 001 lesson #2). |
| `NETSDK1022: Duplicate Content items` | Explicit `<Content Include="wwwroot/App_Plugins/Analyzer/**/*">` after bundle build. | Remove the explicit Content item; the Razor SDK auto-includes wwwroot (slice 001 lesson #3). |
| Backoffice bundle 404 even though build succeeds | Missing `<StaticWebAssetBasePath>/</StaticWebAssetBasePath>` in `Analyzer.csproj`. | Already fixed at `ab4285c`; don't reintroduce (slice 001 lesson #15). |
| `OptionsValidationException: Failed to configure dashboard resource because ASPNETCORE_URLS environment variable was not set` | Bare-CLI `dotnet run` of Aspire AppHost. | Use the `--launch-profile https` profile defined in `aspire/Analyzer.AppHost/Properties/launchSettings.json` (slice 001 lesson #16). |
| Port still in use after killing the AppHost | `dcp` proxy survived. | `pkill -9 -f "Analyzer\.AppHost\|Analyzer\.Host\|aspire/Analyzer\|samples/Analyzer\|/dcp "` (slice 001 lesson #17). |
| `Umbraco:CMS:Global:Id` GUID shows up in `git diff` | First-boot install writes the GUID. | DO NOT commit. Stash or set in `appsettings.Development.json` (slice 001 lesson #18). |
| Pinning test diffs unexpectedly | Public surface accidentally changed. | Read the diff carefully; revert the change OR regen the baseline + spec amendment, never both blindly. |
| Receipts not appearing in `analyzerEventReceipt` despite browsing | Dispatcher hosted service not registered OR queue full silently. | `dotnet run` log shows `"Visitor write dispatcher started"`-equivalent for Analyzer; if missing, check `AnalyzerComposer` for `AddHostedService<AnalyzerEventReceiptWriteDispatcher>()`. |
| Integration test deadlocks | Sync-over-async; missing `await`. | `AsyncMigrationBase` is the right base; sync `MigrationBase` deadlocks under certain `SynchronizationContext`s (`research.md` §4). |
| Cascade-step throws under integration test, but no rollback | Customizer's `AnonymizeVisitorProfileHandler` wasn't actually invoked — your test bypassed the orchestrator. | Use `AnonymizeVisitorProfileCommand` via the command bus, not the repository directly. |

---

## Cross-product hygiene

- **Do not modify any file under `../customizer/`** while implementing this slice. Principle III is strict. Customizer's slice-011 (`PageviewCaptured`) is the only prerequisite, and it already shipped at `05e989c`.
- **Do not import Customizer internals.** The pinned surface is what you can import: `IPersonalizationProfile`, `IAnalyticsStateProvider` (the Customizer one), `PageviewCaptured`, `Pageview`, `IAnonymizationCascadeStep`, `IScopeProvider`. Anything in `Customizer.Features.*.Infrastructure.*` is internal — don't import it; use raw SQL or NPoco grammar in your migration instead (`data-model.md` §1 pinned decision).
- **Update `remember.md`** at end-of-session with anything you learned. The handoff doc in `.remember/` is how the next session gets the load-bearing context.

---

## When in doubt

Read Customizer's `VisitorAnalyticsComposer.cs`. Every pattern this slice uses (queue, dispatcher, hosted service, scoped contracts, migration plan composition) is already there in production-validated form. Symmetry between the two products is itself a value — diverge only when you have a written justification.
