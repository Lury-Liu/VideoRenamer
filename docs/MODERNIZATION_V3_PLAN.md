# Modernization V3 Plan — UI, Structure, Quality, Boundaries, Performance

Branch: `refactor/modernization-v3` (from `refactor/architecture-v2` @ 65068a8, health 85/100)
Predecessor docs: [ARCHITECTURE_REDESIGN_PLAN.md](ARCHITECTURE_REDESIGN_PLAN.md) · [REFACTOR_PROGRESS.md](REFACTOR_PROGRESS.md) · [HEALTH_ASSESSMENT.md](HEALTH_ASSESSMENT.md)

Scope approved by the user (2026-07-26): warm paper-like visual system, layout/workflow redesign, continued structural reorganization, code-quality debt (oversized builders, duplication, hardcoded styling, silent catches, unclear state ownership), explicit module boundaries, remaining performance items, and an evidence-backed final health score. Update hardening and the 30-day key default were evaluated and approved in the preceding exchange and are included as flagged phases.

## 0. Standing invariants (every commit, all phases)

| Invariant | Enforcement |
|---|---|
| Naming output never changes | Golden masters (corpus A/B) byte-identical in **all** phases — no naming logic is in scope |
| Every commit buildable + releasable | G1 SelfTest (`SelfTest OK`, all cases), G2 SmokeTest (`SmokeTest OK`), gates run in-script with `exit 1` **before** `git commit` |
| Release artifact intact | G3 `构建EXE.ps1` + `verify-artifact` at each phase boundary and after any build-script change |
| 10 frozen contracts (build-common header) | untouched: resource name, EXE name, tag/asset scheme, DPAPI files, AppId GUID, ffmpeg search order, loader strings, encodings |
| Behavior changes isolated | any user-visible change is its own commit, message prefixed `flagged:` equivalent (中文标注), listed in §7 |
| Toolchain | C# 5 via in-box csc; .ps1/.cs UTF-8 **with BOM**; PS 5.1 STA test loader unchanged |

## 1. Target directory tree

Already ~90% real after V2; V3 completes it and hardens the rules. `(new)` marks additions.

```
src/
├─ App/                        组装根 + WinForms 外壳（唯一可引用所有层的模块）
│  ├─ Program.cs                启动编排：免责声明 → 授权 → 启动画面 ∥ 更新检查 → 主窗体
│  ├─ AppInfo.cs · AssemblyInfo.cs
│  ├─ MainForm/                 主窗体视图分部：视图状态 + 事件接线（逻辑在 Presenters/Core）
│  ├─ Forms/                    对话框：Splash / Disclaimer / License / About / UpdateProgress
│  ├─ Controls/                 自绘控件（原 Grid/）：双缓冲 Grid/ListView、进度单元格
│  ├─ Presenters/               应用工作流：ExportController · RenameController
│  │                            (new) UpdatePrompter · LicenseGate · DisclaimerGate
│  └─ Theme/                    UiTheme（调色板唯一所有者，门禁保护）· AppIcon
│                               (new) WindowChrome（DWM 深色标题栏）
├─ Core/                       纯领域：零 WinForms/Drawing（机器门禁，现有）
│  ├─ Models/ · Naming/ · Execution/ · Import/ · Abstractions/
│  └─ Text/ (new)               ProcessArguments（引号处理唯一实现）
├─ Media/                      媒体处理：仅可引 Core；零 WinForms（机器门禁，现有）
│  └─ Ffmpeg/ · Providers/ · VideoExportService · MediaLoadScheduler · ThumbnailCache
├─ Services/                   基础设施：仅可引 Core；零 WinForms/Drawing（新门禁，13 阶段起）
│  ├─ Licensing/                LicenseManager（DPAPI 存储，字节不动）· (new) LicenseValidator（纯验证）
│  ├─ Update/                   (new) 纯清单/下载/校验/安装脚本生成（UI 提示移出到 App）
│  ├─ Net/ · Disclaimer/ · (new) Logging/AppLog
└─ Tests/                      自检用例（随 EXE 编译 —— 工具链冻结约束）
```

## 2. Module ownership and dependency rules

