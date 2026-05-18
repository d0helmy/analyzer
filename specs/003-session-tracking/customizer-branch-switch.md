# Customizer branch-switch hand-off — for the Customizer agent

**Trigger**: Analyzer slice-003 implementation is blocked because Customizer's working tree is currently on `slice-010/t061-property-type-aware-editors` (no `Pageview.UserAgent`), while the cross-product UA addition is on Customizer's `main` at `5273c38` (handover `b785841`). Analyzer's `Customizer.csproj` `ProjectReference` compiles against working-tree files regardless of git history, so the build fails with:

```
error CS1061: 'Pageview' does not contain a definition for 'UserAgent'
  at ../analyzer/src/Analyzer/Features/Events/Application/PageviewCapturedHandler.cs:75
```

**Goal**: park Customizer on `main` so Analyzer can build, without losing slice-010 work-in-progress.

**Slice-010 WIP that MUST be preserved** (do NOT discard):
- 4 modified files under `src/Customizer/Controllers/DocumentTypeSegmentation/` — `ContentSegmentationPolicyController.cs`, `DocumentTypeSegmentationManagementController.cs`, `Models/EffectiveSegmentationPolicyResponse.cs`, `Models/PropertyOptInResponse.cs`
- 1 untracked file: `src/Customizer/Features/DocumentTypeSegmentation/Application/ContentTypePropertyEditorResolver.cs`

---

## TODO

### Step 1 — Preserve slice-010 work-in-progress

Pick the option that fits the current state of the slice-010 work:

**Option A — Stash (no commit; choose if the work isn't checkpoint-ready):**

```bash
cd /Users/dia/Documents/customizer
git status   # confirm the 4 modified + 1 untracked match the list above
git stash push -u -m "slice-010 t061 WIP — paused for cross-product Pageview.UserAgent rebase"
git status   # should now show clean working tree
```

**Option B — WIP commit (choose if the work is at a clean checkpoint):**

```bash
cd /Users/dia/Documents/customizer
git status   # confirm what's in flight
git add -A src/Customizer/Controllers/DocumentTypeSegmentation/ \
           src/Customizer/Features/DocumentTypeSegmentation/Application/ContentTypePropertyEditorResolver.cs
git commit -m "wip(slice-010): t061 property-type-aware editors — paused for cross-product UA rebase"
```

### Step 2 — Switch to a branch that has the UA addition

```bash
git checkout main
git log -1 --oneline
# Expected: b785841 HANDOVER ... or 5273c38 cross-product: add UserAgent ...

grep -c "UserAgent" src/Customizer/Features/Visitors/Domain/Pageview.cs
# Expected: ≥ 3  (one positional param at slot 10 + 2 doc-comment references)
```

If `grep` prints 0, the local `main` is behind `origin/main`:

```bash
git fetch && git pull --ff-only origin main
```

### Step 3 — Confirm back to the user

Reply with something concise like:

> "Customizer is on `main` at `b785841`. slice-010 WIP preserved via [stash | wip-commit]. Ready for Analyzer slice-003 build."

The user will then signal the Analyzer session to resume.

### Step 4 — When the Analyzer session signals "done with Customizer working tree"

(This signal comes after the Analyzer slice-003 PR has been opened or merged — typically minutes to hours later.)

Restore the slice-010 work:

```bash
git checkout slice-010/t061-property-type-aware-editors

# If Step 1 used Option A (stash):
git stash pop
# 4 modified files restored + 1 untracked file restored

# If Step 1 used Option B (WIP commit):
git reset --soft HEAD~1
# WIP commit unwound; files stay modified + untracked file stays untracked
# Continue slice-010 work normally; consolidate into proper commits when checkpoint-ready
```

---

## What NOT to do

- **Don't rebase or merge `main` into `slice-010/t061-...`.** The UA addition is unrelated to slice-010's scope; rebasing pollutes slice-010's eventual PR diff.
- **Don't discard the stash or the WIP commit.** Slice-010 is in-progress and load-bearing.
- **Don't push anything from `main` right now.** `main` already has `b785841` published; no further pushes for this hand-off.
- **Don't `git checkout -- .` the working tree** — that wipes slice-010 work.

---

## If something goes wrong

| Symptom | Likely cause | Fix |
|---|---|---|
| `git checkout main` fails: "your local changes would be overwritten" | Step 1 skipped or partial | Run Step 1 first; retry |
| `git stash pop` merge conflict after Step 4 | `main` diverged from slice-010's base | Resolve conflicts; expected only in the 4 segmentation files (UA addition is in `Visitors/Domain/Pageview.cs`, `Middleware/PageviewCaptureMiddleware.cs`, `Visitors/Persistence/PageviewDto.cs` — disjoint from slice-010 scope) |
| `grep -c UserAgent ... = 0` after `git checkout main` | Local `main` is behind `origin/main` | `git fetch && git pull --ff-only origin main` |
| `git fetch` fails (auth) | Customizer is private + token rotation needed | Standard `gh auth status`; refresh as needed |

---

## Background — why this matters

Analyzer's `src/Analyzer/Analyzer.csproj` has:

```xml
<ProjectReference Include="../../../customizer/src/Customizer/Customizer.csproj" />
```

MSBuild resolves this by reading whatever files are on disk at that path. Branch state is irrelevant; working-tree state is what gets compiled. This is fast (no NuGet round-trip) but cross-repo branch coordination must be explicit.

Once Customizer is NuGet-published (anticipated post-v1 per the inter-product contract), this whole class of branch-mismatch issue disappears — Analyzer pins a version, MSBuild restores from the package cache, Customizer's working tree is irrelevant to Analyzer's build. Until then, this hand-off pattern (or a similar workflow) is the cost of the ProjectReference shortcut.

---

## Cross-references

- Cross-product slice that landed the UA addition: customizer commit `5273c38` (one-paragraph context in `git show 5273c38`)
- Inter-product contract entry: `../analyzer/docs/INTER-PRODUCT-CONTRACT.md` §6 item 2
- Analyzer slice that consumes the UA: `../analyzer/specs/003-session-tracking/` (esp. `customizer-prereq.md` for the original cross-product spec)
- Lesson #40 (HttpContextAccessor unreliable under Task.Run) — captured in `../analyzer/.remember/remember.md`
