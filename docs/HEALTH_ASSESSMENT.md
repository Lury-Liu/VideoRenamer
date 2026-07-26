# Project Health Assessment v2 — Evidence-Backed

**Branch:** `refactor/modernization-v3` (17 commits over `refactor/architecture-v2`, which was 27 commits over the V1.0.6.0 baseline)
**Date:** 2026-07-26 · **Verification state:** SelfTest **70/70**, SmokeTest OK (incl. structure/equivalence/collapse assertions), **all 6 build gates** PASS, release EXE builds + verify-artifact PASS, publish `-DryRun` green end-to-end.

Scoring: six dimensions, equal weight, scored against measured evidence (commands and raw numbers in [REFACTOR_PROGRESS.md](REFACTOR_PROGRESS.md)). History: original baseline ≈38 → V2 refactor 85 → **V3 now**.

## Overall: **89 / 100** (V2: 85 · original baseline: ≈38)

Deliberately not rounded to 90: the enumerated remainder (§gaps) is real — chiefly no CI, an unvalidated shadow csproj (no dotnet SDK on this machine), and the human interactive pass (HiDPI, UAC-elevated update) that automation cannot perform. Everything scored below is re-runnable.

| Dimension | Baseline | V2 | V3 | Key evidence |
|---|---|---|---|---|
| 1. Architectural coupling | 35 | 84 | **90** | all dependency rules machine-gated; zero WinForms in Services; single owners gated |
| 2. Maintainability | 40 | 82 | **87** | BuildUi → 10 named builders; hardcoded styling eliminated + gated; dedup complete |
| 3. Testability | 25 | 85 | **89** | 70 named cases; license subsystem fully characterized; screenshot harness |
| 4. Reliability | 45 | 84 | **87** | silent update failure fixed; crash logging; safe cancel paths; orphan sweeps |
| 5. Performance | 50 | 85 | **89** | debounce; probe memoization; O(1) grid row ops; measured numbers held |
| 6. Release safety | 30 | 90 | **91** | 6 standing gates (2 new, drill-proven); DPI manifest build-enforced |

## 1. Architectural coupling — 90

