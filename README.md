# VideoRenamer · Video Material Shot-List Renamer

A Windows desktop tool for batch-renaming raw video footage into a consistent, shot-list–based naming scheme such as `E1-S2-28A-T1.mp4`. It provides a spreadsheet-style shot list, cross-folder conflict checking, automatic conflict-safe numbering, optional 1080p export, undo history, and a built-in auto-updater.

| | |
| --- | --- |
| **Version** | V1.0.11.0 |
| **Status** | Local EXE built and verified on 2026-08-29 |
| **Platform** | Windows · .NET Framework 4.x · WinForms |
| **Language** | C# 5 (compiled with `csc.exe` / `Add-Type`) |
| **Repository** | https://github.com/Lury-Liu/VideoRenamer |

> The repository contains both application source and release tooling. The local update manifest is `updates/latest.json`; online Release availability must be checked separately before presenting this build as a public release.

---

## What it does

The tool is designed for film and short-video material where each shot can have multiple main and backup clips. Set the episode and scene values, fill in the shot list, and drag footage into the corresponding rows. The preview shows the exact target name for every clip and flags missing sources, duplicate names, existing targets, and locked files before anything is written.

The key usability improvement is that users do **not** need to remember the next take number manually. The app can inspect a selected comparison folder, choose a selected output folder, and automatically advance a conflicting take number while staying within the safe `T1`–`T100` range.

## Naming scheme

```text
E{episode}-S{scene}-{shot}{suffix}-{tail}{extension}
```

| Segment | Meaning | Example |
| --- | --- | --- |
| `E{episode}` | Episode number from the toolbar | `E1` |
| `S{scene}` | Shared default scene or per-row scene | `S2` |
| `{shot}{suffix}` | Shot number plus optional 1–2 letter suffix | `28`, `28A` |
| `{tail}` | Take segment, normally `T1`, `T2`, … | `T1` |
| `{extension}` | Original extension, lower-cased by default | `.mp4` |

Concrete example:

```text
E1-S2-28A-T1.mp4
```

Rules:

- The shot suffix is normalized to uppercase, so `28a` becomes `28A`.
- Default take numbering starts at `T1` for each row and advances through the main clips before the backup clips.
- A custom tail is allowed when a text label is more useful than a plain `T` number.
- Custom conflict numbering uses an underscore separator: `补手机`, `补手机_2`, `补手机_3`.
- File-name extension case can be preserved or normalized to lower case.

## Cross-folder comparison and automatic conflict resolution

Use the directory controls above the preview when working with an already-organized folder and a newly downloaded folder:

1. **Comparison folder** — select the folder that contains previously named material. The app reads file names from its first level, case-insensitively, and treats a matching target name as a conflict. Subdirectories are intentionally not scanned in the current version.
2. **Output folder** — optionally select where all new target files should be written. If it is empty, each source file is renamed in its own directory.
3. **Conflict auto-increment** — enable the option when a conflict should be resolved automatically.

The planner checks all of the following before execution:

- a file already exists at the target path;
- another row in the current batch produces the same target path;
- the comparison folder already contains the target file name;
- the target is locked or otherwise unavailable.

When auto-increment is enabled:

```text
T1  →  T2  →  T3  →  …  →  T100
```

For a custom tail:

```text
补手机  →  补手机_2  →  补手机_3
```

If every allowed candidate is occupied, the preview remains blocked and the app will not overwrite an unrelated file. This means the user mainly needs to know the correct **E**, **S**, and shot number; the software handles take numbering and ordinary name collisions.

## Typical workflow

1. Set the episode (`E`) and either a shared scene (`S`) or per-row scene values.
2. Enter or confirm each row's shot number and optional letter suffix.
3. Drag main and backup material into the row.
4. Optionally choose a comparison folder and an output folder.
5. Turn on conflict auto-increment when the batch should automatically move to the next available take number.
6. Review the preview statuses and target paths.
7. Execute rename, or choose export-only when only a 1080p copy is needed.
8. Use the history panel to undo a completed rename batch when necessary.

## Export behavior

- **Rename in place:** with no output directory, the target stays in the source directory.
- **Rename to output directory:** with an output directory, all target files are written there and successful external outputs are reflected in the row model.
- **1080p export with rename:** the planned target name is used for the exported file.
- **Export-only:** the original file name is retained; the app can overwrite in place or create a `_1080p` copy.
- Export progress is shown at batch level and supports cancellation. A cancelled or failed export does not discard the source file.

## Features

