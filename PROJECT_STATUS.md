# Project Status — Video Material Shot-List Renamer

| | |
| --- | --- |
| **Source version** | **V1.0.7.0** (bumped `b1dcc27`; EXE built + verify-artifact PASS) |
| **Published release** | **v1.0.6.0** — remote tag on `main`; this is what the in-app updater currently serves |
| **Branch** | `refactor/modernization-v3`, pushed to `origin` (tip `6a1da4c`); `main` still at the V1.0.6.0 merge (`7f6855f`) |
| **Status generated** | 2026-07-26 |
| **Overall state** | Modernization V2+V3 complete · health **89/100** (evidence-backed) · V1.0.7.0 release publication pending |

## Summary

The app was carried through two verified refactor campaigns on top of the V1.0.6.0 baseline: **V2** (`refactor/architecture-v2`, 27 commits, health 38→85/100) rebuilt the structure under a characterization-test net, and **V3** (`refactor/modernization-v3`, 23 commits by `git rev-list --count`, 85→**89/100**) delivered the warm-paper visual system, the mockup-aligned four-zone layout (badges ①–④, letterless grid, always-visible 场号/进度 columns, colored preview statuses, 素材信息 inspector with resolution·duration, themed 执行中 strip), update-subsystem hardening, the **30-day** activation-key default, machine-enforced layer boundaries, and the remaining performance items. A 10-auditor + critic verification workflow checked every requirement against the code with file:line evidence (34/35 confirmed; the two real gaps it found were fixed in phase 10i, the one falsified doc claim corrected).

## Verification state (re-run at V1.0.7.0, this tree)

| Gate | Result |
| --- | --- |
| SelfTest (70 characterization cases incl. golden masters, license table, palette pins) | ✅ 70/70 → `SelfTest OK` |
| SmokeTest (UI construction + structure/equivalence/collapse/strip locks) | ✅ `SmokeTest OK` |
| 6 build gates (version / status-literal / Core+Media purity / Services purity / palette ownership / csproj parity) | ✅ all PASS |
| verify-artifact | ✅ version=1.0.7.0, 101,696,000 bytes, ffmpeg resource present, single hyphen-free EXE, no stray DLLs |
| Security | ✅ 生成授权密钥工具.ps1 (RSA private key) never tracked in any commit on any branch |

## Release checklist — V1.0.7.0

| Step | State |
| --- | --- |
| Source version bumped (`AppInfo.cs` / `AssemblyInfo.cs` / `installer.iss` / README) | ✅ Done |
| EXE built → `dist\视频素材镜头表命名工具.exe` (1.0.7.0) | ✅ Done |
| Branch pushed to `origin` | ✅ Done (`refactor/modernization-v3` @ `6a1da4c`) |
| Merged to `main` | ⚠️ Pending — PR: https://github.com/Lury-Liu/VideoRenamer/pull/new/refactor/modernization-v3 |
| Tag `v1.0.7.0` + GitHub Release + `latest.json` (`发布更新到GitHub.ps1`) | ⚠️ Pending — updater keeps serving v1.0.6.0 until this runs |
| Installer repackaged (optional; release asset must stay the raw EXE — frozen contract) | ⚠️ Pending |

## Remaining work (from the audit's manual-check list)

1. **Publish V1.0.7.0** — merge to `main`, then run `发布更新到GitHub.ps1` (asset must remain raw `VideoRenamer-v1.0.7.0.exe` + `latest.json`; an installer asset would break deployed updaters).
2. **Human visual pass** — side-by-side with the approved mockup in both themes, plus HiDPI 125%/150% (automation verified 96dpi only).
3. **Real export run** — 执行中 strip appears/updates (`N% · filename`)/hides; 取消 kills ffmpeg immediately.
4. **Elevated-update rehearsal** — Program Files install → UAC accept/decline paths; only testable at the next real release.
5. **`AppInfo` layer move** — Services/Media reference App-layer `AppInfo` (constants only); moving it to Core would make the dependency direction fully hold (task chip filed).
6. **CI / csproj validation** — the shadow `VideoRenamer.csproj` is parity-gated but never compiled (no dotnet SDK on this machine); validate on a machine that has one.
7. **`CHANGELOG.md`** — build/installer scripts already bundle it if present; still absent.

## Where the detail lives

- [docs/REFACTOR_PROGRESS.md](docs/REFACTOR_PROGRESS.md) — phase-by-phase log with verification evidence, deliberate mockup deviations, and release status.
- [docs/HEALTH_ASSESSMENT.md](docs/HEALTH_ASSESSMENT.md) — the 89/100 scoring with per-dimension evidence and honest-scope notes.
- [docs/ARCHITECTURE_REDESIGN_PLAN.md](docs/ARCHITECTURE_REDESIGN_PLAN.md) / [docs/MODERNIZATION_V3_PLAN.md](docs/MODERNIZATION_V3_PLAN.md) — the historical V2/V3 plans (banner-marked completed).
- [README.md](README.md) — user/contributor-facing overview, naming scheme, build & publish instructions (rewritten to the V3 tree).
