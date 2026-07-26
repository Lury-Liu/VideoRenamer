# Architecture Assessment & Redesign Plan

**Project:** 视频素材镜头表命名工具 (Video Material Shot-List Renamer) · V1.0.6.0
**Date:** 2026-07-26
**Scope:** Full architectural assessment + phased refactoring plan. Goals, in priority order: (1) reduce coupling, (2) reorganize structure, (3) improve maintainability, (4) improve performance — while preserving all existing functionality with zero regressions.

> **Status (2026-07-26): COMPLETED.** Executed as `refactor/architecture-v2` (27 commits, health 38→85/100), then extended by the V3 modernization (`refactor/modernization-v3`, 89/100, source now V1.0.7.0). This document is the historical plan — see [REFACTOR_PROGRESS.md](REFACTOR_PROGRESS.md) for verification evidence and [HEALTH_ASSESSMENT.md](HEALTH_ASSESSMENT.md) for the current state.

**How this plan was produced:** six parallel subsystem assessments (main form, domain logic, media pipeline, services/startup, UI rendering, build/release), three independently designed candidate architectures (conservative in-place, toolchain-first modernization, gated strangler-fig), and a three-lens adversarial judge panel (regression safety, decoupling/maintainability, performance/correctness). The winning plan below is the strangler-fig migration (won 2 of 3 lenses: regression safety 9/10, performance correctness 8/10) with mandatory transplants from the other two, and the toolchain modernization retained as the explicit follow-on phase (it won the end-state maintainability lens 8/10).

---

## Part 1 — Architectural Assessment

### 1.1 What exists today

~7,800 lines of C# 5 across 42 files under `src/`, compiled as one WinForms EXE by feeding the entire `src/` glob to the in-box legacy compiler (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) via `构建EXE.ps1`. There is no `.csproj`/`.sln`, no NuGet, no test framework. The dev loop (`video_material_renamer.ps1`) compiles the same glob in-memory via `Add-Type` under PowerShell 5.1. ffmpeg.exe (~100 MB, git-ignored) is embedded as a resource at build time and extracted at runtime by the exact name `VideoMaterialRenamer.ffmpeg.exe`.

The layering is partially real: `Plan/RenamePlanBuilder.cs` (naming engine), `Models/`, `Media/` providers, `Services/` (license/update/disclaimer), and `Forms/` dialogs are separate files. But the center of the application is a single god class.

### 1.2 The core problem: MaterialRenamerForm is one class, not thirteen modules

`MaterialRenamerForm` is ~4,090 lines split across 13 partial-class files that all share ~35 instance fields declared in `MaterialRenamerForm.Core.cs:35-82`. The partials are slices of one state machine, not modules — the hottest fields act as an implicit global bus:

| Shared field | Touched by | Notes |
|---|---|---|
| `statusLabel` | **8 of 13 partials** | ~30 direct `.Text` writes with hardcoded Chinese strings |
| `rows` (List\<ShotRow\>) | 7 partials | mutated by Grid, Rows, History, read everywhere |
| `darkMode` | 7 partials | theme state consulted ad hoc |
| `grid` control | 5 partials | built in Ui.cs, mutated in Grid/Rows/Details |
| `previewList` | 4 partials | doubles as the data structure (plans stored in `ListViewItem.Tag`) |
| `currentPlan` | 3 partials | mutated from a background thread (Preview.cs:372-378) |

Symptomatic couplings:

- **`RenameFiles` (Rename.cs:22-171) is a cross-partial orchestrator god-method**: one click handler reaches into Preview, Theme, Export, Ffmpeg, and History internals — validation, three confirmation dialogs, ffmpeg discovery, a synchronous `File.Move` loop **on the UI thread**, undo push, and history save in one method.
- **Misplaced members create hidden seams**: the UI-marshaling primitive `QueueOnUi` lives in the *Media* partial (Media.cs:22) but is used by Preview and Export; the whole-form lockout `SetOperationUiEnabled` lives in *History* (History.cs:185) but is called by Export; export settings accessors live in *Theme* (Theme.cs:29-37) but are consumed by Preview, Rename, and Ui.
- **`BuildUi` (Ui.cs:22-336) wires ~25 event handlers as inline anonymous delegates** spanning 8 partials — handlers can't be unhooked, named, or tested.
- **Self-tests are welded to private internals**: `RunSelfTest`/`RunSmokeTest` (Core.cs:103-294) live on the form and assert private controls (`btnAbout`, grid column colors), so any extraction strands the only regression net.
- Some partials are already service-shaped and cheap to free: `Ffmpeg.cs` is 406 lines of **100% static** code touching zero instance fields; History persistence is nearly standalone.

### 1.3 Domain layer: good engine, leaky boundary

`RenamePlanBuilder` (421 lines, static) is genuinely UI-free in behavior but not pure, and the boundary leaks in both directions:

- **The plan status is a stringly-typed protocol of Chinese display text** (`"就绪"`, `"目标已存在"`, `"新文件名重复"`, `"源文件缺失"`, `"未变化"`, `"待覆盖导出1080p"`). `IsBlockingIssue` does string equality (RenamePlanBuilder.cs:125-132); one status (`"目标文件被占用"`) is never produced by the builder at all — it is injected by the form from a background thread (Preview.cs:372-378). A typo or missed comparison site fails silently, not at compile time.
- **Not pure**: `BuildPlan` performs `File.Exists` per file, `IsFileLocked` opens files exclusively, `GetUniquePathWithSuffix` probes the disk in a loop, and `BuildPlan` mutates its input rows (resizes tail-override lists — a de facto contract callers rely on).
- **Reference-identity aliasing is load-bearing**: `RenamePlan.Row` points at the *live* `ShotRow`; undo, export progress dictionaries (keyed by `ShotRow` reference), and preview reselection all depend on it. Any refactor that clones or snapshots rows silently breaks progress mapping and undo.
- **A second, parallel plan builder lives in the UI**: `PrepareExportPlan` + a hand-rolled `CloneRenamePlan` (Export.cs:22-48) that **silently drops the `ShotLabel` field** — a real latent bug: cloned plans lose the `28A` suffix.
- The shot-label *parse* regex lives in the UI (Grid.cs:442) while its inverse *format* lives in the builder — the pair can drift apart.
- Test coverage gap: `RunSelfTest` covers happy-path naming but **none of the five blocking statuses, `IsBlockingIssue`, `PrepareExportPlan`, plan execution, or undo** — exactly the surfaces a refactor would touch.

### 1.4 Media pipeline: correct providers, dangerous orchestration

The three providers (`VideoMetadataReader`, `VideoThumbnailProvider`, `VideoFrameStripProvider`) are UI-free, but all orchestration lives in form partials:

- **Thread-per-request**: every detail-panel selection spawns up to three brand-new STA threads plus one ffmpeg.exe process — no pool, no queue, no coalescing. Rapid arrow-key navigation stacks dozens of threads and concurrent decodes. (The STA requirement is load-bearing: Shell COM silently degrades on MTA.)
- **`ExtractEmbeddedFfmpeg` runs unconditionally inside every `FindFfmpegPath` call**, never memoized — including on the UI thread at export start (Rename.cs:59): a multi-second freeze re-stat-ing a ~100 MB resource.
- **No cancellation anywhere**: in-flight frame-strip ffmpeg processes always run to completion for garbage results; 1080p export has no cancel button and **no FormClosing handler** — closing the app mid-batch orphans a 100%-CPU ffmpeg child and leaves `.vmr_*` temp files.
- **Caches are never invalidated on content change**: after an overwrite-mode 1080p export, the preview shows the pre-export size/resolution/thumbnail for that path indefinitely.
- **The hover-scrub strip doesn't sample the whole video**: `thumbnail=n=16` + `-frames:v 16` covers roughly the first 256 frames (~8-10 s), contradicting the code's own 均匀分布 comment.
- Duplication: `QuoteArgument` exists character-for-character twice (Ffmpeg.cs:113-116 vs VideoFrameStripProvider.cs:13-16), plus duplicated arg prefixes and `ProcessStartInfo` setup.

### 1.5 Services & startup: static singletons fused to MessageBox

- `LicenseManager` (390 lines) fuses six responsibilities: RSA verification, key parsing, machine fingerprinting, DPAPI persistence, expiry/clock-rollback state, and modal dialog flow — with **bidirectional coupling** (it shows `LicenseDialog`, which calls back into `LicenseManager.TryActivate`). Nothing is testable without a desktop session.
- `UpdateManager` (747 lines across two partials) owns UI directly (constructs the progress form, detects dark mode, MessageBoxes every outcome). `CheckForUpdatesManually` (~50 lines) is **dead code** duplicated almost verbatim by `AboutForm`. JSON is parsed by two different hand-rolled regex mechanisms.
- **Startup blocks the UI thread on network I/O**: the gate chain (disclaimer → license → 4-second modal splash → synchronous update check) can stall a cold offline launch **15-24 s** before any window appears (6 s + 2×9 s sequential timeouts), *after* a guaranteed serial 4-second splash.
- The async download has no effective timeout and no cancel behind a `ControlBox=false` modal — a stalled connection traps the user.
- Zero logging; ≥10 silent empty catch blocks.

### 1.6 UI rendering: incremental refresh exists but is barely wired

- The cheap paths exist (`RenderGridRow`, `RefreshPreviewNamesOnly`/`StatusOnly` from the V1.0.6.0 perf work) but are wired to **one call site**. Episode/scene spinners, all checkboxes, theme toggle, and every row operation still trigger **full rebuilds** — `Items.Clear()` + reallocation + a `TextRenderer.MeasureText` autosize storm (~960 measure calls), plus `RefreshPreviewNamesOnly`'s fallback **builds the plan twice** (Preview.cs:265→35), each build doing per-file `File.Exists` on the UI thread.
- **No double buffering anywhere** (zero grep hits) — visible flicker during export progress repaints.
- The preview `ListView` **is** the data structure (plans in `Item.Tag`, linear scans to find entries → O(n²) during metadata backfill).
- Grid row population duplicated verbatim (Grid.cs:132-144 vs 165-177); per-cell style allocation defeats WinForms style sharing; progress cell allocates two brushes + a pen per paint.
- Theming dispatches on magic `Tag` strings (`"Muted"`/`"Primary"`) via a recursive control walk.

