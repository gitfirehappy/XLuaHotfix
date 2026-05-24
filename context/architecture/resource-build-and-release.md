# Resource Build And Release

Last reviewed: 2026-05-23

## Scope

This document covers the verified build-time and release-time resource pipeline that exists today.

## Exported Build Data

The build pipeline exports several data assets used later by runtime loading and hotfix logic.

Current source locations:

- Release orchestration shared entry points: `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/`
- Catalog-backed release backend and export helpers: `Assets/FYAsset/Scripts/Build/Release/Editor/Addressables/`
- AB release backend: `Assets/FYAsset/Scripts/Build/Release/Editor/AB/`
- Runtime-readable manifest models: `Assets/FYAsset/Scripts/Runtime/Manifests/`
- Lua routing data model: `Assets/XLuaFramework/Scripts/XLuaLoader/LuaScriptsIndex.cs`
- Build bootstrap model: `Assets/FYAsset/Scripts/Build/Bootstrap/`
- Artifact diff model: `Assets/FYAsset/Scripts/Build/Snapshots/`
- Build repository model and filesystem implementation: `Assets/FYAsset/Scripts/Build/Repository/`
- Version data: `Assets/FYAsset/Scripts/Build/Versioning/`

| Data | Role |
| --- | --- |
| `BuildIndexData` | packaged build identity, version, and GUID used for major-version validation |
| `AAManifest` | version plus bundle hash/CRC/size mapping for AA hotfix comparison and fast bundle verification; also embeds the AA asset index lists; emitted as JSON and binary by default |
| `ABManifest` | AB asset/bundle manifest; emitted as JSON and binary by default for package output and StreamingAssets bootstrap |
| `LuaScriptsIndex` | Lua module name to Addressables key mapping; normal Addressable asset in the `LuaScripts` group; type lives with `XLuaLoader` |
| `PackageIndex` | remote package pointer written to `PackageIndex.json` and used to locate the latest package root |

## Build Repository And Differential System

The hotfix build flow relies on repository HEAD comparison instead of manual group maintenance.

### Core pieces

- `FileBuildRepository` stores JSON commits under project-root `BuildData/Snapshots/{BuildTarget}[-Channel]/{AA|AB}/`
- `RepositoryHeadState` stores only `HeadVersion`; the object path is derived as `objects/{HeadVersion}.json`
- `RepositoryCommit` stores version, channel key, backend mode, build target, package name, UTC creation time, artifact digests, `GitCommitHash`, `IsDirty`, and `PackageRootDir`
- `ArtifactDigest` stores artifact name, hash, size, and CRC for diffing; it is JSON-serializable and is not binary-serialized
- `ArtifactDelta` represents Added / Modified / Removed artifact sets
- `ArtifactDiffer` performs pure name/hash diffing with no Unity API side effects
- `AddressableSourceArtifactScanner` scans AA source assets before build at asset GUID granularity and computes shallow composite content identity from the main asset file plus its `.meta` file
- `AbBundleOutputArtifactScanner` scans AB bundle outputs after build at bundle-name granularity; when created from `ABManifest.BundleEntries`, it reuses manifest hash/CRC/size without recomputing file hashes
- `BuildProjectManager` commits AA source digests or AB output bundle digests after a successful build
- AA and AB repository spaces are isolated by the backend segment in the channel key
- `DifferentialProcessor` compares current AA artifact digests against the repository HEAD commit
- changed AA assets are reassigned into hotfix groups by `LegacyAddressableHotfixGroups`
- `LegacyAddressableHotfixGroups` writes `Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json` and blocks another hotfix prepare while pending moves exist
- `ConfirmReleaseHotfix` is a placeholder for future release/push work and does not mutate repository HEAD
- `BuildRepositoryCLI` exposes `Status`, `Diff`, `Push`, and `ListCommits`; `LocalDirectoryPushTarget` is the only Plan 3 push target implementation
- `PushHistory.json` is written by the repository at `BuildData/Snapshots/{BuildTarget}[-Channel]/{BackendMode}/PushHistory.json` after a successful push, while the target directory itself only receives delta bundles, `ABManifest.json`, and `PackageIndex.json`
- `RepositoryStatusPanel` exposes repository status and read-only Diff Preview in the build pipeline window
- AB Diff Preview uses `DAGScheduler.Execute` with a stop-after task and whitelist, writes temporary outputs under `Temp/BuildRepositoryPreview/{guid}/`, and deletes that directory in a `finally` path

