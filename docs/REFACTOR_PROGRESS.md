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

## Phase 3 — Compile-checked seams ✅ (commits d521783, 5159f4a, b356f42, 4b77695)

**Criteria:** stringly-typed status protocol → enum with enforced literal ownership; statusLabel writes funneled; control reads snapshot-ized; dispatcher contract declared; dead code removed.

| Check | Evidence |
|---|---|
| 3a PlanStatus enum (8 states incl. first-class `TargetLocked`/`SaveAsNewFile`) | all comparison/assignment sites converted; golden masters **byte-identical** (user-visible text zero change); `plan_status_text_goldens` pins all 8 strings |
| Literal-ownership gate | `Assert-StatusLiteralOwnership` in 构建EXE.ps1: 7 status literals outside PlanStatusText/PlanStatus/Tests = build failure; PASS after conversion (gate literals built from char codes so the gate can't match itself) |
| 3b IStatusSink | 35 direct `statusLabel.Text` writes across 6 partials funneled through `SetStatus`/write-only `StatusText`; only the implementation site remains |
| 3c NamingSettings + IFileSystemProbe | `ReadNamingSettings()` single control-read point; `BuildPlan(rows, settings, probe)` primary overload; `AddFilesToPlan` 14-param public API → private; `FakeFileSystemProbe` proves statuses deterministically without temp files |
| 3d IUiDispatcher + dead code | drop-when-disposed contract documented as frozen; write-only `detailLoadVersion` deleted (3 sites) |
| G1/G2 | SelfTest **53/53** (commit b356f42's message says 54 — actual is 53); SmokeTest OK |
| G3 | build + status-literal-gate + verify-artifact PASS |

**Note on the side-by-side conflict walkthrough:** the interactive portion (visual row-color comparison against a pre-phase build) is replaced by automated equivalents — status text pinned per-state by `plan_status_text_goldens` + golden masters, and styling logic untouched except the compile-checked comparison. A human visual pass over the five conflict types remains on the outstanding-verification list.

## Phase 4 — Directory restructure + purity gate + parity csproj ✅ (commits b0e2bcd, ee850c6, 928c2dc)

| Check | Evidence |
|---|---|
| 4a pure moves | 40 files `git mv`, **all at 100% similarity** (provably no text edits); target tree App/Core/Media/Services/Tests; only script change: version-parse paths follow AppInfo/AssemblyInfo into src/App |
| 4b header trims | 9 Core/Media files trimmed from the 15-line copy-pasted header to actual needs (pure models now zero usings) |
| Core-purity gate | `Assert-CorePurity` full-text scan (catches fully-qualified refs): Core = no WinForms/Drawing, Media = no WinForms → PASS, enforced in every build |
| 4c shadow csproj | LangVersion 5-pinned, GenerateAssemblyInfo=false, frozen LogicalName, same file glob; `Assert-CsprojParity` structural gate PASS |
| G1/G2/G3 | SelfTest 53/53; SmokeTest OK; verify-artifact PASS |

**Outstanding (documented):** no dotnet SDK/MSBuild on this machine → the shadow csproj has never been actually built; validate on an SDK machine before relying on it (structural parity is gated, binary parity is not).
## Phase 5 — Form decomposition ✅ (commits 9684e00, 25c57ee, 7924dc7, 3bebb3c, 736a08f, a5b1357)

| Cut | Evidence |
|---|---|
| 5a MediaLoadScheduler + ThumbnailCache | thread-per-request → 2 persistent STA workers (fast/slow lanes, newest-first); LRU cache as owning class; SelfTest 53/53 |
| 5b ExportController + VideoExportService | orchestration out of the form (`.vmr_`→`File.Replace` contract verbatim); **new behavior (own commits):** FfmpegCancellation kill-on-cancel, OnFormClosing confirm-abort guard, orphan temp sweeps (1h age filter) |
| 5c PlanExecutor + RenameController | File.Move loop off the UI thread; `PatchRowFileList` = the single write-back implementation (was 3 copies); 55/55 incl. 2 new executor cases |
| 5d/5e | `PopulateGridRow` dedup; export accessors re-homed; probe-loop in-loop cancellation |

**Documented scope decision:** grid/preview rendering stays in form partials (full presenter classes over the same live controls = re-sharding); the extracted classes carry the real state+logic. `currentPlan` single-ownership deferred (gap list).
**Process note:** commit 7924dc7 landed with a compile error because the gate output wasn't checked before committing (fixed same-session in 3bebb3c); all later commits gate in-script.

## Phase 6 — Services decoupling (update side) ✅ (commit 826bffc)

| Check | Evidence |
|---|---|
| Async startup check | fetch overlaps the 4s splash; offline cold start ~24s invisible hang → 4s; accept-update semantics verbatim |
| Download hardening | cancel button + 60s stall detection (WaitOne() was unbounded behind ControlBox=false); progress throttled; themed once |
| Dead code | CheckForUpdatesManually (~50 lines, 0 callers) deleted; TLS 1.2 once, by name |

**Documented scope decision:** LicenseManager split deferred — requires old-binary DPAPI fixtures capturable only on a machine with a real activation (gap list #1).

## Phase 7 — Performance pass ✅ (commits 900b1cc, b2b7484)

Measured (headless harness, 120-clip fixture): spinner-tick refresh 52.7→25.6 ms (2.1×); metadata read 249→17 ms/file (14.6×, values verified identical); GetUniqueCustomTail worst-case 0.24 ms/call. Golden masters byte-identical throughout. Fixes: stale-cache eviction, stale group headers, double-BuildPlan, O(n²) backfill, double buffering, static brushes, thumbnail retained-guard (use-after-dispose).

## Health assessment ✅ — see [HEALTH_ASSESSMENT.md](HEALTH_ASSESSMENT.md): **85/100** (baseline ≈38), gap list to ~90 enumerated.
