# Resource Build And Release

Last reviewed: 2026-05-20

## Scope

This document covers the verified build-time and release-time resource pipeline that exists today.

## Exported Build Data

The build pipeline exports several data assets used later by runtime loading and hotfix logic.

Current source locations:

- Release orchestration shared entry points: `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/`
- AA Addressables release backend and AA export helpers: `Assets/FYAsset/Scripts/Build/Release/Editor/Addressables/`
- AB release backend: `Assets/FYAsset/Scripts/Build/Release/Editor/AB/`
- Runtime-readable manifest models: `Assets/FYAsset/Scripts/Runtime/Manifests/`
- Lua routing data model: `Assets/XLuaFramework/Scripts/XLuaLoader/LuaScriptsIndex.cs`
- Build bootstrap model/exporter: `Assets/FYAsset/Scripts/Build/Bootstrap/`
- Snapshot model/processor: `Assets/FYAsset/Scripts/Build/Snapshots/`
- Version data: `Assets/FYAsset/Scripts/Build/Versioning/`

| Data | Role |
| --- | --- |
| `BuildIndexData` | packaged build identity, version, and GUID used for major-version validation |
| `AAManifest` | version plus bundle hash/CRC/size mapping for AA hotfix comparison and fast bundle verification; also embeds the AA asset index lists; emitted as JSON and binary by default |
| `ABManifest` | AB asset/bundle manifest; emitted as JSON and binary by default for package output and StreamingAssets bootstrap |
| `LuaScriptsIndex` | Lua module name to Addressables key mapping; normal Addressable asset in the `LuaScripts` group; type lives with `XLuaLoader` |
| `PackageIndex` | remote package pointer written to `manifest.json` and used to locate the latest package root |

## Differential Snapshot System

The hotfix build flow relies on snapshot comparison instead of manual group maintenance.

### Core pieces

- `BuildSnapshots` stores at least `Head` and `Staged` states
- `DifferentialProcessor` compares current asset hashes against the `Head` snapshot
- changed assets are reassigned into hotfix groups automatically
- snapshot promotion rotates `Staged` into `Head`
- full-package preparation restores original grouping before a major build

## Release Operations

`BuildProjectManager` exposes the main release operations and now acts as an orchestrator over two build backends.

### `BuildFullPackage`

- increments the major version
- requires hotfix groups to be reset to original grouping first
- represents a packaged baseline refresh
- always goes through `BuildProjectManager.CreateBackend()`
- runs shared post-build steps in the orchestrator: package manifest update, `LocalStatusExporter.ExportData`, and snapshot rebuild

### `BuildHotfix`

- increments the patch version
- relies on `DifferentialProcessor` to detect changed assets automatically
- produces incremental content for hotfix distribution
- uses `DifferentialProcessor.PrepareHotfix()` only on the AA Addressables backend path
- routes actual package generation through the backend selected by `FYAssetSettings.Instance.UseABBackend`

### `ConfirmRelease`

- promotes the staged snapshot into the published head snapshot
- should be called only after a release is accepted as the new baseline
- remains a AA-only operation; AB backend mode logs and skips it

### `ResetGroupsToOriginal`

- restores assets from hotfix groups back to their original groups
- is a prerequisite for correct full-package publishing
- remains a AA-only operation; AB backend mode logs and skips it

## Build Backend Split

The build entry point is now split with the same orchestration pattern already used by runtime hotfix and asset loading.

### Shared orchestrator

- `BuildProjectManager` owns version increment, Lua index export, `BuildPackageRequest` creation, package naming, `manifest.json` (`PackageIndex`) update, and full-build post steps
- `BuildPackageRequest` is created before backend execution and carries version, build type, backend mode, package name, final package output directory, bundles directory, and `PackageIndex` path
- `BuildCommandLine` still calls `BuildProjectManager.BuildFullPackage()` / `BuildHotfix()` and does not bypass backend selection
- backend selection is centralized in `BuildProjectManager.CreateBackend()` using `FYAssetSettings.Instance.UseABBackend`

