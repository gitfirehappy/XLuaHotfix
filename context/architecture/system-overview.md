# System Overview

Last reviewed: 2026-05-24

## Purpose

This document gives AI agents the current architectural map of `XLuaHotfix`. It is intentionally different from `docs/`, which contains human-oriented explanations and design discussions.

## Technology Baseline

- Engine: Unity `2022.3.62f3`
- Primary languages: C# and Lua
- Lua integration: XLua
- Current shipping runtime resource path: Unity Addressables-based flow behind `AssetPackageManager`
- In-progress runtime alternative: custom AB backend behind `FYAssetSettings.Instance.UseABBackend`
- Build output roots:
  - `Assets/StreamingAssets/` for packaged data
  - `HotfixOutput/` for generated hotfix payloads

## Documentation Split

- `docs/`: Chinese, human-facing, may include design intent and human documentation when there is an actual documentation need
- `context/`: English, AI-facing, only verified current facts unless explicitly marked `[UNVERIFIED]`

Do not treat human design notes in `docs/` as the default runtime truth unless the code matches them.

## Top-Level Module Map

### Resource build and release

Main code roots:

- `Assets/FYAsset/Scripts/Build/`
- `Assets/FYAsset/Scripts/Helpers/`

Responsibilities:

- export build metadata such as `BuildIndexData`, `AAManifest`, `LuaScriptsIndex`, and hotfix `PackageIndex`
- build the AA asset index through `AAAssetIndexBuilder`, then write it into `AAManifest`
- manage differential snapshots and hotfix group reassignment
- drive full package, hotfix package, release confirmation, and group reset workflows
- host the new collector foundation used for future build-pipeline refactoring

Primary Build subdirectories:

- `Build/Release/Editor/Shared/` for release orchestration contracts and shared entry points
- `Build/Release/Editor/Addressables/` for the catalog-backed release backend and export helpers
- `Build/Release/Editor/AB/` for AB release backend
- `Runtime/Manifests/Addressables/` and `Runtime/Manifests/AB/` for runtime-readable AA and AB manifest models
- `Runtime/Manifests/Shared/` for the shared `PackageIndex` pointer model
- `Assets/XLuaFramework/Scripts/XLuaLoader/` for `LuaScriptsIndex` and XLua loader runtime data
- `Build/Bootstrap/` for packaged startup metadata
- `Build/Snapshots/` for differential snapshot data and processing
- `Build/Versioning/` for version data

See `resource-build-and-release.md` and `collector-framework.md`.

### Runtime resource loading and hotfix

Main code roots:

- `Assets/FYAsset/Scripts/Runtime/`
- `Assets/FYAsset/Scripts/Hotfix/`

Responsibilities:

- expose the project-approved runtime loading entry point: `AssetPackageManager`
- choose either the AA path or the custom AB path from one feature flag
- orchestrate hotfix startup, version comparison, download, and local pointer switching
- keep `RuntimePathManager` at the Runtime root, while AB-only handle/resolve models live under `Runtime/Backends/AB/Models/`
- keep `Runtime/Models/` reserved for shared runtime diagnostics such as `RuntimeMessage`

See `runtime-resource-loading.md`.

### Project-side XLua integration

Main code roots:

- `Assets/XLuaFramework/Scripts/`

Responsibilities:

- register the project loader into `LuaEnv`
- bind Unity lifecycle callbacks to Lua modules/classes
- initialize bridge components in a fixed order
- mediate cross-language events and cross-language coroutine waiting
- load type/member configuration for XLua attributes from `TypeMemberListSO`

See `xlua-runtime.md`.

### Third-party XLua runtime internals

Main code roots:

- `Assets/XLua/Src/`
- `Assets/XLua/Gen/`

Responsibilities:

- own `LuaEnv`, object translation, generated wrappers, delegate bridges, and hotfix hooks
- provide the low-level runtime model that explains how the project-side bridge behaves