### 1.7 Build & release toolchain: self-contained but ungated

- The **release script's version guard is dead code**: `Get-SourceVersion` regexes `video_material_renamer.ps1` for a constant that moved to `src/AppInfo.cs` during modularization — it silently returns `""` and skips the check (发布更新到GitHub.ps1:141-154, 180-183). A stale EXE can be published.
- **No script ever runs the self-tests** — gating is a README convention.
- `构建EXE.ps1` only **warns** when ffmpeg.exe is missing, and extraction failure at runtime returns `""` silently — a media-crippled EXE can ship with no error at any stage.
- Version lives in four unsynchronized places (AppInfo.cs, AssemblyInfo.cs, installer.iss fallback, README) with no cross-check.
- The source glob + reference list are copy-pasted between build and dev loader; the latest.json schema exists in three places with no shared source.

### 1.8 Frozen contracts (must never break — verified against deployed reality)

Any refactor must treat these as **read-only**; violating any one bricks updates, media, or activations for every installed copy:

1. Embedded resource named exactly `VideoMaterialRenamer.ffmpeg.exe` (构建EXE.ps1:63 ↔ Ffmpeg.cs:69).
2. Single loose EXE `dist\视频素材镜头表命名工具.exe` — installer.iss:45-50 and the publish script's "newest hyphen-free EXE" heuristic both assume it.
3. Auto-update artifact contract: tag `v{FileVersion}`, assets `VideoRenamer-v{version}.exe` + literal `latest.json`, manifest field names, appId guard, lowercase sha256; primary URL `releases/latest/download/latest.json` baked into shipped binaries.
4. `FileVersion` sourced from `src/AssemblyInfo.cs` (drives tag/asset/manifest derivation in the publish script).
5. License persistence: `GetMachineCode` algorithm, DPAPI entropy derivation, `LicenseStateV2`/`RSA-SHA256` markers, `license.v2.dat`/`license.state.v2.dat` under `%LocalAppData%\VideoMaterialRenamer`.
6. Installer AppId GUID and install dir; uninstall preserves `%LocalAppData%` state.
7. Gate order disclaimer → license (legal), and the update-restart early-return in `Program.Run`.
8. Loader switches `-SelfTest`/`-SmokeTest` printing exactly `SelfTest OK`/`SmokeTest OK`.
9. Runtime ffmpeg override order: `baseDir → baseDir\tools → cwd → cwd\tools → PATH → embedded` (the shipped error dialog tells users to drop ffmpeg.exe next to the EXE).
10. Encoding: `.ps1`/`.iss` with Chinese = UTF-8 BOM; `.cs` = UTF-8 no BOM.

### 1.9 Performance bottleneck inventory (verified, with fixes)

| # | Bottleneck | Impact | Fix (phase) |
|---|---|---|---|
| P1 | Unconditional ~100 MB ffmpeg extraction check per `FindFfmpegPath` call, incl. UI thread at export start | High | Memoize `FfmpegLocator.Resolve()` (Phase 2) |
| P2 | Synchronous `File.Move` rename loop on the UI thread | High | `PlanExecutor` on worker thread (Phase 5c) |
| P3 | Full preview rebuild + **double** `BuildPlan` + per-file `File.Exists` on every spinner tick / checkbox / theme toggle | High | Route through names-only diff path + debounce + pass prebuilt plan (early win in Phase 3; completed Phase 7) |
| P4 | Thread-per-request STA media loading (dozens of threads on rapid navigation) | High | Single STA worker + most-recent-first queue (Phase 5a) |
| P5 | Startup: serial 4 s splash + up to ~20 s synchronous update check on UI thread | High | Overlap check with splash on background thread (Phase 6) |
| P6 | No cancellation: frame-strip and export ffmpeg processes never killed; no FormClosing guard | High | Track + `Kill()` processes; cancel button; FormClosing cleanup (Phases 5a/5b) |
| P7 | Stale caches after overwrite-export (correctness bug) | Med | Evict on completion + `Length`+`LastWriteTime` fingerprint (Phase 7) |
| P8 | O(n²) metadata backfill (linear ListView scans); O(10000×n) `GetUniqueCustomTail` | Med | `OldPath→item` dictionary; precomputed HashSet (Phases 5e/7) |
| P9 | No double buffering; per-paint brush allocation; per-cell styles; duplicate `GetCellSummary` per cell | Med | Buffered subclasses; static brushes; column styles (Phase 7) |
| P10 | Shell COM metadata: ~680 late-bound calls per file | Med | Cache the two column indices after first file (Phase 7) — *not* an ffmpeg-probe backend swap |
| P11 | Full grid rebuild for single-row add/move/delete | Med | Single-row grid mutations (Phase 5d) |
| P12 | Superseded lock-probe loops never cancelled (keep opening files exclusively) | Med | In-loop version check (Phase 5e) |

---

## Part 2 — Target Architecture