Dependency edges (machine-gated by end of Phase 13):

```
Core ← Media ← ┐
Core ← Services ├─ App（组装根）←─ Tests（可引全部）
Core ←──────────┘
```

- `Assert-CorePurity` (existing): Core no WinForms/Drawing; Media no WinForms.
- `Assert-ServicesPurity` (new, Phase 13): Services no WinForms/Drawing. Known violations burned down first: UpdateManager (MessageBox/ProgressForm/Application.*), LicenseManager (LicenseDialog/MessageBox), DisclaimerManager (dialog).
- `Assert-PaletteOwnership` (new, Phase 13): `Color.FromArgb`/named `Color.*` construction allowed only in `App/Theme/**` and `App/Controls/**`; everything else must go through `UiTheme` — the machine-enforced version of "no hardcoded styling".

Single owners for important state (after V3):

| State | Sole owner / sole writer |
|---|---|
| `currentPlan` | MainForm Plan partial — `ReplaceCurrentPlan()` only writer; all other partials read |
| `rows` | MainForm Rows partial ops (existing) |
| `undoStack` | History partial (existing) |
| Palette colors | `UiTheme` (gated) |
| Status-bar text | `IStatusSink` single implementation (existing) |
| Chinese status literals | `PlanStatusText` (gated, existing) |
| Thumbnails / video info | `ThumbnailCache` / `videoInfoCache` with controller-driven eviction (existing) |
| License files (DPAPI) | `LicenseManager` storage half — **byte-identical through V3** |
| Update temp dir | Update service (+ new orphan sweep) |
| Log files | `AppLog` (new) |

## 3. Phases, migration order, acceptance criteria

Order rationale: foundations before visuals (named builders make layout diffs reviewable) → theme before layout (color diffs never mix with structure diffs) → perf after layout (debounce targets final event wiring) → update subsystem next (its pure/UI split is prerequisite for) → boundary gates → assessment.

### Phase 8 — Foundations (behavior-preserving, ~5 commits)

1. **8a `currentPlan` single ownership**: all writers routed through one `ReplaceCurrentPlan` in the Plan partial; grep proves a single mutation site.
2. **8b `BuildUi` factoring**: ~340-line method → named `Build*` methods + named handlers for non-trivial delegates, *current layout unchanged* (pure code motion).
3. **8c `ProcessArguments` (Core/Text)**: single quoting implementation; UpdateManager's copy deleted; ffmpeg arg goldens byte-identical.
4. **8d `AppLog`**: size-capped file log under `%LocalAppData%\VideoMaterialRenamer\logs`; never throws; replaces silent catches on the paths where silence hides real failures (update, license save/load, export cleanup, startup sweeps). UI best-effort catches stay silent by design.
5. **8e License validation seam**: pure `LicenseValidator.Validate(key, machineCode, nowUtc)`; `LicenseManager` delegates, DPAPI/storage functions byte-identical (diff-verified). Tests use a **pinned, already-expired key** issued for a dummy machine code — worthless if extracted from the EXE, yet the parameterized clock lets tests exercise the valid path (`nowUtc` before its expiry). Private key never appears in `src/`.

**Acceptance**: G1 grows to ~64 cases (validator table ×8, quoting table); goldens identical; grep gates: 1 currentPlan writer, 0 `QuoteArgument` in Update; G3 PASS.

### Phase 9 — Warm paper theme (~3 commits)

1. **9a Palette swap** to the approved warm palette (light + dark; clay `#BA5B34`/`#D97757` primary): rewrite `UiTheme` role functions; sweep every straggler into it (3× hardcoded panel color in BuildUi, `grid.BackgroundColor`, inline alternating-row + checkbox accents, scene-column reds, progress-cell static brushes → theme-aware). Palette-pin self-test cases added.
2. **9b Comfort details**: button hover/pressed states, softened selection, warm alternating rows, 1px hairline separators replacing `"|"` labels, themed progress fills.
3. **9c DWM dark title bar** (`DwmSetWindowAttribute` 20→19 fallback) for all forms — flagged visual change.

