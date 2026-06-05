# Resource Build And Release

Last reviewed: 2026-06-05

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
- Build settings data: `Assets/FYAsset/Scripts/Build/Settings/`

| Data | Role |
| --- | --- |
| `BuildIndexData` | packaged build identity, version, backend mode, and GUID used for major-version validation |
| `AAManifest` | version plus bundle hash/CRC/size mapping for AA hotfix comparison and fast bundle verification; also embeds the AA asset index lists; emitted as JSON and binary by default |
| `ABManifest` | AB asset/bundle manifest; emitted as JSON and binary by default for package output and StreamingAssets bootstrap |
| `LuaScriptsIndex` | Lua module name to Addressables key mapping; normal Addressable asset in the `LuaScripts` group; type lives with `XLuaLoader` |
| `PackageIndex` | remote package pointer written to `PackageIndex.json`; includes backend mode and latest package root |

## Build Repository And Differential System

The hotfix build flow relies on repository HEAD comparison instead of manual group maintenance.

### Core pieces

- `FileBuildRepository` stores JSON commits under project-root `BuildData/Snapshots/{BuildTarget}[-Channel]/{AA|AB}/`
- `FileBuildRepository.GetStatus()` distinguishes empty HEAD from malformed HEAD through `RepositoryStatus.HasHeadError` / `HeadErrorReason`
- `VersionDataBase` is shared as the product-version source; AA and AB are build backend dimensions, not separate product version streams
- `RepositoryHeadState` stores only `HeadVersion`; the object path is derived as `objects/{HeadVersion}.json`
- `RepositoryCommit` stores version, channel key, backend mode, build target, package name, UTC creation time, artifact digests, `GitCommitHash`, `IsDirty`, and `PackageRootDir`
- `ArtifactDigest` stores artifact name, hash, size, and CRC for diffing; it is JSON-serializable and is not binary-serialized
- `ArtifactDelta` represents Added / Modified / Removed artifact sets
- `ArtifactDiffer` performs pure name/hash diffing with no Unity API side effects
- `TaskScanAddressableHotfixDiff` scans AA source assets before build at asset GUID granularity, computes shallow composite content identity from the main asset file plus its `.meta` file, and publishes `RepositoryArtifacts` for commit
- `TaskScanABHotfixDiff` scans AB bundle outputs after build at bundle-name granularity; when fed from `ABManifest.BundleEntries`, it reuses manifest hash/CRC/size and also publishes `RepositoryArtifacts`
- `BuildProjectManager` commits AA source digests or AB output bundle digests after a successful build
- `TaskWritePackageIndex` writes `PackageIndex.BackendMode` as `AA` or `AB` in official Full and Hotfix DAG runs
- `TaskExportLocalBuildData` writes `BuildIndexData.BackendMode` as `AA` or `AB`
- AA and AB repository spaces are isolated by the backend segment in the channel key
- `TaskScanAddressableHotfixDiff` runs before AA hotfix content build, compares current AA source against repository HEAD, and writes `ArtifactDelta` into `BuildContext`
- `TaskMoveAddressableHotfixGroups` moves Added and Modified AA assets into the Hotfix group, writes `Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json`, blocks another move while pending moves exist, and keeps the manual restore path available
- `TaskScanABHotfixDiff` runs after AB bundle build verification, compares AB bundle output against repository HEAD, and writes `ArtifactDelta` into `BuildContext`
- `ConfirmReleaseHotfix` is a placeholder wrapper and does not mutate repository HEAD, build artifacts, or push targets
- `BuildRepositoryCLI` exposes `Status`, `Diff`, `Push`, and `ListCommits`; `Diff` runs the AA or AB DAG to the backend-specific diff task and stops there
- `FileBuildRepository.Push()` loads the from/to commits for either AA or AB channels, computes the changed artifact count for history display, and delegates publication to the configured `IPushTarget`
- `LocalDirectoryPushTarget` treats `PushTargetConfig.Path` as a publish root. An empty path resolves to `BuildPathManager.OutputRoot`; publication writes `{PublishRoot}/PackageIndex.json` and `{PublishRoot}/{BuildPackagesFolderName}/{PackageName}/...`
- Push writes the root `PackageIndex.json` from the target commit's package name, version, and backend mode. It does not reinterpret package-internal catalog or manifest files.
- `PushHistory.json` is written by the repository at `BuildData/Snapshots/{BuildTarget}[-Channel]/{BackendMode}/PushHistory.json` after a successful push
- `RepositoryStatusPanel` exposes repository status and read-only Diff Preview in the build pipeline window
- `RepositoryStatusPanel` is a shared Manage panel entry and is not owned by the AA or AB sidebar groups
- AB Diff Preview uses `DAGScheduler.Execute` with a stop-after task and whitelist, writes temporary outputs under `Temp/BuildRepositoryPreview/{guid}/`, and deletes that directory in a `finally` path; `TaskPrepareContext` reads the preview output root from `BuildContextKeys.RepositoryPreviewOutput` instead of an environment variable

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
- relies on the AA DAG diff/move tasks to detect changed assets automatically in AA mode
- produces incremental content for hotfix distribution
- routes actual package generation through the backend selected by `FYAssetSettings.Instance.UseABBackend`

