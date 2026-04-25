# Runtime Resource Loading

Last reviewed: 2026-04-25

## Scope

This document covers the current runtime asset-loading entry points, the Legacy-vs-AB split, and the hotfix orchestration boundary.

## Primary Entry Point

`AssetPackageManager` is the approved runtime facade for resource loading.

Key responsibilities:

- initialize one runtime asset index
- initialize one loading backend
- expose query APIs by key, type, label, and label intersection
- expose both legacy object-returning APIs and newer resolve-and-handle APIs

New runtime code should prefer `AssetPackageManager` instead of calling Addressables directly.

## One Flag Controls Two Dimensions

`Constants.USE_AB_BACKEND` controls both:

- the asset index source
- the loading backend implementation

There is intentionally no supported mixed mode such as:

- AB index + Addressables backend
- Legacy index + AB backend

## Legacy Runtime Path

When `Constants.USE_AB_BACKEND` is `false`:

- index source: `AddressableLabelsConfig`
- backend: `AddressablesBackend`
- query cache: `_labelToKeys` is built from the loaded index

This is still the default assumption unless code explicitly selects the AB path.

## AB Runtime Path

When `Constants.USE_AB_BACKEND` is `true`:

- `ManifestLoader.LoadAsync()` loads the AB manifest
- `ABAssetIndex` becomes the active `IAssetIndex`
- `ABBundleLoader` manages bundle loading and dependency traversal
- `ABPackageBackend` becomes the active `IPackageBackend`

Initialization is fail-fast. If AB manifest loading fails, the manager does not silently fall back to the Legacy path.

## Resolve-And-Handle API

The newer API layer resolves a unique runtime entry first, then returns an `AssetHandle<T>`.

Main methods:

- `LoadByAddress<T>()`
- `LoadByAddressSync<T>()`
- `LoadByTypeKey<T>()`
- `LoadByTypeKeySync<T>()`

### Resolve stage

- `AssetResolver.ResolveByAddress<T>()` resolves a unique runtime entry by address
- `AssetResolver.ResolveByTypeKey<T>()` resolves by type key, optionally using labels for disambiguation

### Load stage

- Legacy path: backend returns the asset directly and the manager wraps unload logic into a handle callback
- AB path: backend returns `(asset, bundleName, error)` and the manager allocates a handle whose release callback calls `ABPackageBackend.UnloadByEntryId`

## Index And Backend Abstractions

### `IAssetIndex`

Represents runtime lookup capabilities such as:

- labels
- type keys
- key existence
- query expansion for label/type intersections

Current implementations:

- `AddressableLabelsConfig` as the Legacy implementation
- `ABAssetIndex` as the AB implementation

### `IPackageBackend`

Represents the runtime loading backend.

Current implementations:

- `AddressablesBackend`
- `ABPackageBackend`

## Hotfix Orchestration Boundary

`HotfixManager` is still the orchestration entry point for startup hotfix.

Responsibilities:

- load `BuildIndexData` from `StreamingAssets`
- initialize `PathManager`
- detect package GUID changes and clean caches when needed
- download the remote `manifest.json`
- choose either `LegacyHotfixBackend` or `ABHotfixBackend`
- compare local and remote versions
- prepare and download required bundles
- write the local manifest pointer and switch paths
- call `AssetPackageManager.Instance.Initialize()` as the final resource bootstrap step

## Shared Runtime Support Components

### `PathManager`

- centralizes package root resolution
- uses build GUIDs to isolate package directories
- switches to the new build root after a hotfix is applied

### `NetworkDownloader`

- shared helper under `Assets/FYAsset/Scripts/Helpers/Helper/`
- used by both hotfix backends
- provides text/file download primitives instead of backend-specific download code

## Current Truth vs Refactor Direction

### Verified current truth

- the Legacy path still exists and remains a first-class runtime path
- hotfix startup still depends on `HotfixManager`
- Addressables is not fully removed from the repository

### Verified refactor direction

- AB manifest/index/backend types exist and already plug into the same public manager
- the repository is moving toward backend separation through `IAssetIndex` and `IPackageBackend`
- do not assume the fully custom AB flow is the universal default unless the flag and calling context say so