### 2.1 Design principles

1. **Sequencing beats end-state purity.** In a codebase with no test framework, a stringly-typed status protocol, live reference aliasing, and hand-rolled threading invariants, the dominant regression risk is the *order* in which things are touched. Every phase ends with a releasable EXE built by the unchanged `构建EXE.ps1` path; the migration can pause indefinitely at any phase boundary.
2. **Convert silent failures into checked ones before moving code across them** (PlanStatus enum before decomposition; test gates before any change).
3. **Extract the provably decoupled first** (all-static Ffmpeg.cs), the most-aliased last (`currentPlan`/Preview).
4. **Behavior changes are quarantined**: every deliberate fix (Clone/ShotLabel bug, export cancel, async startup, frame-strip sampling) lands in its own commit with its own flipped characterization test — never inside a move/refactor commit.
5. **C# 5 is retained through the migration** (both build paths run the v4.0.30319 compiler). A `LangVersion 5`-pinned SDK csproj is stood up as a *parity shadow build* mid-migration; the language lift and toolchain promotion happen only after it survives several releases (Phase 8).

### 2.2 Module boundaries and interfaces

**Core** (`src/Core` — zero `System.Windows.Forms`/`System.Drawing`, build-gated)
- `RenamePlanBuilder.BuildPlan(rows, NamingSettings, IFileSystemProbe) : List<RenamePlan>` — pure naming pass + probe-driven validation pass; `IsBlockingIssue(PlanStatus)`.
- `PlanStatus` enum `{Ready, Unchanged, SourceMissing, TargetExists, DuplicateNewName, TargetLocked, PendingOverwriteExport}` — replaces the six Chinese literals; `TargetLocked` finally becomes a first-class state instead of a background-thread string injection.
- `ShotLabelParser.TryParse/Format` — the Grid.cs:442 regex colocated with `FormatShotLabel` so parse/format evolve together.
- `PlanExecutor.Execute(List<RenamePlan>) : ExecutionResult` — the one `File.Move` loop + the **single** row write-back implementation (today triplicated in Rename.cs:127-134, Export.cs:284-297, History.cs:145-158).
- `ExportPlanBuilder` (ex-`PrepareExportPlan`) + `RenamePlan.Clone()` (fixes the dropped-ShotLabel bug; deliberately **shares** the `Row` reference, pinned by a `ReferenceEquals` test).
- `RenameHistoryStore` (TSV encode/decode/save/load), `VideoImportService` (extension filter + `NaturalPathComparer` + duplicate skip).
- Abstractions: `IFileSystemProbe {FileExists, IsFileLocked, GetUniquePath}`, `IClock`, `IStatusSink {SetStatus(string)}`, `IUiDispatcher {bool Post(Action)}`.

**Media** (`src/Media` — may use `System.Drawing`, never Forms)
- `FfmpegLocator.Resolve()` — memoized behind a lock; **never caches a failed resolve** (the shipped drop-ffmpeg-and-retry workflow must keep working); candidate order and resource name preserved byte-for-byte.
- `FfmpegArguments` (single `QuoteArgument`), `FfmpegRunner` (process + progress parsing, tracked handle + `Kill`).
- `VideoExportService` (the `.vmr_` temp → `File.Replace` dance preserved exactly).
- `MediaLoadScheduler` — **one dedicated STA worker** + most-recent-first queue replacing thread-per-request spawning (STA hard-coded; version-token staleness guards kept at the same points).
- `ThumbnailCache` — the LRU trio as one disposable class owning image lifetime (`ReferenceEquals` guard before disposing anything possibly on display).

**Services** (`src/Services/Licensing`, `src/Services/Update`)
- `LicenseValidator` (pure: RSA, parsing, expiry, clock-rollback; takes `IClock` + `IMachineIdProvider`) / `LicenseStore` (DPAPI persistence, byte-for-byte compatible). Dialog loop moves out; `LicenseDialog` takes a `Func<string, ActivationResult>`.
- `UpdateChecker` / `UpdateDownloader` (+ timeout, `CancelAsync`, cancel button) / `UpdateInstaller` (`Environment.Exit` moves to caller) / `MiniJson` (one parser) / `WebClientFactory`. Dead `CheckForUpdatesManually` deleted; `AboutForm` consumes the same checker.

**App** (`src/App` — shell, presenters, startup)
- Five presenters strangle the god form: `GridPresenter` (render/drag-drop/CRUD, owns the `rendering` re-entrancy flag, converts the 1-based `RowIndex` in exactly one place), `PreviewPresenter` (**owns `currentPlan`**; merges the three refresh variants into one diff-based `Update(RefreshKind)` with a column map + `OldPath→item` dictionary; in-loop probe cancellation), `DetailsPresenter`, `RenameController`, `ExportController` (progress dictionaries stay keyed by `ShotRow` reference; adds cancel + FormClosing kill).
- `StartupGates` owns the disclaimer→license→splash→update chain with the update check overlapped with the splash; all service MessageBoxes land here.
- `MaterialRenamerForm` shrinks to field declarations, constructor wiring, disposal, and `BuildUi` factored into named `Build*` factories with named handlers.
- **Completion criterion (anti-re-sharding rule): a partial file may be deleted only when every field it touched has exactly one owner.** The §1.2 contention matrix is the literal checklist.

