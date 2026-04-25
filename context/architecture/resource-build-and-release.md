# Resource Build And Release

Last reviewed: 2026-04-25

## Scope

This document covers the verified build-time and release-time resource pipeline that exists today.

## Exported Build Data

The build pipeline exports several data assets used later by runtime loading and hotfix logic.

| Data | Role |
| --- | --- |
| `BuildIndexData` | packaged build identity, version, and GUID used for major-version validation |
| `VersionState` | version plus bundle hash/size mapping for hotfix comparison |
| `AddressableLabelsConfig` | Legacy runtime index from type/label to resource keys |
| `LuaScriptsIndex` | Lua module name to Addressables key mapping |
| `Manifest` | remote package pointer used to locate the latest package root |

## Differential Snapshot System

The hotfix build flow relies on snapshot comparison instead of manual group maintenance.

### Core pieces

- `BuildSnapshots` stores at least `Head` and `Staged` states
- `DifferentialProcessor` compares current asset hashes against the `Head` snapshot
- changed assets are reassigned into hotfix groups automatically
- snapshot promotion rotates `Staged` into `Head`
- full-package preparation restores original grouping before a major build

## Release Operations

`BuildProjectManager` exposes the main release operations.

### `BuildFullPackage`

- increments the major version
- requires hotfix groups to be reset to original grouping first
- represents a packaged baseline refresh

### `BuildHotfix`

- increments the patch version
- relies on `DifferentialProcessor` to detect changed assets automatically
- produces incremental content for hotfix distribution

### `ConfirmRelease`

- promotes the staged snapshot into the published head snapshot
- should be called only after a release is accepted as the new baseline

### `ResetGroupsToOriginal`

- restores assets from hotfix groups back to their original groups
- is a prerequisite for correct full-package publishing

## Build-Time Architectural Decisions

### Automatic hotfix grouping

The project intentionally avoids manual hotfix group maintenance.

Reason:

- human-maintained grouping is fragile
- snapshot diffing makes incremental packaging deterministic

Tradeoff:

- the build process depends on a snapshot state machine that must stay consistent

### Metadata-first runtime bootstrapping

Runtime and hotfix code rely on exported metadata rather than scanning assets dynamically.

Reason:

- startup code needs stable indices and package identity
- Lua loading needs a prebuilt module index

Tradeoff:

- exported metadata is a required build artifact, not an optional optimization

## Relationship To The Collector Refactor

The collector framework under `Assets/FYAsset/Scripts/Build/Collector/` is the current foundation for a future build-pipeline refactor. It already defines the configuration model and rule contracts, but it is not yet the only build system in the repository.

See `collector-framework.md` for the verified current scope.
