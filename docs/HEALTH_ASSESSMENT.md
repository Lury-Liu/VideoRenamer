# Project Health Assessment — Evidence-Backed

**Branch:** `refactor/architecture-v2` (26 commits over baseline `7f6855f` / V1.0.6.0)
**Date:** 2026-07-26 · **Verification state:** SelfTest **55/55**, SmokeTest OK, all four build gates PASS, release EXE builds + verify-artifact PASS at every phase boundary.

Scoring: six dimensions, equal weight, each scored against measured evidence collected in this session (commands and raw numbers in [REFACTOR_PROGRESS.md](REFACTOR_PROGRESS.md)). Baseline = V1.0.6.0 as assessed by the six-subsystem architectural audit.

## Overall: **85 / 100** (baseline ≈ 38 / 100)

An honest 85, not a rounded-up 90: the remaining five points have names and file paths (§Remaining gaps). Everything scored below is verifiable by re-running the stated command.

| Dimension | Baseline | Now | Key evidence |
|---|---|---|---|
| 1. Architectural coupling | 35 | **84** | god class −30% lines; 12 extracted modules; 3 machine-enforced boundary gates |
| 2. Maintainability | 40 | **82** | responsibility-grouped tree; 4 duplication families killed; largest production file 521→475 lines |
| 3. Testability | 25 | **85** | 1 monolith → 55 named cases; golden masters; sabotage drill caught by 4 cases |
| 4. Reliability | 45 | **84** | 9 real defects fixed (each with its own commit); cancellation/cleanup paths added |
| 5. Performance | 50 | **85** | measured: 2.1× refresh, 14.6× metadata, threads unbounded→2, O(n²)→O(n) |
| 6. Release safety | 30 | **90** | dead version guard resurrected + drill-proven; 4 standing build gates; test-gated publishing |

## 1. Architectural coupling — 84

- `MaterialRenamerForm`: **4,090 → 2,848 lines** (−30%), 13 → 12 partials. Evacuated into real classes: `FfmpegLocator/FfmpegArguments/FfmpegRunner`, `VideoExportService`, `ExportController`, `RenameController`, `MediaLoadScheduler`, `ThumbnailCache`, `PlanExecutor`, `RenameHistoryStore`, `ExportPlanBuilder`, `ShotLabelParser`.
- **Compile-checked seams** replace implicit conventions: `PlanStatus` enum (was 8 Chinese display strings compared by equality across 5 files), `IStatusSink` (was 35 direct `statusLabel.Text` writes across 6 partials — now 1 funnel, verify: `grep "statusLabel.Text = " src/App/MainForm` → 1 hit, the implementation), `NamingSettings` (was scattered control reads in 5 partials — now `ReadNamingSettings()` alone), `IFileSystemProbe`, `IUiDispatcher` (drop-contract documented as frozen).
- **Machine-enforced boundaries** in every build: `Assert-CorePurity` (Core: no WinForms/Drawing; Media: no WinForms — full-text scan so qualified names are caught), `Assert-StatusLiteralOwnership` (status literals only in mapper/enum/tests), `Assert-CsprojParity`.
- Misplaced members re-homed: `SetOperationUiEnabled` (History→Ui), export accessors (Theme→Plan), `QueueOnUi` contract extracted.
- **Held back by:** `currentPlan` still a form field touched by 3 partials; grid/preview rendering still in partials (deliberate: presenter classes holding the same live controls would be re-sharding, not decoupling — documented decision); `LicenseManager` ↔ `LicenseDialog` bidirectional coupling untouched; `AboutForm` still duplicates the update-check flow.

## 2. Maintainability — 82