**Tests + Build** (`src/Tests`, `build/build-common.ps1`)
- `TestRunner`: C# 5, framework-free, named cases, runs **all** (no first-failure abort), prints `SelfTest OK` on zero failures (loader contract preserved). Compiled into the EXE, reachable only via `-SelfTest`/`-SmokeTest`.
- `build-common.ps1`: `Get-SourceFiles` / `Get-ReferenceAssemblies` / `Get-AppVersion` / `Assert-CorePurity`, shared by all four scripts.
- `VideoRenamer.csproj`: net48, `LangVersion 5` pinned, `GenerateAssemblyInfo=false`, `EmbeddedResource LogicalName=VideoMaterialRenamer.ffmpeg.exe` — **parity shadow build only** until Phase 8.

### 2.3 Target directory tree

```
videorenamercopy/
├── 构建EXE.ps1                  (release build — unchanged contract; sources build-common; fails hard on
│                                 missing ffmpeg; asserts embedded resource + version cross-check post-build)
├── video_material_renamer.ps1   (dev loop — unchanged contract, sources build-common)
├── 打包安装程序.ps1 / 发布更新到GitHub.ps1 / installer.iss
│                                (gain hard test gates + fixed version guard reading src/App/AppInfo.cs)
├── build/
│   ├── build-common.ps1         (Get-SourceFiles, Get-ReferenceAssemblies, Get-AppVersion, Assert-CorePurity)
│   └── verify-artifact.ps1      (post-build: resource name, FileVersion==AppInfo, size floor >90MB,
│                                 exactly one hyphen-free EXE, no stray DLLs)
├── VideoRenamer.csproj          (Phase 4+: parity dual-build, NOT the release path until Phase 8)
├── docs/perf-fixture/           (persistent 60-row/200-clip benchmark fixture + recorded baselines)
└── src/                         (everything below swept by the same csc glob — file moves are free)
    ├── App/
    │   ├── Program.cs, AppInfo.cs, AssemblyInfo.cs
    │   ├── Startup/      (StartupGates.cs, UpdatePrompter.cs)
    │   ├── MainForm/     (MaterialRenamerForm.cs — fields/ctor/wiring/disposal,
    │   │                  MaterialRenamerForm.Ui.cs — named Build* factories,
    │   │                  MaterialRenamerForm.Theme.cs)
    │   ├── Presenters/   (GridPresenter, PreviewPresenter, DetailsPresenter,
    │   │                  RenameController, ExportController, StatusReporter, UiDispatcher)
    │   ├── Forms/        (AboutForm, DisclaimerDialog, LicenseDialog, SplashForm,
    │   │                  UpdateDownloadProgressForm)
    │   ├── Grid/         (DataGridViewProgressCell/Column, DoubleBufferedGridView, DoubleBufferedListView)
    │   └── Theme/        (UiTheme.cs — typed UiRole enum, AppIcon.cs)
    ├── Core/             ← zero System.Windows.Forms / System.Drawing (build-gated)
    │   ├── Abstractions/ (IFileSystemProbe, IClock, IStatusSink, IUiDispatcher)
    │   ├── Models/       (ShotRow, RenamePlan [+Clone()], RenameOperation, ExportOutputMode,
    │   │                  PlanStatus, NamingSettings, VideoFileInfo)
    │   ├── Naming/       (RenamePlanBuilder, ShotLabelParser)
    │   ├── Execution/    (PlanExecutor, ExportPlanBuilder, RenameHistoryStore)
    │   └── Import/       (VideoImportService, NaturalPathComparer)
    ├── Media/            ← may use System.Drawing, never Forms
    │   ├── Ffmpeg/       (FfmpegLocator, FfmpegArguments, FfmpegRunner)
    │   ├── Providers/    (VideoMetadataReader, VideoThumbnailProvider, VideoFrameStripProvider)
    │   ├── VideoExportService.cs
    │   ├── MediaLoadScheduler.cs
    │   └── ThumbnailCache.cs
    ├── Services/
    │   ├── Licensing/    (LicenseValidator, LicenseStore, MachineId)
    │   ├── Update/       (UpdateChecker, UpdateDownloader, UpdateInstaller, MiniJson, WebClientFactory)
    │   ├── DisclaimerStore.cs
    │   └── Net/          (TimeoutWebClient.cs)
    └── Tests/            (compiled into the EXE; reachable only via -SelfTest/-SmokeTest)
        ├── TestRunner.cs
        ├── CoreTests.cs / MediaTests.cs / ServicesTests.cs / UiSmokeTest.cs
        └── Fixtures/     (golden latest.json, golden rename_history.tsv, golden-master plan corpus,
                           old-binary license state files)
```

**Boundary enforcement under C# 5:** `Assert-CorePurity` greps for the namespace strings `System.Windows.Forms` / `System.Drawing` **anywhere in file text** (not just `using` lines) — this also catches fully-qualified references, closing the gap a using-only grep would leave. Compiler-enforced boundaries (NetArchTest) arrive with Phase 8.