See `xlua-third-party.md`.

## Current Runtime Truth vs Refactor Truth

This repository contains both the current production-oriented path and an in-progress replacement path.

### Current default runtime path

- `AssetPackageManager` uses the AA index and AA backend when `FYAssetSettings.Instance.UseABBackend` is `false`
- `HotfixManager` still orchestrates startup and chooses `AAHotfixBackend` or `ABHotfixBackend`
- direct Addressables usage still exists in hotfix and runtime loading code

### In-progress replacement path

- `ABAssetIndex`, `ABBundleLoader`, `ABPackageBackend`, `ABHotfixBackend`, and collector-related code are already present
- this path is not an independent parallel public API; it is selected by the same feature flag and still coexists with the AA path
- human docs about a full Addressables replacement describe the direction of travel, not the default assumption for all current code

## Project-Wide Rules for AI Changes

- Prefer `AssetPackageManager` over direct Addressables calls in new runtime code.
- Treat hotfix core flow changes as high-risk.
- Approval workflow belongs in `requirements/`, not in `context/`.
- New Lua-callable C# types must be synchronized with `TypeMemberListSO` / XLua config loading.
- Cross-language event registration/unregistration must go through `EventCentre`; do not introduce raw delegate coupling between Lua and C#.

## Editor Layout