## Release Operations

`BuildProjectManager` exposes the main release operations and now acts as an orchestrator over two build backends.

### `BuildFullPackage`

- increments the major version
- requires hotfix groups to be reset to original grouping first
- represents a packaged baseline refresh
- always goes through `BuildProjectManager.CreateBackend()`
- runs shared post-build steps in the orchestrator: package manifest update and repository commit; full-build local bootstrap export is task-managed

### `BuildHotfix`

- increments the patch version
- relies on `DifferentialProcessor` to detect changed assets automatically
- produces incremental content for hotfix distribution
- uses `DifferentialProcessor.PrepareHotfix()` only on the AA backend path
- routes actual package generation through the backend selected by `FYAssetSettings.Instance.UseABBackend`

### `ConfirmRelease`

- currently logs a placeholder because release/push is deferred
- does not update repository HEAD or build artifacts

### `ResetGroupsToOriginal`

- restores assets from hotfix groups back to their original groups
- is a prerequisite for correct full-package publishing
- remains a AA-only operation; AB backend mode logs and skips it

## Build Backend Split

The build entry point is now split with the same orchestration pattern already used by runtime hotfix and asset loading.

### Shared orchestrator

- `BuildProjectManager` owns version increment, Lua index export, `BuildPackageRequest` creation, package naming, `PackageIndex.json` update, and repository commit
- `BuildPackageRequest` is created before backend execution and carries version, build type, backend mode, package name, final package output directory, bundles directory, and `PackageIndex` path
- `BuildContextKeys.BuildType` is written by each backend before DAG execution so shared tail tasks can preserve full-build-only behavior without reading global state
- `BuildCommandLine` still calls `BuildProjectManager.BuildFullPackage()` / `BuildHotfix()` and does not bypass backend selection
- backend selection is centralized in `BuildProjectManager.CreateBackend()` using `FYAssetSettings.Instance.UseABBackend`
- `IBuildBackend` exposes only `BuildAsync(BuildPackageRequest, BuildExecutionOptions)`; output organization and manifest publication are not backend post-build API methods
- Task graph assets are the backbone source of truth. `BuildPipelineBackbone` supplies default task entries for new config creation plus validation/UI metadata, but existing `BuildPipelineConfig` assets are not auto-repaired during load or build execution.

### AA backend

- `AABuildBackend` is a stateless DAG runner: it receives the shared `BuildPackageRequest`, writes it into `BuildContext`, loads `FYAssetSettings.Instance.AAPipelineConfigPath`, and runs the AA task graph through `DAGScheduler.Execute()`
- `TaskBuildAddressablesContent` owns Addressables-specific setup (`BuildRemoteCatalog`, `PackTogetherByLabel`, LuaScripts remote path fix), ServerData cleanup, and `AddressableAssetSettings.BuildPlayerContent`
- `TaskOrganizeAAOutput` copies ServerData output into the request-owned final package directory and sets `BuildContextKeys.OutputPath` to `BuildPackageRequest.OutputDir`
- `TaskWriteAAPackageManifest` exports `AAManifest.json` and `AAManifest.bin` by default by scanning `{PackageRoot}/bundles/*.bundle`
- `TaskExportLocalBuildData` is the AA graph tail task and implementation owner for local startup data export. It writes `BuildIndexData` only for full builds, cleans stale AB baseline manifests from `StreamingAssets`, and returns success without exporting for hotfix builds. AA baseline file copying remains handled by the existing player build flow.
- each exported `BundleInfo` stores `FileHash` (MD5 content identity), `FileCRC` (CRC32 fast verification), and `FileSize`
- `AAManifest` also stores `AssetEntries`, `KeysByType`, and `KeysByLabel`
- `AAAssetIndexBuilder` is the single Editor-only source for those AA index lists and writes them into `AAManifest`