---

## Part 3 — Migration Plan

Every phase ends releasable via the unchanged `构建EXE.ps1` → `打包安装程序.ps1` → `发布更新到GitHub.ps1` path.

### Phase 0 — Seal the pipeline (zero `.cs` files touched) · risk: low
1. Create `build/build-common.ps1` (dedupe the source glob + reference lists copy-pasted between 构建EXE.ps1:31-33/71-74 and video_material_renamer.ps1:18-22; `Get-AppVersion` from `src/AppInfo.cs`); rewire all four scripts.
2. **Fix the dead version guard**: `发布更新到GitHub.ps1` reads `src/AppInfo.cs` and **fails** (not skips) on empty/mismatch vs the dist EXE's FileVersion; cross-check `AppInfo.Version` vs `AssemblyFileVersion` in `构建EXE.ps1`.
3. `构建EXE.ps1`: missing `tools\ffmpeg.exe` becomes a **throw** (opt-out `-AllowNoFfmpeg` for dev); add `build/verify-artifact.ps1` run after every build — asserts manifest resource literally named `VideoMaterialRenamer.ffmpeg.exe`, FileVersion==AppInfo.Version, EXE size floor >90 MB, exactly one hyphen-free EXE, no stray DLLs. *(Converts all three silent-ship failure modes into hard build failures on day one.)*
4. Wire hard test gates: 打包安装程序.ps1 and 发布更新到GitHub.ps1 invoke `-SelfTest`/`-SmokeTest` and abort unless output contains exactly `SelfTest OK`/`SmokeTest OK`.
5. Add **internal test hooks** on the form (e.g. `internal Button TestBtnAbout`) so the smoke test stops asserting private controls *before* decomposition begins.
6. Delete vestigial script code (unused `$mainScript` requirement, `$generatedSource`); fix 打包安装程序.ps1's unreliable `$LASTEXITCODE` check.

