# VideoRenamer · Video Material Shot-List Renamer

A Windows desktop tool for batch-renaming raw video footage into a consistent, shot-list–based naming scheme such as `E1-S2-28A-T1.mp4`. It gives you a spreadsheet-style shot list, live rename preview with conflict detection, embedded-FFmpeg thumbnails and hover-scrub frame preview, optional 1080p re-export, undo history, and a built-in auto-updater.

| | |
| --- | --- |
| **Version** | V1.0.8.0 |
| **Author** | @寒松 |
| **Platform** | Windows · .NET Framework 4.x · WinForms |
| **Language** | C# 5 (compiled with `csc.exe` / `Add-Type`) |
| **Repository** | https://github.com/Lury-Liu/VideoRenamer |

> **This repository plays two roles at once.** It holds the application source *and* hosts the auto-update releases. The running app checks `https://github.com/Lury-Liu/VideoRenamer/releases/latest/download/latest.json` on startup and offers to download newer builds.

---

## What it does

The tool is built for organizing film / short-video material where every shot maps to numbered takes. You lay out a shot list in a grid — episode, scene, shot number, plus a set of *main* and *backup* clips per shot — then drag your footage into the rows. As you edit, the bottom panel previews the exact target file name each clip will get and flags any problem (missing source, name collision, target already exists, unchanged). When the plan looks right, you rename in place; optionally the tool re-exports each clip to 1080p (overwriting) using the embedded FFmpeg. Every rename batch is recorded so it can be undone.

## Naming scheme

```
E{episode}-S{scene}-{shot}{suffix}-{tail}{ext}
```

| Segment | Meaning | Example |
| --- | --- | --- |
| `E{episode}` | Episode number (global, from the toolbar) | `E1` |
| `S{scene}` | Scene number (per-row or a shared default) | `S2` |
| `{shot}{suffix}` | Shot number, with an optional 1–2 letter **uppercase** suffix | `28`, `28A` |
| `{tail}` | Take segment — auto `T1`, `T2`, … or a custom label | `T1`, `补_1` |
| `{ext}` | Original extension (lower-cased unless "keep case" is on) | `.mp4` |

Concrete example: `E1-S2-28A-T1.mp4`

- **Letter suffix (`28A`)** lets you insert a *bridge shot* between shots 28 and 29 without renumbering everything. Suffixes are normalized to uppercase, 1–2 ASCII letters.
- **Take segment (`tail`)** defaults to `T1`, `T2`, … per clip in a row. You can override it with a custom label; multiple clips get an underscore-separated running number (`补_1`, `补_2`) so digits never visually collide (e.g. `TT1` + `1` becomes `TT1_11`, not `TT111`).
- **Batch increment** — select several clips in the preview, type a base name, and "批量应用" assigns `base_1`, `base_2`, … (continuing the number if the base already ends in a digit).

## Features

- Spreadsheet-style shot list (episode / scene / shot number, main + backup clips per row) with drag-and-drop import and natural-order sorting.
- Live rename preview grouped by shot, with per-row status: 就绪 / 未变化 / 目标已存在 / 新文件名重复 / 源文件丢失 / 目标文件被占用.
- Conflict-safe planning — duplicate target names and in-use files are detected before anything is written.
- Embedded FFmpeg (bundled into the EXE as a resource): cover thumbnails, resolution/metadata read-out, and a 16-frame **hover-scrub preview** (move the mouse across the thumbnail to scrub through the clip).
- Optional **1080p re-export** with a per-row progress column.
- **Undo history** persisted to `rename_history.tsv`.
- **Warm-paper theme** in light and dark variants — the initial mode follows the Windows system setting, and a one-click 护眼模式 button toggles it (including the native dark title bar).
- First-run **disclaimer** gate and an **authorization-key** license gate.
- **Auto-update** from GitHub Releases with SHA-256 verification of the downloaded EXE.
- Built-in **self-test** and **smoke-test** entry points for fast regression checks.

## Project structure

```
videorenamercopy/
├─ src/                          # All C# source (namespace VideoRenamer)
│  ├─ App/                       # WinForms layer
│  │  ├─ Program.cs              # Entry point: Disclaimer → License → Splash → Update check → MainForm
│  │  ├─ AppInfo.cs              # Version, author, update URLs, app-data path
│  │  ├─ MainForm/               # MaterialRenamerForm split into 12 partial classes by concern
│  │  │                          #   Core · Ui · Grid · Rows · Preview · Details · Media
│  │  │                          #   Plan · Rename · Export · History · Theme
│  │  ├─ Presenters/             # RenameController, ExportController, LicenseGate, DisclaimerGate, UpdatePrompter
│  │  ├─ Controls/               # DataGridViewProgress{Cell,Column}, ZoneBadge, SlimProgressBar, …
│  │  ├─ Forms/                  # AboutForm, DisclaimerDialog, LicenseDialog, SplashForm, UpdateDownloadProgressForm
│  │  └─ Theme/                  # UiTheme — warm-paper palette, single owner of every color (build-gated); WindowChrome, AppIcon
│  ├─ Core/                      # Pure logic — zero WinForms/Drawing (build-gated)
│  │  ├─ Naming/                 # RenamePlanBuilder, ShotLabelParser — the naming engine (self-tested)
│  │  └─ Models · Execution · Import · Text · Abstractions
│  ├─ Media/                     # Embedded-FFmpeg locator/runner, VideoMetadataReader, thumbnail & frame-strip providers
│  ├─ Services/                  # Licensing, Update, Logging, Net, DisclaimerManager — zero WinForms (build-gated)
│  └─ Tests/                     # 77 characterization cases, compiled in and run by -SelfTest
├─ scripts/                      # build-common.ps1 (6 build gates), verify-artifact.ps1, capture-ui.ps1
├─ docs/                         # V3 plan, REFACTOR_PROGRESS.md, HEALTH_ASSESSMENT.md, historical plans
├─ assets/                       # fixed EXE icon, rotating startup ICOs, Inno Setup messages
├─ tools/                        # ffmpeg.exe (NOT in git — supply locally to build the embedded EXE)
├─ VideoRenamer.ps1    # Dev loader — compiles src/ in-memory and runs the app
├─ 构建EXE.ps1                   # Build the distributable EXE (runs all gates, embeds FFmpeg + icon)
├─ 打包安装程序.ps1              # Build EXE, then compile the Inno Setup installer
├─ installer.iss                 # Inno Setup script
├─ 发布更新到GitHub.ps1          # Publish EXE + latest.json to GitHub Releases (gh CLI)
├─ 生成授权密钥工具.ps1          # Dev-only license-key generator (git-ignored — never commit)
├─ 启动VideoRenamer.bat          # Convenience launcher
├─ dist/                         # Build output (git-ignored)
└─ installer/                    # Packaged installers (git-ignored)
```