### `ConfirmRelease`

- currently logs a placeholder and does not update repository HEAD, build artifacts, or push targets
- repository push is available through Build Repository CLI/UI rather than this wrapper

### `ResetGroupsToOriginal`

- restores assets from hotfix groups back to their original groups
- is a prerequisite for correct full-package publishing
- remains a AA-only operation; AB backend mode logs and skips it

## Build Backend Split

The build entry point is now split with the same orchestration pattern already used by runtime hotfix and asset loading.

### Shared orchestrator

- `BuildProjectManager` owns version increment, Lua index export, `BuildPackageRequest` creation, package naming, backend routing, and repository commit
- `BuildPackageRequest` is created before backend execution and carries version, build type, backend mode, package name, final package output directory, bundles directory, and `PackageIndex` path
- Runtime hotfix rejects a remote `PackageIndex` if its backend mode is missing, invalid, or different from the current `FYAssetSettings.UseABBackend` mode
- `BuildContextKeys.BuildType` is written by each backend before DAG execution so shared tail tasks can preserve full-build-only behavior without reading global state
- `BuildCommandLine` still calls `BuildProjectManager.BuildFullPackage()` / `BuildHotfix()` and does not bypass backend selection
- backend selection is centralized in `BuildProjectManager.CreateBackend()` using `FYAssetSettings.Instance.UseABBackend`
- `IBuildBackend` exposes only `BuildAsync(BuildPackageRequest, BuildExecutionOptions)`; output organization and manifest publication are not backend post-build API methods
- Task graph assets are the backbone source of truth. `BuildPipelineBackbone` supplies default task entries for new config creation plus validation/UI metadata, but existing `BuildPipelineConfig` assets are not auto-repaired during load or build execution.
- `DAGScheduler` treats `WriteKeys` as BuildContext write/update declarations, not exclusive write locks. Staged writes to the same key are valid when explicit task dependencies define the order.
- AB `CollectedAssets` is a staged key: `TaskCollectAssets` creates the list, `TaskCollectBuiltins` appends builtin shader/resources entries, and `TaskAnalyzeDependencies` writes the dependency-augmented list back.
- Diff Preview uses `DAGScheduler.Execute` with a whitelist and validates only the effective preview task set.
- Active FYAsset settings are the three runtime-safe Resources assets loaded through `FYAssetSettings`, `FYAssetAASettings`, and `FYAssetABSettings`. The Editor-only `FYAssetBuildSettingsProvider` exposes those three active assets and only reads old `SharedBuildSettings`, `AABuildSettings`, `ABBuildSettings`, and `BuildRepositorySettings` as migration sources when creating missing defaults.

### AA backend

- `AABuildBackend` is a stateless DAG runner: it receives the shared `BuildPackageRequest`, writes it into `BuildContext`, loads the pipeline config path from `FYAssetAASettings`, and runs the AA task graph through `DAGScheduler.Execute()`
- `TaskScanAddressableHotfixDiff` and `TaskMoveAddressableHotfixGroups` are AA graph front tasks. Full builds skip them; hotfix builds continue when the diff is empty and move changed assets before `TaskBuildAddressablesContent`.
- `TaskBuildAddressablesContent` owns Addressables-specific setup (`BuildRemoteCatalog`, `PackTogetherByLabel`, LuaScripts remote path fix), ServerData cleanup, and `AddressableAssetSettings.BuildPlayerContent`
- `TaskOrganizeAAOutput` copies ServerData output into the request-owned final package directory and sets `BuildContextKeys.OutputPath` to `BuildPackageRequest.OutputDir`
- `TaskWriteAAPackageManifest` exports `AAManifest.json` and `AAManifest.bin` by default by scanning `{PackageRoot}/bundles/*.bundle`
- `TaskWritePackageIndex` runs after the AA package manifest task and writes the remote latest-package pointer for both Full and Hotfix official builds
- `TaskExportLocalBuildData` is the AA graph tail task and implementation owner for local startup data export. It writes `BuildIndexData` only for full builds, copies the final `AAManifest` files into `StreamingAssets` as the AA query-index baseline, cleans stale AB baseline manifests, and returns success without exporting for hotfix builds. AA catalog and bundle placement remain handled by the existing Addressables player build flow.
- each exported `BundleInfo` stores `FileHash` (MD5 content identity), `FileCRC` (CRC32 fast verification), and `FileSize`
- `AAManifest` also stores `AssetEntries`, `KeysByType`, and `KeysByLabel`
- `AAAssetIndexBuilder` is the single Editor-only source for those AA index lists and writes them into `AAManifest`