### AA backend

- `AAAddressableBuildBackend` receives the shared `BuildPackageRequest` and owns Addressables-specific setup (`BuildRemoteCatalog`, `PackTogetherByLabel`, LuaScripts remote path fix)
- it still builds through `AddressableAssetSettings.BuildPlayerContent`
- it exports `AAManifest.json` and `AAManifest.bin` by default by scanning `{PackageRoot}/bundles/*.bundle`
- each exported `BundleInfo` stores `FileHash` (MD5 content identity), `FileCRC` (CRC32 fast verification), and `FileSize`
- `AAManifest` also stores `AssetEntries`, `KeysByType`, and `KeysByLabel`
- `AAAssetIndexBuilder` is the single Editor-only source for those AA index lists and writes them into `AAManifest`

### AB backend

- `ABBuildBackend` receives the shared `BuildPackageRequest`, writes it into `BuildContext`, and runs the already-landed E5/E6 task graph through `DAGScheduler.Execute()`
- it consumes `ABManifest` and `OutputPath` produced by the pipeline tasks
- it reorganizes package output into `{PackageRoot}/bundles/` so the runtime contracts stay aligned with `HotfixManager` download layout and `ABBundleLoader` lookup rules
- it exports `ABManifest.json` and `ABManifest.bin` by default at the package root as the AB-side version descriptor

### Build path helpers

- `BuildPathManager` is the Editor-only source for build output paths; it preserves the current `HotfixOutput/Packages/Build_{date}_{version}` layout.
- `AddressablesBuildOutputOrganizer` owns AA Addressables `ServerData` cleanup and package output copying rules.

## FYAssetSettings

`FYAssetSettings` is a `ScriptableObject` (Runtime assembly) that replaced the deleted `FYAssetConstants` static class.

- Asset path: `Assets/Resources/FYAssetSettings.asset` (versioned in repo; `LoadOrCreate()` creates it there on the Editor path if missing)
- Singleton access: `FYAssetSettings.Instance`
- Editor: `LoadOrCreate()` searches `Assets/Resources/FYAssetSettings.asset`, creates the asset if missing, and saves it through `AssetDatabase`
- Player: `LoadOrCreate()` loads the asset through `Resources.Load<FYAssetSettings>("FYAssetSettings")`; only if that fails does it fall back to `CreateInstance<FYAssetSettings>()`
- Instance fields (configurable in Inspector): `ProjectName`, `HotfixUrl`, `UseABBackend`, `MaxHotfixSizeBytes`, `HotfixMaxRetryCount`, `HotfixRetryBaseDelaySeconds`, `ManifestOutputFormat`, `VersionDataBasePath`, `LuaScriptsIndexPath`, `SnapshotAssetPath`, `BuildIndexJsonPath`, `CollectorDataFolder`, `CollectorSettingPath`, `PipelineConfigPath`
- `ManifestOutputFormat.JsonAndBinary` is the release-safe default. `BinaryOnly` exists as an option but is not the formal release default.
- Runtime consumers read configuration via `FYAssetSettings.Instance` at use sites; no `static readonly` settings snapshots remain in `RuntimePathManager` / `HotfixManager`
- Static `const` members: all rule name strings (`RULE_*`), group/label identifiers (`LUA_SCRIPTS_INDEX`, `HOTFIX_GROUP_NAME`, `DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL`), file names (`MANIFEST_FILE_NAME`, `MANIFEST_FILE_NAME_BIN`, `AA_MANIFEST_FILE_NAME`, `AA_MANIFEST_FILE_NAME_BIN`, `BUILD_INDEX_FILENAME`), and editor paths (`BUILD_PIPELINE_WINDOW_MENU_PATH`, `BINARY_SERIALIZER_GENERATE_PATH`)
- `UseABBackend` is the single source of truth for backend selection — `BuildPipelineConfig.DefaultBackendMode` was removed

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