**Acceptance**: screenshot harness (below) produces before/after PNGs both modes, reviewed; SmokeTest theme assertions green (they reference `UiTheme` so they track the palette); zero `Color.FromArgb` outside Theme/Controls (pre-gate grep); G3 PASS.

### Phase 10 — Layout redesign (~6 commits, flagged)

Pre-step: **`scripts/capture-ui.ps1`** — constructs the real form via Add-Type with a fixture, `DrawToBitmap` → PNG per theme; every visual commit ships with captures reviewed (then the final human pass).

1. **10a Footer command bar**: status + 导出1080x1920 + 文件名水印 + 取消命名 + 执行重命名/导出1080p co-located at bottom; naming settings remain top.
2. **10b Zone toolbars**: row/material ops move above the grid; 删除记录 + custom-tail cluster move above the preview (each control next to what it acts on).
3. **10c Collapsible details inspector** (toggle, width preserved in-session).
4. **10d Visible progress + cancel in footer** during operations — export cancel wires to existing `ExportController.CancelActive()`; rename cancel only if partial-history semantics verify safe (else disabled during rename, documented).
5. **10e Keyboard**: Ctrl+Z → 取消命名 (outside cell edit), Del in preview (existing), Ctrl+Enter → primary action.
6. **10f DPI awareness** (System-DPI manifest via `/win32manifest` + `AutoScaleMode.Font`) — flagged, single commit, trivially revertible; needs the user's high-DPI visual check.

**Acceptance**: SmokeTest updated same-commit to assert the new structure (loader contract `"SmokeTest OK"` frozen); drag-drop/column-layout/hover-scrub code untouched (containers only); captures reviewed per commit; G1/G2/G3 PASS.

### Phase 11 — Performance leftovers (~4 commits)

1. **11a 120 ms debounce** on spinner/checkbox preview refreshes (trailing-edge UI timer at the *event-wiring* level; direct method calls untouched so tests are unaffected). Measured criterion: 10-tick burst → 1 rebuild (was 10).
2. **11b Per-refresh probe cache**: `CachedFileSystemProbe` memoizing `FileExists` for one BuildPlan pass (`IsFileLocked` never cached). Criterion: probe-call count halves on the 120-clip fixture; statuses identical via `FakeFileSystemProbe` tests.
3. **11c Incremental grid row ops** (add/move/delete mutate rows in place; full `RenderGrid` stays for structural resets). Criterion: **equivalence check** — after a scripted op sequence, incremental grid state == full-rebuild state, added to SmokeTest.
4. **11d Update-temp orphan sweep** at startup (1 h age filter, mirrors export-temp sweep).

**Acceptance**: perf harness re-run with numbers recorded; goldens identical; G1/G2/G3 PASS.

### Phase 12 — Update subsystem (~3 commits)

1. **12a Pure/UI split (behavior-preserving)**: `UpdateManager` → Services-pure service (fetch/parse/download/verify/script-gen, callback-driven) + `App/Presenters/UpdatePrompter` owning MessageBox/progress-form; AboutForm's duplicate flow deleted; startup prompt text 逐字 preserved.
2. **12b Hardening (flagged, pre-approved)**: install-dir writability probe → elevated helper fallback (one UAC prompt); helper try/catch → on failure relaunches the old EXE + writes a failure marker the app reports next start; helper cleans the downloaded file in `finally`. Script *shape* characterized by tests; end-to-end elevation verified at next real release (documented residual).
3. **12c 30-day keys (flagged, pre-approved)**: keygen default `-Days 30`; `LicenseManager` expiry message drops the hardcoded number.

**Acceptance**: update-flow tests (script shape ×2, prompter-free service compiles without WinForms — pre-gate grep); `发布更新到GitHub.ps1 -DryRun` full pass; G1/G2/G3 PASS.

### Phase 13 — Boundary finalization (~4 commits)

1. **13a License/Disclaimer UI flows → App** (`LicenseGate`, `DisclaimerGate` presenters); Services halves keep validation/storage only; DPAPI functions diff-proven byte-identical.
2. **13b Gates ON**: `Assert-ServicesPurity` + `Assert-PaletteOwnership` added to `构建EXE.ps1` (fail-closed, self-match-proofed like the literal gate).
3. **13c Structural tidy**: `App/Grid/` → `App/Controls/` (pure `git mv`), App-partial using-header trims, `REFACTOR_PROGRESS` phase records.

