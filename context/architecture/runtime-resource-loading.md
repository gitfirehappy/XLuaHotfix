# Runtime Resource Loading

Last reviewed: 2026-04-29

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

- `ManifestLoader.LoadAsync()` loads the AB manifest via `FileHelper.ReadAllBytesAsync`
- `ABAssetIndex` becomes the active `IAssetIndex`
- `ABBundleLoader` manages bundle loading and dependency traversal
- `ABPackageBackend` becomes the active `IPackageBackend`

Initialization is fail-fast. If AB manifest loading fails, the manager does not silently fall back to the Legacy path.

All public load methods return `(T asset, RuntimeMessage error)` tuples — errors are values, not exceptions. The internal tuple API `LoadAssetTupleAsync/Sync` returns `(T, string bundleName, RuntimeMessage)` and is used by `AssetPackageManager` for Handle allocation.

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

- Legacy path: backend returns `(asset, error)` tuple and the manager wraps unload logic into a handle callback
- AB path: backend returns `(asset, bundleName, error)` and the manager allocates a handle via `HandleRegistry.Alloc`; the release callback calls `ABPackageBackend.UnloadByEntryId` only when `HandleRegistry._entryActiveCounts` for that EntryId reaches zero (all Handles released)

### Handle lifecycle

- `HandleRegistry` tracks per-EntryId active Handle count via `_entryActiveCounts` dictionary
- `Alloc` increments the count; `Release` decrements and fires the unload callback only when the count reaches zero
- This prevents use-after-free when multiple Handles reference the same asset

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

Represents the runtime loading backend. All load methods return `(T, RuntimeMessage)` tuples — errors are never thrown for expected failure paths.

```csharp
Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key);
(T asset, RuntimeMessage error) LoadAssetSync<T>(string key);
// Plus entryId overloads with the same tuple return pattern.
```

Current implementations:

- `AddressablesBackend` — wraps Addressables exceptions as `RuntimeMessage`
- `ABPackageBackend` — uses internal `LoadAssetInternal*` methods that return `RuntimeMessage` directly

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

- shared helper under `Assets/FYAsset/Scripts/Helpers/`
- used by both hotfix backends
- provides text/file download primitives instead of backend-specific download code

### `FileHelper`

- cross-platform file I/O utility, same tier as `NetworkDownloader` / `PathManager`
- Android StreamingAssets reads go through `UnityWebRequest`; other platforms use `Task.Run(File.ReadAllBytes)`
- atomic writes via temp-file + rename pattern (`WriteAllBytesAtomic` / `WriteAllTextAtomic`)
- safe deletion via `TryDelete` / `TryDeleteDirectory` that return bool and never throw
- used by `ManifestLoader`, `HotfixManager.LoadBuildIndexFromStreamingAssets`, and `ABHotfixBackend.PostDownloadAsync`

## Error Handling Architecture

### Runtime errors: `RuntimeMessage`

- `RuntimeSeverity { Warning, Error }` × `Code` (string) × `Message` (string)
- constructed exclusively through static factory methods (`RuntimeMessage.NotFound`, `.Error`, `.Warning`, etc.)
- `RuntimeErrorCodes` holds all code constants as `const string`
- all `IPackageBackend` load methods return `(T, RuntimeMessage)` tuples — errors are values
- `AssetHandle<T>` carries a `RuntimeMessage` through `HandleRegistry` for caller inspection

### Build-time errors: `BuildMessage`

- `BuildSeverity { Warning, Error }` × `Code` (string) × `Message` (string) × `Source` (string)
- used by `CollectionScanner` and the collector framework for diagnostics during build
- factory methods: `BuildMessage.Error(code, msg, source)` / `BuildMessage.Warning(code, msg, source)`
- `BuildErrorCodes` holds all code constants

### Design principles

- Build-time and runtime error types are intentionally separate (different assemblies, runtime has no `Source`)
- String `Code` on both sides — extensible without touching a central enum
- `Warning` severity on both sides — runtime currently has zero Warning consumers (infrastructure reserved for degraded loading / retry recovery)

## Current Truth vs Refactor Direction

### Verified current truth

- the Legacy path still exists and remains a first-class runtime path
- hotfix startup still depends on `HotfixManager`
- Addressables is not fully removed from the repository

### Verified refactor direction

- AB manifest/index/backend types exist and already plug into the same public manager
- the repository is moving toward backend separation through `IAssetIndex` and `IPackageBackend`
- do not assume the fully custom AB flow is the universal default unless the flag and calling context say so