### AB backend

- `ABBuildBackend` is a stateless DAG runner: it receives the shared `BuildPackageRequest`, writes it into `BuildContext`, and runs the AB task graph through `DAGScheduler.Execute()`
- The AB backbone order is `TaskPrepareContext -> TaskCollectAssets -> TaskCollectBuiltins -> TaskAnalyzeDependencies -> TaskBuildBundles -> TaskGenerateManifest -> TaskVerifyBuildResult -> TaskScanABHotfixDiff -> TaskOrganizeOutput -> TaskWriteABPackageManifest -> TaskWritePackageIndex -> TaskExportLocalBuildData`.
- `TaskScanABHotfixDiff` is the AB graph diff task between `TaskVerifyBuildResult` and `TaskOrganizeOutput`; standalone AB diff stops after this task so package organization and publication do not run.
- `TaskOrganizeOutput` consumes the request and writes the final AB package layout directly under `BuildPackageRequest.OutputDir`, copying bundles into `BuildPackageRequest.BundlesDir`
- `TaskWriteABPackageManifest` publishes `ABManifest.json` and/or `ABManifest.bin` at the final package root according to `FYAssetABSettings.ManifestOutputFormat` and applies the AB hotfix size limit from `FYAssetABSettings`
- `TaskWritePackageIndex` runs after the AB package manifest task and writes the remote latest-package pointer for both Full and Hotfix official builds
- `TaskExportLocalBuildData` is the AB graph tail task and implementation owner for local startup data export. It writes `BuildIndexData` only for full builds, copies the real final AB package baseline (`ABManifest` files plus `bundles/`) into `StreamingAssets`, cleans stale AA baseline files, and returns success without exporting for hotfix builds
- `BuildContextKeys.OutputPath` is the request-owned final package directory after AB finalization
- missing manifest-listed bundle files during AB finalization fail the task instead of being silently skipped

### Build path helpers

- `BuildPathManager` is the Editor-only source for build output paths. It reads `FYAssetSettings.BuildOutputRoot` and `FYAssetSettings.BuildPackagesFolderName`; the default layout remains `HotfixOutput/Packages/Build_{yyyyMMddHHmmss}_{version}`.
- `FYAssetPathUtility` is the shared helper for URL joining, local file path joining/resolution, Unity asset path normalization, relative file path calculation, and path identity comparison.
- Remote URL roots and path segments are joined with `FYAssetPathUtility.JoinUrl(...)`; local filesystem paths from settings, CLI arguments, repository publication, package output, temporary build output, and StreamingAssets export are joined or resolved with `JoinFilePath(...)` / `ResolveFilePath(...)`.
- Unity `AssetDatabase` paths remain `Assets/...` paths with `/` separators and are normalized with `NormalizeAssetPath(...)` / `JoinAssetPath(...)`. URI-like paths such as Android StreamingAssets `jar:` paths are joined with `/` and are not normalized as local OS paths.
- `AddressablesBuildOutputOrganizer` owns AA `ServerData` cleanup and package output copying rules.

## Active FYAsset Settings

`FYAssetSettings`, `FYAssetAASettings`, and `FYAssetABSettings` are runtime-safe `ScriptableObject` assets stored under `Assets/Resources/`.

### `FYAssetSettings`

Stores global project/build configuration and compile-time constants.