**Gate:** build before/after with the csc command line echoed and diffed — provably identical binary inputs. Plant a stale EXE in `dist\` and confirm the version guard now trips; deliberately break one assert to prove the gate refuses to package.

### Phase 1 — Characterization-test net · risk: low
1. `TestRunner.cs` (named cases, run-all, per-case PASS/FAIL, `SelfTest OK` contract preserved); port `RunSelfTest`'s body into discrete cases (private statics → `internal`).
2. Pin **current** behavior: all six status strings produced by `BuildPlan` against fixtures; `IsBlockingIssue` truth table incl. `目标文件被占用`; `PrepareExportPlan` derivation + duplicate throw; `CloneRenamePlan` field-by-field — **asserting the dropped-ShotLabel bug as-is** with a marker comment (flipped in Phase 2); `NormalizeCustomTailText` truncation; `GetUniquePathWithSuffix`; the shot-label regex table; history-TSV round-trip + golden fixture; ffmpeg arg golden strings.
3. **Golden-master plan corpus**: a committed fixture shot table (main+backup, row-scene on/off, `28A`, custom + batch tails `补_1…`) with its full `(OldPath, NewName, Status)` output — byte-identical through every later phase; performance work is *forbidden* from changing it.
4. Golden release-contract cases: `ParseManifest` against a verbatim published `latest.json`; `IsNewerVersion` table; asset-pairing regex.
5. Capture **license state files written by the old binary** as committed fixtures (loaded byte-for-byte by the future `LicenseStore` in a permanent regression test).

**Gate:** every case passes on *unmodified* production code (a failing characterization test means the test is wrong). Target ≥40 named cases vs today's 1 monolith. Drill: deliberately break one naming rule and confirm the net fails.

### Phase 2 — Extract the already-decoupled statics · risk: medium
1. `src/Media/Ffmpeg/`: `FfmpegLocator` (memoized; **re-resolves if the cached path no longer exists**; never caches failure), `FfmpegArguments` (delete the duplicate `QuoteArgument`), `FfmpegRunner`. Delete `MaterialRenamerForm.Ffmpeg.cs`; update the four call sites.
2. `ShotLabelParser` (regex + `FormatShotLabel` colocated); `ExportPlanBuilder`; `RenamePlan.Clone()` — **flip the ShotLabel characterization test in its own commit** (deliberate bug fix).
3. `RenameHistoryStore` extracted from History.cs:22-97; move the misplaced `SetOperationUiEnabled` to the Ui partial.
4. **Early perf win (judge-mandated pull-forward):** route the two `NumericUpDown` handlers through the existing `RefreshPreviewNamesOnly` path (its count-mismatch fallback already self-corrects, Preview.cs:266-272) — kills the worst user-visible freeze years before Phase 7.

**Gate:** TestRunner green (arg goldens byte-identical). Manual: build on a machine with **no** loose ffmpeg.exe; verify thumbnails, hover-scrub, both export modes (proves embedded extraction), undo. Shippable as a patch release.

### Phase 3 — Compile-checked seams · risk: medium (highest-leverage phase)
1. **`PlanStatus` enum** + `PlanStatusText.For(status)` returning the exact current Chinese strings. Sweep every equality site (RenamePlanBuilder.cs:74-97/125-132, Preview.cs:223/374-376, Rename.cs:37, the self-test assertion). Completion rule: repo-wide grep for each of the six literals returns **zero hits outside `PlanStatusText.cs`**.
2. `IStatusSink` (`StatusReporter` over `statusLabel`): mechanical replacement of the ~30 direct writes across 8 partials.
3. `NamingSettings` snapshot struct built in one `ReadNamingSettings()`; `BuildPlan` takes it + `IFileSystemProbe` (plan tests go deterministic, no temp files). Preserve the `numScene==null` guard semantics (load-bearing during ctor ordering).
4. `UiDispatcher` extracted from `QueueOnUi` **preserving the exact drop-when-disposed / false-return-disposes-image contract**; delete the write-only `detailLoadVersion` field.

**Gate:** TestRunner green with unchanged string goldens. Standing 10-minute **manual conflict walkthrough** (written down; reused ever after): each conflict type side-by-side vs the previous build — identical text, colors, and blocking behavior.

### Phase 4 — Directory restructure + purity gate + shadow csproj · risk: medium
1. **4a: pure `git mv`** into the target tree — *no text edits in this commit* (the glob build makes moves free; "identical bits modulo moves" stays mechanically reviewable).
2. **4b: text-only cleanup commit**: trim the copy-pasted 15-line using-headers; models lose WinForms imports (`VideoFileInfo.ListSummary` display text moves toward App).
3. Enforce `Assert-CorePurity` in `构建EXE.ps1` (full-text namespace grep, catches qualified names).
4. Add `VideoRenamer.csproj` (net48, `LangVersion 5` pinned, `GenerateAssemblyInfo=false`, `EmbeddedResource LogicalName=...`, `AssemblyName 视频素材镜头表命名工具`) + parity assertions (file-set vs `Get-SourceFiles`, FileVersion, manifest resource list). **csc remains the sole release path.**

**Gate:** both builds runnable; parity assertions pass; full TestRunner + smoke + installer end-to-end on the csc output.

### Phase 5 — Form decomposition, five releasable cuts (blast-radius order) · risk: high
- **5a `MediaLoadScheduler` + `ThumbnailCache`**: one STA worker + most-recent-first queue; centralized image ownership (`ReferenceEquals` guard before dispose); `OnFormClosed` delegates to `Dispose`. *Explicit test: measure details-panel first-paint latency on a slow clip — the single worker serializes loads that ran in parallel; if metadata/thumbnail lag behind strip decodes, add a fast-lane/preemption, don't ship a latency regression.*
- **5b `ExportController`**: orchestration out; progress dictionaries stay ShotRow-reference-keyed; **new**: cancel button, FormClosing confirm-kill-cleanup, startup sweep of orphaned `.vmr_*`/`VMR_Strip_*` temps (each behavior change its own commit).
- **5c `RenameController`**: validate → confirm → `PlanExecutor.Execute` **on a worker** (the UI-thread `File.Move` freeze dies here) → history save → refresh; rebuild-then-validate ordering preserved.
- **5d `GridPresenter`**: one `PopulateGridRow` (kills the 12-line duplication); single-row insert/remove/move; owns the `rendering` flag; 1-based↔0-based conversion in one place; smoke-test assertions updated in the same commit.
- **5e `PreviewPresenter`**: owns `currentPlan`; three refresh variants merge into diff-based `Update(RefreshKind)` with a column map + `OldPath→item` dictionary; probe loops observe cancellation **inside** the batch loop; `BuildUi` factored into named factories (ctor ordering `BuildUi→ApplyTheme→RenderAll` preserved).

**Gate after every sub-step:** full TestRunner + smoke + the complete manual regression checklist (import both cell types, row ops, `28a`→`28A`, tails, all conflicts side-by-side, rename+undo, both export modes + mid-export cancel, dark-mode toggle mid-operation, hover-scrub). 5a additionally: thread count flat during rapid navigation. 5e: side-by-side preview-column comparison vs previous build. **Completion criterion: the §1.2 field-contention matrix — a partial is deleted only when each of its fields has exactly one owner.**

### Phase 6 — Services decoupling + startup · risk: high
1. `LicenseValidator`/`LicenseStore` split (persistence code verbatim; machine code cached; `SHA256.Create` in usings); dialog loop → `StartupGates`; fixture state files from Phase 1 must load byte-for-byte.
2. `UpdateChecker`/`Downloader`/`Installer` split; delete dead `CheckForUpdatesManually` + the unused overload; `AboutForm` reuses the checker; one `MiniJson`; TLS 1.2 set once at startup by name.
3. Downloader hardening: `WaitOne` timeout + `CancelAsync`, Cancel button, throttled progress, labels themed once.
4. `StartupGates`: update check on a background thread **overlapping the 4-second splash**; `Environment.Exit` moves out of the service so the old EXE never overlaps its replacement.

**Gate:** rehearsals, not mocks — full check→download→sha256→restart against a **real draft GitHub prerelease**; license continuity on a machine holding a **real existing activation** (no re-prompt, correct remaining days) + fresh activation + clock-rollback trip. Cold offline launch shows the main form in ~4-5 s (vs up to ~24 s today).

### Phase 7 — Performance pass under the full net · risk: medium
Complete P3 (all count-stable handlers through the diff path, 120 ms debounce, prebuilt-plan handoff — no more double `BuildPlan`); `File.Exists` memoized per refresh cycle via the probe; cache-only summary column with async backfill; double-buffered subclasses; `Rows.AddRange`; static brushes; single `GetCellSummary` per cell; autosize skipped on unchanged content-version; cache eviction on rename/export completion **plus `Length`+`LastWriteTime` mismatch-as-miss**; COM column-index caching (~680 calls → ~2); `GetUniqueCustomTail` HashSet; kill-on-supersede for strip processes.
**Flagged, separate commit, product sign-off:** frame strip sampled across the full duration (visible output change).

**Gate:** golden-master corpus byte-identical (hard rule); the standing conflict walkthrough; post-export details show *post*-export size/resolution. **Numeric targets** on the persistent `docs/perf-fixture` (results committed, not buried in commit messages): spinner-tick refresh **>5×** faster with zero full rebuilds logged (debug counter on `Update(Full)`); thread count bounded ≤ ~3 during rapid navigation; 200-file network-share rename keeps the window repainting; GDI handle count stable across 50 open/close-details cycles; strip frame timestamps span the full clip on a 5-minute fixture.

### Phase 8 (follow-on project) — Toolchain promotion
Only after the parity csproj has shipped several releases as a shadow build: promote it to the release path (`构建EXE.ps1` becomes a thin `dotnet build` wrapper + `verify-artifact.ps1`), lift `LangVersion` (7.3+), port TestRunner cases to xUnit in a **separate test project** (tests leave the shipped EXE; internal test hooks retired), add NetArchTest boundary facts + CI, then retire the Add-Type dev loader. Also the right home for: splitting ffmpeg into a separately-versioned update asset (ends the ~100 MB-per-update problem — deliberately out of scope here because it changes the deployed updater contract), and an eventual net8.0-windows evaluation. The Phase-1 characterization corpus is exactly the net this migration lacks today — attempted in this order, the compiler swap inherits it for free.

---

## Part 4 — Risk register (top items)

| Risk | Mitigation |
|---|---|
| Missed status-literal comparison site in the Phase-3 enum conversion changes blocking/coloring silently | Phase-1 pins all six strings + `IsBlockingIssue` table first; enumerated site sweep; zero-grep-hits-outside-`PlanStatusText` completion rule; side-by-side conflict walkthrough |
| Cloning/snapshotting rows breaks undo & export progress (reference identity) | Standing rule: presenters never copy `ShotRow`; `Clone()` shares `Row` with a `ReferenceEquals` test; checklist exercises undo + per-row progress every sub-step |
| Threading invariants (`QueueOnUi` contract, version tokens, `rendering` flag, STA) silently broken by moves | Contracts preserved verbatim at the same guard points; flag moves with its readers in one commit; rapid-navigation + mid-operation stress steps in every threading-adjacent gate |
| C# 6+ syntax breaks both build paths | `构建EXE.ps1` *is* the language gate every phase; csproj pins `LangVersion 5` so IDE squiggles match csc |
| Deployed-contract drift (resource name, EXE name, tag/asset names, DPAPI, AppId) | §1.8 frozen list in `build-common.ps1` comments; `verify-artifact.ps1` after every build; golden manifest test; draft-release update rehearsal; license fixtures; installer.iss untouched |
| Only regression net is welded to private internals | Phase-0 internal test hooks + Phase-1 conversion before anything moves; test updates land in the same commit as each extraction |
| Long high-risk Phase 5 stalls mid-way | Five blast-radius-ordered, individually releasable cuts; any pause point is a coherent shippable hybrid |
| Checklist fatigue across Phases 5-7 manual gates | Checklist is short (10-15 min), written, and scoped per sub-step; the highest-risk changes also carry automated goldens (status truth table, golden-master corpus, debug counters) |

## Part 5 — Testing strategy (summary)

Three tiers, all hard pipeline gates from Phase 0: **(1)** TestRunner characterization suite (named cases, run-all; goldens for statuses, args, TSV, manifest; golden-master plan corpus; fixture-driven via `IFileSystemProbe`/`IClock`), invoked through the preserved `-SelfTest` contract and required by the packaging/publish scripts; **(2)** UiSmokeTest via `-SmokeTest` (form construction, theme, control presence — via internal hooks, updated in lockstep with each extraction); **(3)** the standing 10-15 minute manual regression walkthrough after every releasable commit in Phases 5-7, run side-by-side against the previous build for conflict statuses and preview content. High-consequence externalities get **rehearsals, not mocks**: real draft-prerelease update cycle, real existing activation. Characterize-then-flip discipline: known bugs are pinned as current behavior and fixed only in their own flagged commits.

---

*Full assessment evidence (per-subsystem findings with file:line references), the three candidate proposals, and the judge-panel scoring are preserved in the session workflow output; this document is the synthesized, actionable plan.*