The architecture deliberately keeps all *naming* logic in the pure static class `RenamePlanBuilder` (`src/Core/Naming` — no WinForms dependency, exercised by `RunSelfTest`), while UI wiring lives in the `MaterialRenamerForm` partials (exercised by `RunSmokeTest` plus manual checks). Layer discipline is machine-enforced at every build: `构建EXE.ps1` runs six source gates (version consistency, status-literal ownership, Core/Services UI-framework purity, palette ownership, shadow-csproj parity) plus artifact verification and the global app-identity gate.

## Build & run from source

Requires Windows with .NET Framework 4.x (`csc.exe` at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\`) and PowerShell 5.1.

```powershell
# Run the app straight from source (compiles src/ into memory each launch)
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1"

# Logic regression gate — must print "SelfTest OK"
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SelfTest

# UI smoke gate — must print "SmokeTest OK"
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SmokeTest
```

Running from the loader does not include FFmpeg, so thumbnail / metadata / hover-scrub features stay silent until you build the embedded EXE.

## Package a distributable

```powershell
# 1) Build the EXE (embeds tools\ffmpeg.exe and assets\app.ico) → dist\VideoRenamer.exe
powershell -ExecutionPolicy Bypass -File "构建EXE.ps1"

# 2) One-shot: build EXE + compile the Inno Setup installer → installer\...v<version>.exe
powershell -ExecutionPolicy Bypass -File "打包安装程序.ps1"
#    (add -SkipExeBuild to package an already-built EXE)
```

Building the *embedded-FFmpeg* EXE needs `tools\ffmpeg.exe` present (it is git-ignored because of its size). Packaging the installer additionally needs **Inno Setup 6** (`ISCC.exe`).

## Publish an update

```powershell
# Reads the newest EXE in dist\, writes updates\latest.json, and pushes both to a GitHub Release
powershell -ExecutionPolicy Bypass -File "发布更新到GitHub.ps1"
```

The script (via the `gh` CLI, which must be logged in) refuses to publish if the EXE's file version doesn't match the source `AppInfo.Version`, computes the asset's SHA-256, and uploads `latest.json` describing the release. Installed copies of the app read that manifest from `releases/latest/download/latest.json` and offer the update. The tag it publishes is `v{version}` (e.g. `v1.0.8.0`). A full installer may coexist in the same Release; the updater downloads only the raw EXE named by `latest.json`.

## Requirements at a glance

| Task | Needs |
| --- | --- |
| Run from source | Windows, .NET Framework 4.x, PowerShell 5.1 |
| Build embedded EXE | Above + `tools\ffmpeg.exe` |
| Package installer | Above + Inno Setup 6 |
| Publish update | GitHub CLI (`gh auth login`) with push rights to the repo |

## Licensing & first-run gates

On first launch the app requires the user to accept a **disclaimer** and then enter a valid **authorization key** (verified by `LicenseManager`). License/disclaimer state is stored under `%LocalAppData%\VideoRenamer` and intentionally survives uninstall so users don't have to re-activate after an upgrade. The key generator (`生成授权密钥工具.ps1`) is developer-only and excluded from git — do not ship it to end users.

## Conventions for contributors

- **C# 5 only.** No `using static`, string interpolation (`$"..."`), expression-bodied members (`=>`), `?.`, `nameof`, or tuple deconstruction — the old `csc.exe` compiles all of `src/`. Match the existing C# 5 style.
- Any `.ps1` / `.iss` / `.bat` containing Chinese must be saved **UTF-8 with BOM**; `.cs` files stay UTF-8 without BOM.
- Gate every change: `-SelfTest` must print `SelfTest OK`; UI changes must also pass `-SmokeTest`.
- One logical change per commit, prefixed `feat:` / `perf:` / `refactor:` / `build:`.

## Author

Built by **@寒松**. See [docs/REFACTOR_PROGRESS.md](docs/REFACTOR_PROGRESS.md) for the phase-by-phase modernization log and [docs/HEALTH_ASSESSMENT.md](docs/HEALTH_ASSESSMENT.md) for the current evidence-backed state of the project.
