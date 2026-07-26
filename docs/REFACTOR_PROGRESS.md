# Refactor Progress & Verification Record

Branch: `refactor/architecture-v2` · Plan: [ARCHITECTURE_REDESIGN_PLAN.md](ARCHITECTURE_REDESIGN_PLAN.md)

Each phase lists its acceptance criteria and the **actual verification evidence** recorded when the phase gate was run. A phase is only marked ✅ when every gate produced the stated evidence.

## Standing gates (run after every phase, and for Phases 5-7 after every sub-commit)

| Gate | Command | Pass condition |
|---|---|---|
| G1 Self-test | `video_material_renamer.ps1 -SelfTest` (Windows PowerShell 5.1 STA) | every named case PASS + exact `SelfTest OK` |
| G2 Smoke test | `video_material_renamer.ps1 -SmokeTest` | exact `SmokeTest OK` |
| G3 Release build | `构建EXE.ps1` | csc exit 0 + `verify-artifact` PASS (resource name, version triple-match, >90MB, single hyphen-free EXE, no DLLs) |
| G4 Golden masters | included in G1 | corpus A/B byte-identical (only a flagged behavior-change commit may update them) |

## Phase 0 — Seal the pipeline ✅ (commit a4da130)

**Criteria:** zero `.cs` changes; identical compiler inputs; gates provably refuse bad artifacts.

| Check | Evidence |
|---|---|
| No-op proof | csc command line diffed against pre-change baseline: **identical, 54 lines** |
| G1/G2 | SelfTest OK / SmokeTest OK through rewired loader |
| verify-artifact on good EXE | PASS: version=1.0.6.0, 101,618,176 bytes, resource present |
| Version-guard drill | crafted EXE with FileVersion 1.0.5.34 → publish refused with exact mismatch message |
| verify-artifact drill | non-app EXE → all 4 independent checks reported FAIL + threw |
| Publish dry-run | `发布更新到GitHub.ps1 -DryRun` passed all gates, stopped before upload |

**Fixed en route:** dead version guard (regexed a file the constant left years ago); ffmpeg missing = warn→throw; `$LASTEXITCODE` misuse in 打包安装程序.ps1; PS 5.1 stderr/`$ErrorActionPreference` footgun in child-process reflection.

## Phase 1 — Characterization-test net ✅ (commit 3262d29)

**Criteria:** ≥40 named cases pinning CURRENT behavior, all green on unmodified logic; sabotage drill caught; loader contract preserved.

| Check | Evidence |
|---|---|
| Suite on unmodified code | **49/49 PASS** first run (characterizations correct) |
| Known bug pinned | `clone_rename_plan_copies_fields_drops_shot_label` asserts ShotLabel **is** dropped, marked for Phase-2 flip |
| Sabotage drill | suffix-case flip caught by 4 cases (incl. both golden masters); `SelfTest OK` withheld; green again after revert |
| Golden masters | corpus A (8 lines) + corpus B (8 lines) captured from live behavior incl. dup/missing/exists/unchanged/28A/补 tails |
| G2/G3 | SmokeTest OK; build + verify-artifact PASS (tests compile into EXE) |

**Coverage added:** all six builder statuses + `另存为新文件`, IsBlockingIssue table, PrepareExportPlan (3 branches), Clone field-by-field, NormalizeCustomTailText, GetUniquePathWithSuffix, shot-label regex table, history base64 golden, ffmpeg arg goldens, real published latest.json golden, version-compare table incl. 3-vs-4-segment pin.

## Phase 2 — Extract decoupled statics ✅ (commits 6cd53a1, 3442954, 2e16208, 7a9607e, e4e1864)

**Criteria:** behavior-preserving moves with goldens byte-identical; deliberate fixes isolated in own commits; embedded-ffmpeg path proven on built artifact.

| Check | Evidence |
|---|---|
| 2a Ffmpeg trio | `FfmpegLocator`(memoized)/`FfmpegArguments`/`FfmpegRunner` extracted; 406-line static partial deleted; QuoteArgument dedup; 49/49 + arg goldens identical |
| 2b ShotLabelParser + ExportPlanBuilder + Clone move | parse/format colocated; form statics deleted; 50/50 (Clone bug still pinned) |
| Clone bug fix (own commit 2e16208) | `Clone()` copies ShotLabel; test flipped to assert fix; 50/50 |
| 2d RenameHistoryStore | TSV format frozen-contract documented + first direct save/load tests; misplaced `SetOperationUiEnabled` → Ui partial; 51/51 |
| 2e Early perf win | episode spinner → incremental refresh (self-correcting fallback); scene spinner deferred to Phase 7 (its value is in group headers) |
| Embedded extraction end-to-end | built EXE loaded in child proc, CWD forced away, cache cleared → resolved to `%LocalAppData%\VideoMaterialRenamer\tools\ffmpeg.exe`, 101,457,920 bytes, `ffmpeg -version` runs |
| G3 | verify-artifact PASS |

**Defect fixed:** cloned export plans lost the `28A` ShotLabel (latent since the feature shipped).
**Defect logged (existing, fix in Phase 7):** the incremental preview path never refreshes group-header text, so shot/scene cell edits leave `第 N 行 / 场号 X / 镜号 Y` headers stale.

## Phase 3 — Compile-checked seams ⏳

**Planned criteria:** PlanStatus enum with zero Chinese status literals outside `PlanStatusText.cs` (grep-verified); ~30 `statusLabel.Text` writes behind `IStatusSink`; `NamingSettings` snapshot + `IFileSystemProbe` into BuildPlan; `UiDispatcher` preserving the QueueOnUi drop-contract; dead `detailLoadVersion` removed. Gates: G1-G4 + status strings/colors identical in side-by-side conflict walkthrough.

## Phase 4 — Directory restructure + purity gate + parity csproj ⏳
## Phase 5 — Form decomposition (5 releasable cuts) ⏳
## Phase 6 — Services decoupling ⏳
## Phase 7 — Performance pass ⏳
## Health assessment ⏳
