# Project Status — VideoRenamer

| | |
| --- | --- |
| **Source version** | **V1.0.8.0** |
| **Published release** | **v1.0.8.0** from `main` |
| **Branch policy** | `main` is the only remote branch |
| **Status generated** | 2026-07-26 |
| **Overall state** | Modernization V2+V3 complete · VideoRenamer identity migration complete · release verified |

## Current release

V1.0.8.0 completes the project-wide migration to the `VideoRenamer` identity and keeps it centralized across namespaces, assembly metadata, executable/installer names, application-data paths, resources, update manifests, build scripts, and launchers. The prior installed identity is intentionally not migrated; a clean installation is recommended for this release.

The fixed executable icon remains `assets\app.ico`. Nine ICO-only startup images under `assets\startup-icons` rotate between application sessions through separate state, service, and theme modules, without growing the main entry point. The startup renderer extracts the largest embedded PNG frame from each ICO to avoid the previously corrupted splash image.

## V1.0.8.0 changes

- Fixed stale hover-scrub frames remaining visible after **global clear**.
- Aligned the numbered workspace badges on the left edge.
- Added session-to-session rotation for all nine startup ICOs while keeping the EXE icon fixed.
- Migrated the global application name and durable paths to `VideoRenamer`.
- Hardened updates: exact `appId` matching, mandatory 64-character SHA-256, and post-download hash enforcement before replacement.
- Made release SHA-256 generation compatible with the available Windows PowerShell runtime.

## Verification state

| Gate | Result |
| --- | --- |
| SelfTest | ✅ 77/77 → `SelfTest OK` |
| SmokeTest | ✅ `SmokeTest OK` |
| Source/architecture gates | ✅ version, app identity, status ownership, layer purity, palette ownership, csproj parity |
| Package build | ✅ `dist\VideoRenamer.exe` + `installer\VideoRenamer-Setup-v1.0.8.0.exe` |
| verify-artifact | ✅ version/resource/name/layout checks PASS |
| Publish rehearsal | ✅ `发布更新到GitHub.ps1 -DryRun` |

## Release assets

- `VideoRenamer-Setup-v1.0.8.0.exe` — full installer for users.
- `VideoRenamer-v1.0.8.0.exe` — raw executable consumed by the automatic updater.
- `latest.json` — hash-pinned update manifest; its `fileName` selects the raw executable even when the installer coexists in the Release.

## Remaining manual checks

1. Compare both themes at 100%, 125%, and 150% display scaling.
2. Run a real rename/export/cancel/undo workflow with representative footage.
3. Rehearse the Program Files update path for both accepted and declined UAC prompts.
4. Validate the shadow SDK-style project on a machine with a current .NET SDK and add CI when that toolchain is adopted.

## Detail

- [README.md](README.md) — product, build, packaging, and update instructions.
- [CHANGELOG.md](CHANGELOG.md) — release notes.
- [docs/REFACTOR_PROGRESS.md](docs/REFACTOR_PROGRESS.md) — phase-by-phase modernization history.
- [docs/HEALTH_ASSESSMENT.md](docs/HEALTH_ASSESSMENT.md) — evidence-backed architecture and quality assessment.
