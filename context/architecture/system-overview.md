# System Overview

Last reviewed: 2026-04-29

## Purpose

This document gives AI agents the current architectural map of `XLuaHotfix`. It is intentionally different from `docs/`, which contains human-oriented explanations and design discussions.

## Technology Baseline

- Engine: Unity `2022.3.6f1c1`
- Primary languages: C# and Lua
- Lua integration: XLua
- Current shipping runtime resource path: Unity Addressables-based flow behind `AssetPackageManager`
- In-progress runtime alternative: custom AB backend behind `Constants.USE_AB_BACKEND`
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

- export build metadata such as `BuildIndexData`, `VersionState`, `AddressableLabelsConfig`, `LuaScriptsIndex`, and hotfix `Manifest`
- manage differential snapshots and hotfix group reassignment
- drive full package, hotfix package, release confirmation, and group reset workflows
- host the new collector foundation used for future build-pipeline refactoring

See `resource-build-and-release.md` and `collector-framework.md`.

### Runtime resource loading and hotfix

Main code roots:

- `Assets/FYAsset/Scripts/Runtime/`
- `Assets/FYAsset/Scripts/LegacyRuntime/`

Responsibilities:

- expose the project-approved runtime loading entry point: `AssetPackageManager`
- choose either the Legacy Addressables path or the custom AB path from one feature flag
- orchestrate hotfix startup, version comparison, download, and local pointer switching

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

- `AssetPackageManager` uses the Legacy index and Legacy backend when `Constants.USE_AB_BACKEND` is `false`
- `HotfixManager` still orchestrates startup and chooses `LegacyHotfixBackend` or `ABHotfixBackend`
- direct Addressables usage still exists in hotfix and legacy loading code

### In-progress replacement path

- `ABAssetIndex`, `ABBundleLoader`, `ABPackageBackend`, `ABHotfixBackend`, and collector-related code are already present
- this path is not an independent parallel public API; it is selected by the same feature flag and still coexists with the Legacy path
- human docs about a full Addressables replacement describe the direction of travel, not the default assumption for all current code

## Project-Wide Rules for AI Changes

- Prefer `AssetPackageManager` over direct Addressables calls in new runtime code.
- Treat hotfix core flow changes as high-risk.
- Approval workflow belongs in `requirements/`, not in `context/`.
- New Lua-callable C# types must be synchronized with `TypeMemberListSO` / XLua config loading.
- Cross-language event registration/unregistration must go through `EventCentre`; do not introduce raw delegate coupling between Lua and C#.