**Acceptance**: both new gates PASS and *provably fail* on a planted violation (drill, then revert); csproj parity PASS; G1/G2/G3 PASS.

### Phase 14 — Verification + Health Assessment v2 (~2 commits)

Full gate matrix; perf harness numbers; architectural metrics (LOC by module, dependency-gate list, largest files, duplication check); `HEALTH_ASSESSMENT.md` v2 with per-dimension evidence; updated interactive checklist (theme both modes, new layout, footer cancel, collapsed inspector, elevated update, DPI on a high-DPI display); remaining-risk list.

## 4. Expected health-score trajectory (honest projections)

| After | Coupling | Maintain. | Testability | Reliability | Perf | Release | Overall |
|---|---|---|---|---|---|---|---|
| V2 baseline | 84 | 82 | 85 | 84 | 85 | 90 | **85** |
| P8 foundations | 85 | 85 | 87 | 84 | 85 | 90 | **86** |
| P9 theme | 85 | 86 | 87 | 84 | 85 | 90 | **86.5** |
| P10 layout | 86 | 87 | 87 | 84 | 85 | 90 | **86.5–87** |
| P11 perf | 86 | 87 | 87 | 85 | 89 | 90 | **87.5** |
| P12 update | 87 | 87 | 88 | 87 | 89 | 91 | **88.5** |
| P13 boundaries | 90 | 88 | 89 | 87 | 89 | 91 | **≈90** |

Ceilings that V3 deliberately does **not** remove (they cap ≈90–91): no CI runner, shadow csproj still unvalidated (no dotnet SDK on this machine), C# 5 cap, tests compiled into the shipped EXE, smoke gate needs an interactive desktop. All fall together in the future csproj-promotion project.

## 5. Test strategy

- Case count 55 → ~70: LicenseValidator table (8), ProcessArguments quoting table (2), palette pins (2), probe-cache counting via counting fake (2), update-script shape (2), plus SmokeTest gaining structure assertions + the grid-equivalence block.
- Golden masters: byte-identical across the whole effort (naming untouched — the strongest invariant available).
- Visual work: `capture-ui.ps1` PNGs reviewed at every visual commit (machine-renderable portion), final human checklist for what only eyes catch (hover states, HiDPI, real video content).
- Negative drills: both new build gates get a planted-violation drill before being trusted.

## 6. Risk register (top items)

| Risk | Mitigation |
|---|---|
| Visual regressions invisible to gates | screenshot harness at every visual commit + palette pins + final human checklist |
| DPI manifest changes rendering on user's machine | own flagged commit; single build flag → trivial revert; user verification requested explicitly |
| License flow relocation touches activation | DPAPI/storage functions byte-identical (diff-verified); validator characterized before any move; pinned test key is expired + machine-locked to a dummy code |
| Update hardening not end-to-end testable pre-release | script-shape characterization + `-DryRun` + explicit next-release manual step (documented residual risk) |
| Incremental grid ops corrupt row/index invariants | equivalence-vs-full-rebuild check in SmokeTest; `RenderGrid` fallback retained |
| SmokeTest churn during layout work | updated in the same commit as each structure change; loader contract frozen |
| Debounce breaks test determinism | debounce lives in event wiring only; tests call methods directly |

## 7. Pre-approved behavior changes (everything else is behavior-preserving)

New layout arrangement (10a–c) · visible in-footer progress/cancel (10d) · keyboard shortcuts (10e) · DPI awareness (10f, revertible) · warm palette + dark title bar (9) · update elevation/failure-recovery (12b) · 30-day key default + message (12c) · refresh debounce timing (11a) · incremental grid ops (11c, equivalence-proven).

Explicitly **out of scope**: naming logic (frozen), ListView virtualization, frame-strip full-duration sampling (product decision pending), splitting ffmpeg from the update download (contract change pending), installer-asset switch (would brick deployed updaters), csproj promotion/CI (needs SDK machine), rounded corners beyond the primary button.