### AB backend

- `ABBuildBackend` is a stateless DAG runner: it receives the shared `BuildPackageRequest`, writes it into `BuildContext`, and runs the AB task graph through `DAGScheduler.Execute()`
- `TaskOrganizeOutput` consumes the request and writes the final AB package layout directly under `BuildPackageRequest.OutputDir`, copying bundles into `BuildPackageRequest.BundlesDir`
- `TaskWriteABPackageManifest` publishes `ABManifest.json` and/or `ABManifest.bin` at the final package root according to `FYAssetSettings.ManifestOutputFormat` and applies `HotfixPackageSizeGuard`
- `TaskExportLocalBuildData` is the AB graph tail task and implementation owner for local startup data export. It writes `BuildIndexData` only for full builds, copies the real final AB package baseline (`ABManifest` files plus `bundles/`) into `StreamingAssets`, cleans stale AA baseline files, and returns success without exporting for hotfix builds
- `BuildContextKeys.OutputPath` is the request-owned final package directory after AB finalization
- missing manifest-listed bundle files during AB finalization fail the task instead of being silently skipped

### Build path helpers

- `BuildPathManager` is the Editor-only source for build output paths. It reads `FYAssetSettings.BuildOutputRoot` and `BuildPackagesFolderName`; the default layout remains `HotfixOutput/Packages/Build_{yyyyMMddHHmmss}_{version}`.
- `AddressablesBuildOutputOrganizer` owns AA `ServerData` cleanup and package output copying rules.

## FYAssetSettings

`FYAssetSettings` is a `ScriptableObject` (Runtime assembly) that replaced the deleted `FYAssetConstants` static class.

- Asset path: `Assets/Resources/FYAssetSettings.asset` (versioned in repo; `LoadOrCreate()` creates it there on the Editor path if missing)
- Singleton access: `FYAssetSettings.Instance`
- Editor: `LoadOrCreate()` searches `Assets/Resources/FYAssetSettings.asset`, creates the asset if missing, and saves it through `AssetDatabase`
- Player: `LoadOrCreate()` loads the asset through `Resources.Load<FYAssetSettings>("FYAssetSettings")`; only if that fails does it fall back to `CreateInstance<FYAssetSettings>()`
- Instance fields (configurable in Inspector): `ProjectName`, `HotfixUrl`, `UseABBackend`, `MaxHotfixSizeBytes`, `HotfixMaxRetryCount`, `HotfixRetryBaseDelaySeconds`, `ManifestOutputFormat`, `VersionDataBasePath`, `LuaScriptsIndexPath`, `BuildIndexJsonPath`, `BuildOutputRoot`, `BuildPackagesFolderName`, `CollectorDataFolder`, `CollectorSettingPath`, `PipelineConfigPath`
- `AAPipelineConfigPath` points to the AA task graph asset. The existing `PipelineConfigPath` remains the AB graph path.
- `ManifestOutputFormat.JsonAndBinary` is the release-safe default. `BinaryOnly` exists as an option but is not the formal release default.
- Runtime consumers read configuration via `FYAssetSettings.Instance` at use sites; no `static readonly` settings snapshots remain in `RuntimePathManager` / `HotfixManager`
- Static `const` members: all rule name strings (`RULE_*`), group/label identifiers (`LUA_SCRIPTS_INDEX`, `HOTFIX_GROUP_NAME`, `DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL`), file names (`PACKAGE_INDEX_FILE_NAME`, `MANIFEST_FILE_NAME`, `MANIFEST_FILE_NAME_BIN`, `AA_MANIFEST_FILE_NAME`, `AA_MANIFEST_FILE_NAME_BIN`, `BUILD_INDEX_FILENAME`), and editor paths (`BUILD_PIPELINE_WINDOW_MENU_PATH`, `BINARY_SERIALIZER_GENERATE_PATH`)
- `UseABBackend` is the single source of truth for backend selection — `BuildPipelineConfig.DefaultBackendMode` was removed
- `BackendMode.AA` is the canonical AA enum value; duplicate AA/Addressables mode names are not supported.

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

