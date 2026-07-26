# Changelog

## V1.0.8.0 — 2026-07-26

### Changed

- Migrated the complete application identity to `VideoRenamer`, including namespaces, assembly/product metadata, executable and installer names, application-data paths, resources, launchers, build scripts, and update `appId`.
- Added modular startup-icon rotation across all nine ICO assets while retaining a fixed executable icon.
- Updated release packaging to provide both a full installer and the raw executable selected by `latest.json`.

### Fixed

- Cleared cached hover-preview frames when the global material list is emptied.
- Aligned the numbered workspace badges along the left edge.
- Prevented corrupted splash images by decoding the largest embedded PNG frame from ICO files.
- Replaced the unavailable `Get-FileHash` dependency in the publishing flow with a compatible .NET SHA-256 implementation.

### Security

- Update manifests must match the exact `VideoRenamer` app identity.
- Update manifests without a valid 64-character SHA-256 value are rejected.
- Downloaded updates are always hash-verified before replacement or restart.

### Upgrade note

The old application identity and its local activation/settings storage are intentionally not migrated. Uninstall the previous build and perform a clean V1.0.8.0 installation.