- **Dependency rules fully machine-enforced** (every build): `Core ← Media / Services ← App ← Tests` via `Assert-CorePurity` + new `Assert-ServicesPurity` (full-text scans — qualified names can't hide); `Assert-StatusLiteralOwnership`; new `Assert-PaletteOwnership`; `Assert-CsprojParity`. Both new gates were **drill-proven** (planted violations caught; the services gate also caught two real leftovers on arrival).
- **Zero WinForms/Drawing in Services**: update/license/disclaimer UI orchestration lives in `App/Presenters` (`UpdatePrompter`, `LicenseGate`, `DisclaimerGate`), moved verbatim; Services keep pure logic + storage.
- **Single owners, verified**: `currentPlan` — one writer (`ReplaceCurrentPlan`, field co-located); palette — `UiTheme` only (gated); status literals — `PlanStatusText` (gated); status text — `IStatusSink`; quoting — `ProcessArguments`.
- License validation is a pure function (`LicenseValidator.Validate(key, machineCode, nowUtc)`) — machine code and clock injected; DPAPI storage untouched and covered by a round-trip test.
- **Held back by:** the main form is still 13 partials sharing view state (inherent to the single-form WinForms design; full MVP would be re-sharding); `AboutForm` keeps its own check choreography (download/restart now shared).

## 2. Maintainability — 87

- `BuildUi` (one ~340-line method) → **10 named `Build*` methods** + named handlers; the layout code now mirrors the visual zones it builds.
- **No hardcoded styling anywhere** — swept and machine-gated; the palette is one file with named roles, pinned by tests.
- Duplication complete: quoting (1 impl), row write-back (1), grid cell fill (1), update download/restart flow (1 — the AboutForm copy died).
- `App/Grid` → `App/Controls` (100%-similarity moves); Services + model using-headers trimmed to actual needs.
- Tree: App 5,219 / Core 959 / Media 1,320 / Services 1,045 / Tests 1,314 lines; largest production file 673 (`Ui.cs` — grown by builders, each method small).
- **Held back by:** App/MainForm partials still carry the 15-line copy-pasted using header (documented deviation); two regex JSON mechanisms; C# 5 syntax cap (toolchain-frozen until csproj promotion).

## 3. Testability — 89

- **55 → 70 named cases.** New: `LicenseValidator` table ×9 — the subsystem previously written off as untestable — using pinned **already-expired keys for dummy machine codes** (worthless if extracted from the EXE) with the injected clock walking the valid path; a DPAPI round-trip proving the storage layer; updater-script shape ×2 (hardening semantics pinned); palette pins ×2; quoting table; caching-probe counting.
- SmokeTest grew teeth: footer structure, progress initially hidden, inspector collapse round-trip, and the **grid incremental-vs-full-rebuild equivalence** check. It caught a real bug during development (`Control.Visible` is false on unshown forms — the collapse toggle was rebuilt on an explicit state field).
- `scripts/capture-ui.ps1`: off-screen screenshots of the real form (both themes) reviewed at every visual commit.
- Golden masters byte-identical through **all 17 commits** — naming output provably never drifted.
- **Held back by:** no CI runner; tests compile into the shipped EXE; UI beyond construction+structure needs the human pass; networking (`TimeoutWebClient`) untested.

## 4. Reliability — 87

New fixes on top of V2's nine:
1. **Silent update failure** (the big one): installer-based installs live in Program Files; the old replace-script died unelevated at `Copy-Item` *after the app had exited* — the user saw "clicked yes, window closed, nothing happened." Now: writability probe → elevated helper (one UAC prompt); script `try/catch/finally` — on failure it writes a marker (reason shown next start), **relaunches the old version**, and always deletes the ~100 MB download; declining UAC never exits the app.
2. Orphaned `update_*.exe` temp files (~100 MB each, previously never cleaned) swept at startup (1 h age filter).
3. Last-chance crash logging (`AppDomain.UnhandledException`, observe-only) + `AppLog` on silently-failing paths (update check, prompt flow, disclaimer save).
4. Cancel paths are honest: export cancel kills ffmpeg immediately; rename cancel stops between files with completed moves recorded in undo history; both report exact completed counts.
- **Held back by:** the elevated update path can't be end-to-end tested before the next real release (documented residual); Media best-effort cleanup catches stay silent by design; no structured log coverage beyond the targeted paths.

## 5. Performance — 89 (measured, re-run on final code)

| Metric | Before V2 | Now | Note |
|---|---|---|---|
| Spinner-tick refresh (120 clips) | 52.7 ms full rebuild | **25.6 ms** incremental | re-measured this session (53.3 → 25.6, 2.1×) |
| Rapid spinner burst (N ticks) | N full BuildPlans | **1** (120 ms trailing debounce) | by construction; event-layer only |
| `FileExists` probes per refresh | repeated per duplicate path | **memoized per refresh cycle** | `CachedFileSystemProbe`; lock probe never cached |
| Grid row add/move/delete | full grid rebuild | **only affected rows** | equivalence-asserted in SmokeTest |
| Metadata read (Shell COM) | 249 ms/file | **17 ms/file** | re-measured (264→17, 15.5×) |
| Tail uniqueness worst case | 10,000×n scans | 0.24 ms/call | re-measured |
| Media loader threads | unbounded | 2 persistent STA workers | V2, unchanged |
- **Held back by:** preview still fully refreshes on row ops (headers/indexes change — inherent); no ListView virtualization; frame-strip sampling unchanged (pending product decision).

## 6. Release safety — 91

- **Six standing gates** on every build: status-literal, core-purity, **services-purity (new)**, **palette (new)**, csproj-parity, verify-artifact — the two new ones drill-proven before being trusted.
- DPI manifest is a hard build requirement (missing = build error, not silent degradation); revert = one flag.
- Publish rehearsal re-proven this session: `发布更新到GitHub.ps1 -DryRun` runs both test gates, computes sha256, and stops before upload.
- All 10 frozen deployment contracts intact (resource name, EXE name, tag/asset scheme, DPAPI files, AppId, ffmpeg search order, loader strings, encodings).
- **Held back by:** no CI (nothing runs gates on push); shadow csproj still never actually built (no dotnet SDK here); smoke gate needs an interactive desktop.

## Remaining gaps — the path from 89 upward

1. **CI + csproj validation** (+1–1.5): build `VideoRenamer.csproj` on a dotnet-SDK machine, compare FileVersion/resources against the csc EXE, stand up CI running the gates headless. The single biggest remaining lever.
2. **The one-hour human pass** (checklist below) — especially **HiDPI rendering** (10f) and the **elevated-update rehearsal at the next real release** (12b). Automation cannot see these.
3. **Minor debt:** App/MainForm using headers; `AboutForm` check choreography; two regex JSON parsers; `Ui.cs` (673 lines) could split builders into a Layout partial.
4. **Product decisions pending (unchanged):** frame-strip full-duration sampling; splitting ffmpeg from the ~100 MB update download.
5. **Toolchain ceiling (unchanged):** C# 5 cap and tests-in-EXE fall only with the csproj-promotion project.

## Interactive verification checklist (one human hour)

Drag-drop import to both cell types · row ops (add/move/delete — now incremental) · `28a`→`28A` · custom tails + batch apply · five conflict types side-by-side vs a pre-V3 build · rename + undo + **Ctrl+Z** · both export modes · **mid-export cancel from the footer** (new) · **mid-rename cancel** (new) · close-window abort · update-dialog cancel · **update on a Program Files install → UAC prompt → success; decline UAC → app stays usable + next-start failure notice** (new, at next release) · warm theme both modes incl. **dark title bar** (new) · theme toggle mid-operation · **collapsible inspector** (new) · hover-scrub · **HiDPI display: layout scales, nothing clipped** (new) · offline cold start (~4–5 s).