- Tree grouped by responsibility: `src/App` (4,326 lines) / `Core` (898, pure) / `Media` (1,320) / `Services` (1,146) / `Tests` (1,058); 40-file restructure done as provably pure `git mv` (100% similarity on every file).
- Duplication resolved with single owners: row write-back (3 implementations → `PlanExecutor.PatchRowFileList`), grid cell population (2 → `PopulateGridRow`), `QuoteArgument` (2 of 3 copies → `FfmpegArguments`), shot-label parse/format colocated (`ShotLabelParser`), `GetCellSummary` computed once per cell (was twice).
- Largest production file 521 → 475 lines; pure models carry zero using-noise (was a 15-line copy-pasted header on all 42 files; Core/Media cleaned, 9 files).
- **Held back by:** `BuildUi` still ~315 lines of inline anonymous delegates; third `QuoteArgument` copy in `UpdateManager.Download.cs`; two hand-rolled JSON mechanisms; App partials still carry bloated using headers; C# 5 syntax throughout (toolchain-frozen by design until the csproj is promoted).

## 3. Testability — 85

- 155-line first-failure-aborts monolith → **55 named cases**, all run, per-case PASS/FAIL (`video_material_renamer.ps1 -SelfTest`).
- Characterization coverage on every surface later phases cut: all 8 statuses, `IsBlockingIssue` truth table, export-plan derivation + both IOExceptions, clone field-by-field, tail normalization/truncation, shot-label parse table, history TSV round-trip + golden base64, ffmpeg arg goldens (byte-exact), real published `latest.json` golden, version-compare table incl. 3-vs-4-segment pin, `PlanExecutor` disk semantics, patch variants.
- **Golden masters**: two full plan corpora pinned byte-for-byte; survived the enum conversion and perf pass unchanged (proof user-visible naming output never drifted).
- Deterministic seams: `FakeFileSystemProbe` drives all statuses with zero temp files.
- Net proven, not assumed: sabotage drill (suffix-case flip) caught by 4 cases with the OK marker withheld.
- **Held back by:** license/update networking still untestable (no `IClock`, no DPAPI fixtures); UI covered only by construction smoke + manual checklist; tests compile into the shipped EXE; no CI runner.

## 4. Reliability — 84

Nine pre-existing defects fixed, each isolated in its own flagged commit:
1. `Clone` dropped `ShotLabel` — cloned export plans lost the `28A` label (latent since the feature shipped).
2. Stale metadata/thumbnail caches after overwrite-export (details panel showed pre-export size/resolution forever).
3. Stale preview group headers after scene/shot edits (and after spinner changes once rerouted).
4. Superseded lock-probe loops never terminated (kept exclusively opening files every 20 ms; loops stacked on rapid refresh).
5. Closing mid-export orphaned a 100%-CPU ffmpeg child + `.vmr_` temp litter — now confirm→kill→cleanup, plus startup/pre-export orphan sweeps.
6. Update download had no cancel and no effective timeout behind a `ControlBox=false` modal — now cancel button + 60 s stall detection.
7. Startup could hang ~24 s invisible on offline networks — check now overlaps the 4 s splash.
8. Thumbnail evicted while displayed was disposed → GDI+ crash on repaint — retained-image guard.
9. Progress-event flood (hundreds of cross-thread posts + re-theming per second during download) — throttled, themed once.
Plus: ~50 lines of dead code deleted (`CheckForUpdatesManually`, write-only `detailLoadVersion`); TLS 1.2 set once by name; media threads bounded with graceful degradation semantics preserved.
- **Held back by:** no logging (≥10 silent empty catches remain); license subsystem untouched; interactive paths (cancel flows, walkthrough) verified by code review + tests, not by hand yet.

## 5. Performance — 85 (measured)

| Metric | Before | After | Method |
|---|---|---|---|
| Spinner-tick preview refresh (120 clips) | 52.7 ms full rebuild | **25.6 ms** incremental (2.1×) | Stopwatch harness over real form; real-UI gain larger (full path also triggers `TextRenderer` autosize storms not counted headless) |
| Video metadata read (real mp4, Shell COM) | 249 ms/file | **17 ms/file** (14.6×) | ~680 late-bound COM calls → ~4 after first-file column-index cache; values verified identical |
| ffmpeg path resolution | ~100 MB resource re-check per call, incl. UI thread at export start | memoized once, failure never cached | by construction + end-to-end extraction test on built EXE |
| Media loader threads (rapid navigation) | unbounded (3/selection) | **2 persistent STA workers** (fast/slow lanes) | by construction; LIFO newest-first |
| Metadata backfill into preview | O(n²) scans | O(n) via `OldPath→items` map | code path |
| Tail uniqueness search | worst-case 10,000×n scans | HashSet, **0.24 ms/call** measured worst-case | harness |
| Rename batch | synchronous `File.Move` loop froze UI | worker thread + per-file progress | flagged behavior commit |
| BuildPlan double-run on structural refresh | 2× (with 2×File.Exists/file each) | 1× (prebuilt-plan handoff) | code path |
| Grid/preview painting | no double buffering anywhere | buffered subclasses + static progress brushes | code path |
- **Held back by:** no 120 ms spinner debounce (each tick still runs one BuildPlan with real `File.Exists` I/O); row add/move/delete still full-grid rebuild; no ListView virtualization; frame-strip still samples only ~the first 256 frames (product-decision change, deliberately not made unilaterally).