- The landed build-pipeline editor lives under `Assets/FYAsset/Scripts/Build/Editor/` and `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/`.
- `BuildPipelineWindow` is a UI Toolkit `CreateGUI()` editor window with a resizable left sidebar and four groups: `SETTINGS`, `AA PIPELINE`, `AB PIPELINE`, and `MANAGE`.
- `AA PIPELINE` and `AB PIPELINE` are mutually gray-disabled depending on `FYAssetSettings.Instance.UseABBackend`; both groups are collapsible and keep the active group expanded.
- `AA PIPELINE` contains `AA Config`, `AA Build`, and `AA Report`.
- `AB PIPELINE` contains `Collect Config`, `Collector`, `Pipeline`, and `Builder`.
- `CollectorSettingInspector` is a UI Toolkit shortcut inspector that opens `BuildPipelineWindow` directly.
- `CollectorSettingPanel` is a UI Toolkit panel that edits `CollectorSetting` with package/group navigation and collector editing.
- `CollectorPanel` is a UI Toolkit panel focused on the current group's collector list plus validation and scan preview. Its scan preview remains in the bottom tab and uses scrollable content so compact windows can still inspect the full asset-to-bundle list.
- Build settings are split from runtime settings. `FYAssetSettings` keeps runtime/global fields; `SharedBuildSettings` stores shared build paths and push targets; `AABuildSettings` and `ABBuildSettings` store backend-specific pipeline config paths, manifest output format, and hotfix size limits.
- `SettingsPanel` edits `FYAssetSettings` first and `SharedBuildSettings` second. `AAConfigPanel` edits `AABuildSettings` before the Addressables overview. `PipelinePanel` edits `ABBuildSettings` before the AB BuildGraph.
- `PipelinePanel` is parameterized by config path, default backbone factory, Build Options visibility, and Build controls visibility. The AB sidebar entry uses it with `ABBuildSettings.BuildPipelineConfigPath` and `BuildPipelineBackbone.CreateABTasks()`, exposing Reload, Validate, Build Options, Build Mode, Build controls, and BuildGraph inspection. The AA Build sidebar entry delegates to the same panel with `AABuildSettings.BuildPipelineConfigPath` and `BuildPipelineBackbone.CreateAATasks()`, exposing Reload, Validate, Build Mode, Build controls, and BuildGraph inspection, but not Build Options because AA configuration remains owned by Addressables.
- `PipelinePanel` uses a `BuildGraphView` GraphView DAG visualization powered by `BuildGraphLayoutEngine` and `BuildTaskNode`. The graph shows code-level execution edges, SO-level execution edges, and data-flow edges derived from `ReadKeys`/`WriteKeys`. It supports Reload, `DAGScheduler.Validate()`, a right-click optional-task creation menu, node-level source opening for registered Task types, and Pipeline-triggered Full/Hotfix builds through `BuildProjectManager`.
- `PipelinePanel` keeps top validation status as a short summary and shows full validation details in a hidden-until-needed bottom bar with copy and close controls.
- Pipeline and CLI Full/Hotfix builds call the public `BuildProjectManager` build entries; `BuildProjectManager` no longer registers legacy `Tools/Build` menu items.
- Pipeline-triggered builds validate first. Fatal validation failures block execution. When execution starts, `BuildExecutionOptions` carries a `TaskStatusChanged` callback through `BuildProjectManager` and the active backend into `DAGScheduler`; `BuildTaskExecutionEvent` / `BuildTaskExecutionStatus` drive node states (`Pending`, `Running`, `Success`, `Failed`, `Skipped`) in `BuildTaskNode`.
- `BuildPipelineConfig.asset` and `AABuildPipelineConfig.asset` are the task-backbone source of truth. `BuildPipelineBackbone` only provides default task-entry creation, UI backbone recognition, display ordering, and validation metadata; it does not modify existing config assets during panel load or backend build execution.
- BuildGraph right-click task creation excludes backbone tasks (`TaskPrepareContext`, `TaskCollectAssets`, `TaskAnalyzeDependencies`, `TaskCollectBuiltins`, `TaskBuildBundles`, `TaskGenerateManifest`, `TaskVerifyBuildResult`, `TaskOrganizeOutput`, `TaskWriteABPackageManifest`, `TaskBuildAddressablesContent`, `TaskOrganizeAAOutput`, `TaskWriteAAPackageManifest`, `TaskWritePackageIndex`, `TaskExportLocalBuildData`). Backbone tasks are displayed in the DAG but are not normal creation candidates.
- BuildGraph edges and ports are display-only: users may drag task nodes to adjust the visual layout, but cannot select, delete, or reconnect existing lines. Code dependency and SO dependency edges are opaque white/blue execution lines; data-flow edges are low-opacity green lines drawn behind execution lines and de-duplicated per producer-consumer task pair.
- `TaskCollectAssets` is the backbone scan task. It loads `CollectorSetting`, runs `CollectionScanner.Scan()`, writes `CollectedAssets` and `SharePolicies` into `BuildContext`, and is the dependency source for dependency analysis and builtin collection.
- `BuilderPanel` does not host the DAG. Build result/report querying is deferred until after E7 because E7 will define diff snapshot and digest outputs that affect report inputs.
- `SettingsPanel`, `VersionPanel`, `AAConfigPanel`, `AABuildPanel`, `AAReportPanel`, `CollectorSettingPanel`, `CollectorPanel`, `PipelinePanel`, `BuilderPanel`, and generic `PlaceholderPanel` all expose UI Toolkit content through `IBuildPipelinePanel.CreateContent()`.
- The previous Collector IMGUI helper files (`CollectorTreeView`, `CollectorPropertyPanel`, `CollectorResultPanel`, and `CollectorTargetPickerPopup`) are no longer active compile targets.
- `SOAddressableTagger` is also a UI Toolkit `CreateGUI()` helper window.
- `CollectorAssetInspectorGUI` still uses `Editor.finishedDefaultHeaderGUI`; this is a Unity default Inspector header extension point and remains IMGUI-bound.
- `BuildGraph/` contains `BuildGraphView.cs` (GraphView surface), `BuildTaskNode.cs` (task node rendering and execution status display), `BuildGraphLayoutEngine.cs` (topological layer layout), and `EdgeStyle.cs` (edge type enum). The AA `BuildGraphToolbar` file has been removed.
- The `AB PIPELINE` sidebar group is visually disabled when `FYAssetSettings.Instance.UseABBackend` is `false`.