- Spreadsheet-style shot list with natural-order sorting and drag-and-drop import.
- Live rename preview grouped by shot, with statuses including 就绪, 未变化, 目标已存在, 新文件名重复, 源文件丢失 and 目标文件被占用.
- Case-insensitive conflict detection across the output path, current plan and selected comparison folder.
- Safe automatic take numbering capped at `T100`.
- Embedded FFmpeg in the distributable EXE for metadata reading and 1080p export.
- Standalone export-only action.
- Rename history persisted to `rename_history.tsv`.
- Warm-paper, eye-care and dark-theme UI options.
- First-run disclaimer gate and authorization-key license gate.
- GitHub update checking with SHA-256 verification.
- Built-in self-test and smoke-test entry points.

## Project structure

```text
VideoRenamer/
├─ src/
│  ├─ App/          WinForms shell and UI orchestration
│  ├─ Core/         Pure naming, planning, models and file-system abstractions
│  ├─ Media/        FFmpeg lookup, metadata reading and video export
│  ├─ Services/     Licensing, updates, logging and disclaimer services
│  └─ Tests/        Compiled-in self-test cases
├─ scripts/         Build gates, artifact verification and UI capture helpers
├─ docs/            Project health assessment
├─ assets/          App icon
├─ tools/           Local ffmpeg.exe input (git-ignored)
├─ dist/            Build output (git-ignored)
├─ updates/         Local update manifest and release asset (git-ignored)
├─ AGENTS.md        Developer constraints and build contract
├─ CHANGELOG.md     Version history
├─ PROJECT_STATUS.md Current status and known limits
└─ handoff.md       Current implementation handoff
```

The naming engine is isolated in the pure static class `RenamePlanBuilder` under `src/Core/Naming`. UI wiring lives in the `MaterialRenamerForm` partial classes. The build scripts enforce version consistency, status-text ownership, Core/Services purity, palette ownership, shadow-csproj parity and artifact validity.

## Build and run from source

Requires Windows with .NET Framework 4.x (`csc.exe`) and PowerShell 5.1.

```powershell
# Run from source (does not embed FFmpeg)
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1"

# Logic regression gate — must print SelfTest OK
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SelfTest

# UI smoke gate — must print SmokeTest OK
powershell -ExecutionPolicy Bypass -File "VideoRenamer.ps1" -SmokeTest
```

The source loader intentionally does not contain FFmpeg. Use the built EXE for media metadata and export functionality.

## Build a portable distributable

```powershell
# Build dist\VideoRenamer.exe; embeds tools\ffmpeg.exe and assets\app.ico
powershell -ExecutionPolicy Bypass -File "构建EXE.ps1"

```

The distributable is the portable single-file EXE `dist\VideoRenamer.exe`. Building it requires `tools\ffmpeg.exe`, which is intentionally git-ignored. Keep the EXE in a directory where the current user has write permission so GitHub automatic updates can replace it in place.

## Publish an update

```powershell
powershell -ExecutionPolicy Bypass -File "发布更新到GitHub.ps1"
```

The release script verifies that the EXE version matches `AppInfo.Version`, computes SHA-256, creates the `v{version}` tag/asset naming convention, and writes `updates\latest.json`. It requires an authenticated GitHub CLI with push rights. Do not describe a build as publicly released until the GitHub Release itself has been confirmed.

## Requirements at a glance

| Task | Needs |
| --- | --- |
| Run from source | Windows, .NET Framework 4.x, PowerShell 5.1 |
| Build embedded EXE | Above + `tools\ffmpeg.exe` |
| Publish update | GitHub CLI (`gh auth login`) and repository push rights |

## Licensing and first-run gates

On first launch the app requires acceptance of the disclaimer and a valid authorization key. License/disclaimer state is stored under `%LocalAppData%\VideoRenamer` so an upgrade does not require re-activation. The key generator `生成授权密钥工具.ps1` is developer-only and must never ship to end users.

## Known limits

- The comparison folder scan is first-level only; it does not recurse into subdirectories.
- The app does not infer a real shot number from video content. Users still need to provide or confirm E, S and the shot number.
- The automatic numeric take range is deliberately limited to `T1`–`T100`.
- The local source loader has no embedded FFmpeg; use the built EXE for media operations.

## Documentation

- [AGENTS.md](https://github.com/Lury-Liu/VideoRenamer/blob/main/AGENTS.md) — developer build rules and frozen contracts.
- [CHANGELOG.md](https://github.com/Lury-Liu/VideoRenamer/blob/main/CHANGELOG.md) — version history.
- [PROJECT_STATUS.md](https://github.com/Lury-Liu/VideoRenamer/blob/main/PROJECT_STATUS.md) — current status and open items.
- [docs/HEALTH_ASSESSMENT.md](https://github.com/Lury-Liu/VideoRenamer/blob/main/docs/HEALTH_ASSESSMENT.md) — health and quality assessment.
- [handoff.md](https://github.com/Lury-Liu/VideoRenamer/blob/main/handoff.md) — implementation handoff for the next maintainer or coding session.

## Author

Built by **@寒松**.