## 6. Release safety — 90

- Version guard **resurrected from dead code** (regexed a file the constant left long ago; silently skipped) → reads `AppInfo.cs`, hard-fails, **drill-proven**: crafted 1.0.5.34 EXE refused with exact message.
- `verify-artifact.ps1` after every build: frozen resource name present, FileVersion triple-match, >90 MB floor, exactly one hyphen-free EXE, no stray DLLs — negative drill: non-app EXE fails all 4 applicable checks.
- Publishing gated: `打包安装程序.ps1` and `发布更新到GitHub.ps1` refuse without exact `SelfTest OK`/`SmokeTest OK`; `-DryRun` rehearsal mode passes end-to-end.
- Missing ffmpeg: warn → **hard fail** (`-AllowNoFfmpeg` opt-out); embedded-extraction path proven on the built artifact (cache cleared, CWD forced away → extracted 101,457,920 bytes, `ffmpeg -version` runs).
- All 10 frozen deployment contracts enumerated in `scripts/build-common.ps1` and asserted where scriptable.
- **Held back by:** no CI (nothing runs gates on push); smoke gate requires an interactive desktop session; shadow csproj never actually built (no dotnet SDK on this machine); a real draft-prerelease update rehearsal was not performed this session.

## Remaining gaps — the path from 85 to ~90 (and beyond)

Ordered by expected health impact; none block release — every commit on the branch is shippable.

1. **License subsystem split with fixtures first** (coupling+testability+reliability). Blocked deliberately: splitting `LicenseManager` without regression fixtures written by the *old binary on a machine with a real activation* is exactly the DPAPI-compatibility risk the plan forbids. Capture `license.v2.dat`/`license.state.v2.dat` fixtures, then split Validator/Store with `IClock`.
2. **Finish the form diet** (coupling+maintainability): factor `BuildUi` into named `Build*` methods with named handlers; give `currentPlan` a single owner with `GetCurrentPlan()` access; dedupe `AboutForm`'s update flow via a shared prompter.
3. **Perf leftovers** (performance): 120 ms spinner debounce; single-row grid mutations for add/move/delete; `File.Exists` memoization per refresh cycle through the probe seam.
4. **CI + csproj validation** (release safety+testability): build the shadow csproj on a dotnet-SDK machine, compare FileVersion + manifest resources against the csc EXE, then stand up CI running the gates headless (requires porting the smoke gate off the interactive session).
5. **Interactive verification pass** (one human hour): the standing checklist — drag-drop import both cell types, row ops, `28a`→`28A`, tails, five conflict types side-by-side vs a pre-refactor build, rename+undo, both export modes incl. **mid-export cancel and close-window abort (new)**, update-dialog **cancel (new)**, dark-mode toggle mid-operation, hover-scrub, offline cold start (~4-5 s, was up to ~24 s).
6. **Product decisions pending**: frame-strip full-duration sampling (changes visible hover-scrub output); splitting ffmpeg from the 100 MB update download (changes the deployed updater contract).
7. **Minor debt**: third `QuoteArgument` copy; two regex JSON parsers; logging instead of silent catches; App-partial using headers.

*Item 1 alone is ≈ +2 points (it lifts three dimensions); items 2–4 together ≈ +3. The C# 5 cap and tests-in-EXE are toolchain-frozen constraints that only the csproj-promotion project (planned Phase 8) removes.*