- Asset path: `Assets/Resources/FYAssetSettings.asset` (versioned in repo; `LoadOrCreate()` creates it there on the Editor path if missing)
- Singleton access: `FYAssetSettings.Instance`
- Editor: `LoadOrCreate()` searches `Assets/Resources/FYAssetSettings.asset`, creates the asset if missing, and saves it through `AssetDatabase`
- Player: `LoadOrCreate()` loads the asset through `Resources.Load<FYAssetSettings>("FYAssetSettings")`; only if that fails does it fall back to `CreateInstance<FYAssetSettings>()`
- Instance fields: `ProjectName`, `UseABBackend`, `BuildOutputRoot`, `BuildPackagesFolderName`, `VersionDataBasePath`, `BuildIndexJsonPath`, and `PushTargets`
- Runtime consumers read configuration through the current settings `Instance` accessors at use sites; no `static readonly` settings snapshots remain in `RuntimePathManager` / `HotfixManager`
- Static `const` members: filter/group rule name strings (`RULE_COLLECT_ALL`, `RULE_GROUP_*`), group/label identifiers (`LUA_SCRIPTS_INDEX`, `HOTFIX_GROUP_NAME`, `DEFAULT_XLUA_TYPE_CONFIG_LOAD_LABEL`), file/directory names (`PACKAGE_INDEX_FILE_NAME`, `MANIFEST_FILE_NAME`, `MANIFEST_FILE_NAME_BIN`, `AA_MANIFEST_FILE_NAME`, `AA_MANIFEST_FILE_NAME_BIN`, `BUILD_INDEX_FILENAME`, `BUNDLES_DIRECTORY_NAME`, `ADDRESSABLES_CATALOG_FILE_NAME`), and editor paths (`BUILD_PIPELINE_WINDOW_MENU_PATH`, `BINARY_SERIALIZER_GENERATE_PATH`)
- `UseABBackend` is the single source of truth for backend selection — `BuildPipelineConfig.DefaultBackendMode` was removed
- `BackendMode.AA` is the canonical AA enum value; duplicate AA/Addressables mode names are not supported.

### `FYAssetAASettings`

Stores AA backend runtime and build configuration.

- Asset path: `Assets/Resources/FYAssetAASettings.asset`
- Singleton access: `FYAssetAASettings.Instance`
- Instance fields: `HotfixUrl`, `HotfixMaxRetryCount`, `HotfixRetryBaseDelaySeconds`, `BuildPipelineConfigPath`, `ManifestOutputFormat`, `MaxHotfixSizeBytes`, and `LuaScriptsIndexPath`

### `FYAssetABSettings`

Stores AB backend runtime, build, and collection configuration.

- Asset path: `Assets/Resources/FYAssetABSettings.asset`
- Singleton access: `FYAssetABSettings.Instance`
- Instance fields: `HotfixUrl`, `HotfixMaxRetryCount`, `HotfixRetryBaseDelaySeconds`, `BuildPipelineConfigPath`, `ManifestOutputFormat`, `MaxHotfixSizeBytes`, `AssetCollectionDataFolder`, `AssetCollectionSettingPath`, `DependencyFilterExtensions`, and `ExcludedAssetGUIDs`
- `ExcludedAssetGUIDs` is the AB collection exclusion source used by `CollectionScanOptions.FromABSettings()`.

## Compatibility Settings Assets

`SharedBuildSettings`, `AABuildSettings`, `ABBuildSettings`, and `BuildRepositorySettings` are compatibility/migration leftovers. Active code should not read them for current behavior. `FYAssetBuildSettingsProvider` may copy values from those old assets only when creating missing `FYAssetSettings`, `FYAssetAASettings`, or `FYAssetABSettings`.

- `PushTargetConfig.Path` is still a publish root; an empty path means the current `BuildPathManager.OutputRoot`.
- `VersionDataBase` remains shared product-version data and is referenced from `FYAssetSettings.VersionDataBasePath`; there are no AA/AB-specific version database paths.
- The Settings panel edits `FYAssetSettings`. AA Config edits `FYAssetAASettings`. AB Config edits `FYAssetABSettings`; the AB Pipeline page owns the BuildGraph and build controls. Repository Push target configuration is edited from the repository panel and stored on `FYAssetSettings.PushTargets`.
- Build settings path fields now use chooser buttons in the Editor UI instead of raw string-only editing.
- `MaxHotfixSizeBytes` in AA/AB settings is edited through a byte-unit control that displays the exact byte count alongside a selectable unit.
- `PushTargetConfig.Path` is edited through a chooser-based path field in the repository panel.

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

The collector framework under `Assets/FYAsset/Scripts/Build/Collector/` is the current AB build-time collection foundation. It defines AssetCollectionSetting, AssetEntry metadata, Filter/Group rules, and BundlePackingMode-based bundle grouping.

See `collector-framework.md` for the verified current scope.

