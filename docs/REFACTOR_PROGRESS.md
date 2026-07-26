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

## V2 health assessment ✅ — 85/100 (baseline ≈38); superseded by V3 below.

---

# Modernization V3 (branch `refactor/modernization-v3`, plan: [MODERNIZATION_V3_PLAN.md](MODERNIZATION_V3_PLAN.md))

Standing invariants held across all V3 phases: golden masters byte-identical (naming untouched), every commit gated in-script (SelfTest + SmokeTest before `git commit`), G3 build + verify-artifact at each phase boundary, 10 frozen contracts untouched.

## Phase 8 — Foundations ✅ (4768f45, 3468e32, 1199ea3, 9633244, 6d85f58)

| Item | Evidence |
|---|---|
| 8a currentPlan 单一所有者 | 字段+唯一写入点 ReplaceCurrentPlan 同驻 Plan 分部；grep 证明仅 1 处变更点 |
| 8b BuildUi → 10 具名 Build* 方法 | 纯搬移（语句顺序逐条一致）；多语句匿名委托改具名处理器 |
| 8c ProcessArguments 唯一实现 | ffmpeg 委托逐字节一致断言；导出参数黄金值不变 |
| 8d AppLog | 1MB 滚动、绝不抛异常；崩溃仅观察记录；更新静默路径留痕 |
| 8e LicenseValidator 纯函数 | 机器码+时钟注入；9 新用例（内置样例密钥全部已过期+哑机器码，注入 nowUtc 走通有效路径；拼接签名攻击、错误表逐字锁定；DPAPI 往返证明存储层完好） |

Gates: SelfTest 55→65, SmokeTest OK, G3 PASS.

## Phase 9 — 暖纸色系主题 ✅ (5a8470a, 87509ca) — 标注：视觉变更

- 9a UiTheme 全角色重写（暖白纸面/暖炭深色 + 陶土主色 #BA5B34/#D97757）；按钮/复选框悬浮按下态；进度刷随主题换刷；全部散落色值清扫归位；palette_pins ×2 锁定色值。
- 9b/9c 1px 发丝分隔线；DWM 深色标题栏（Win10 1903+/1809 回退，旧系统静默）。
- 证据：scripts/capture-ui.ps1 离屏截图（浅/深）逐张人工核对；期间用像素探针排除了一次"删除按钮变红"的误判（ClearType 边缘色）。

## Phase 10 — 布局重构 ✅ (764ac3c, 809c0d9, 1fa56af, 83f90b1) — 标注：布局/新行为

- 10a 底部执行栏：状态 + 导出选项 + 取消命名 + 主按钮同驻（改变按钮含义的开关紧挨按钮）。
- 10b 分区工具条：行/素材操作迁到镜头表上方；删除记录+自定义末尾迁到预览上方；头部收窄 100→52px。
- 10c 详情面板可折叠（28px 细条）。冒烟门禁抓出真实缺陷：Control.Visible 在未显示窗体上恒 false，不能当状态——改显式字段。
- 10d 执行栏进度条+可见取消：导出立即杀 ffmpeg；重命名文件间停（已完成部分照常入撤销历史）；取消后如实弹窗汇报。
- 10e Ctrl+Z=取消命名、Ctrl+Enter=执行（文本编辑上下文不接管）。
- 10f 系统级 DPI 感知：app.manifest（缺失即构建失败）+ 全部 6 窗体 AutoScaleMode.Font（基准 7x17 实测）；96dpi 截图逐像素不变证明缩放系数恰为 1；高 DPI 待人工核验；回退=移除 /win32manifest。
- SmokeTest 同提交扩展：执行栏结构、进度初始隐藏、折叠往返断言。

## Phase 11 — 性能收尾 ✅ (5084a53, 9685e1d)

- 11a 120ms 尾沿防抖（事件层；直接调用不受影响）：微调框连打 N 击 N 次全表 BuildPlan → 停手后 1 次。
- 11b CachedFileSystemProbe：单刷新周期 FileExists 记忆化（IsFileLocked 决不缓存）；计数用例锁定。
- 11c 网格行级增量操作（增/移/删）：计数前置不满足即回退整表重建；冒烟等价性断言（增量后网格内容 == 整表重建逐格一致）。
- 11d 启动清扫更新临时目录 1h 前孤儿（原来 ~100MB update_*.exe 永不清理）。

## Phase 12 — 更新子系统 ✅ (abc8d02, 54e346a, 156cb43)

- 12a 纯逻辑/UI 分离（逐字迁移）：UpdatePrompter（App）承载提示/进度窗/退出编排；Services 的 UpdateManager 零 WinForms；About 与启动共用同一下载/重启实现。
- 12b 加固（经批准）：替换脚本 try/catch/finally——Copy 失败写标记+重启旧版+finally 必清下载文件；目录不可写→提权（一次 UAC）；拒绝提权绝不退出进程；下次启动提示失败原因。修复"接受更新后什么都没发生"（Program Files 安装的既有缺陷）。脚本形态+引号转义 2 用例锁定。**残余风险：提权端到端待下次真实发布人工验证。**
- 12c 密钥默认有效期 30 天：签发端（本地工具，私钥不入库）默认改 30；应用零逻辑改动（密钥自带过期时间）；到期文案去掉写死天数。
- 验收：`发布更新到GitHub.ps1 -DryRun` 全绿（sha256 生成、双门禁、停在上传前）。

## Phase 13 — 边界收口 ✅ (e872a0f)

- 13a LicenseGate/DisclaimerGate（App）逐字承接 UI 编排；LicenseManager 只剩验证委托+DPAPI 存储（磁盘格式逐字不动）；DisclaimerManager 只剩记录读写。
- 13b 两道新门禁并入构建，均经植入违例演练（必须 FAIL 才被信任）：services-purity-gate（Services 全文无 WinForms/Drawing——顺带抓出 LicenseInfo/UpdateInfo 的残留 using 头）；palette-gate（裸颜色只许 App\Theme / App\Controls / Tests）。
- 13c App/Grid → App/Controls（100% 相似度纯移动）；Services 全部 using 头裁至实际所需。
- 依赖规则全部机器强制：Core ← Media/Services ← App ← Tests。

**Documented deviation:** App/MainForm 分部的 15 行拷贝板 using 头未裁（纯外观、12 个文件的机械修改留作小额债务，见健康评估遗留清单）。

## V3 final verification ✅

SelfTest **70/70** · SmokeTest OK（含结构/等价性/折叠断言）· 全部 **6 门禁** PASS（status-literal / core-purity / services-purity / palette / csproj-parity / verify-artifact）· 发布 DryRun 全绿 · 实测性能复测通过（见健康评估 v2）。V3 共 **17 提交**，每个提交可发布。
