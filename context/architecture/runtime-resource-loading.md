# Runtime Resource Loading

Last reviewed: 2026-06-05

## Scope

This document covers the current runtime asset-loading entry points, the AA-vs-AB split, and the hotfix orchestration boundary.

## Primary Entry Point

`AssetPackageManager` is the approved runtime facade for resource loading.

Source location: `Assets/FYAsset/Scripts/Runtime/Core/AssetPackageManager.cs`.

Key responsibilities:

- initialize one runtime asset index
- initialize one loading backend
- expose query APIs by key, type, label, and label intersection
- expose both AA object-returning APIs and newer resolve-and-handle APIs

New runtime code should prefer `AssetPackageManager` instead of calling Addressables directly.

## One Flag Controls Two Dimensions

`FYAssetSettings.Instance.UseABBackend` controls both:

- the asset index source
- the loading backend implementation

There is intentionally no supported mixed mode such as:

- AB index + Addressables backend
- AA index + AB backend

## AA Runtime Path

When `FYAssetSettings.Instance.UseABBackend` is `false`:

- index source: `AAManifestLoader` loads `AAManifest.bin` or `AAManifest.json` from `RuntimePathManager.CurrentGUIDRoot`, then falls back to `Application.streamingAssetsPath` for the full-build baseline
- backend: `AddressablesBackend`
- query caches are built from `AAManifest.AssetEntries`, `AAManifest.KeysByType`, and `AAManifest.KeysByLabel`
- `AddressablesBackend` only loads/unloads objects through Addressables; it does not read the AA manifest or build query indexes

This is still the default assumption unless code explicitly selects the AB path.

## AB Runtime Path

When `FYAssetSettings.Instance.UseABBackend` is `true`:

- `ABManifestLoader.LoadAsync()` loads the AB manifest via `FileHelper.ReadAllBytesAsync`, checking `RuntimePathManager.CurrentGUIDRoot` before `Application.streamingAssetsPath`
- `ABManifest.BundleEntries` is the complete runtime bundle table used by the AB index, bundle loader, and package backend
- `ABAssetIndex` becomes the active `IAssetIndex`
- `ABBundleLoader` manages bundle loading and dependency traversal
- `ABPackageBackend` becomes the active `IPackageBackend`

Current initialization falls back to the AA path with an explicit warning if AB manifest loading fails.

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

- AA path: backend returns `(asset, error)` tuple and the manager wraps unload logic into a handle callback
- AB path: backend returns `(asset, bundleName, error)` and the manager allocates a handle via `HandleRegistry.Alloc`; the release callback calls `ABPackageBackend.UnloadByEntryId` only when `HandleRegistry._entryActiveCounts` for that EntryId reaches zero (all Handles released)

### Handle lifecycle

- `HandleRegistry` tracks per-EntryId active Handle count via `_entryActiveCounts` dictionary
- `Alloc` increments the count; `Release` decrements and fires the unload callback only when the count reaches zero
- This prevents use-after-free when multiple Handles reference the same asset

The handle/resolve runtime models (`AssetHandle`, `HandleRegistry`, `ResolveResult`, and `RuntimeAssetEntry`) live under `Assets/FYAsset/Scripts/Runtime/Backends/AB/Models/` because they belong to the AB runtime loading model. `RuntimeMessage` stays under `Assets/FYAsset/Scripts/Runtime/Models/` as the shared runtime diagnostic type.

## Index And Backend Abstractions

### `IAssetIndex`

Represents runtime lookup capabilities such as:

- labels
- type keys
- key existence
- query expansion for label/type intersections

Current implementations:

- `AAManifest` data as the AA query-cache source
- `ABAssetIndex` as the AB implementation

Build-time AA index data is produced by `AAAssetIndexBuilder` and written into `AAManifest`.

Source location: `Assets/FYAsset/Scripts/Runtime/Contracts/IAssetIndex.cs`.

### `IPackageBackend`

Represents the runtime loading backend. All load methods return `(T, RuntimeMessage)` tuples — errors are never thrown for expected failure paths.

```csharp
Task<(T asset, RuntimeMessage error)> LoadAssetAsync<T>(string key);
(T asset, RuntimeMessage error) LoadAssetSync<T>(string key);
// Plus entryId overloads with the same tuple return pattern.
```

Current implementations:

- `AddressablesBackend` — wraps Addressables loading as `(asset, RuntimeMessage)` tuples and leaves AA manifest/index loading to `AAManifestLoader` plus `AssetPackageManager`
- `ABPackageBackend` — uses internal `LoadAssetInternal*` methods that return `RuntimeMessage` directly

Source location: `Assets/FYAsset/Scripts/Runtime/Contracts/IPackageBackend.cs`.

## Hotfix Orchestration Boundary

`HotfixManager` is still the orchestration entry point for startup hotfix.

Source location: `Assets/FYAsset/Scripts/Hotfix/HotfixManager.cs`.

Responsibilities:

- load `BuildIndexData` from `StreamingAssets`
- initialize `RuntimePathManager`
- detect package GUID changes and clean caches when needed
- download the remote `PackageIndex.json`
- choose either `AAHotfixBackend` or `ABHotfixBackend`
- compare local and remote versions
- prepare and download required bundles
- verify copied or downloaded bundles by CRC32 when metadata is available
- write bundles to `.tmp` files and replace the target bundle only after download/copy and verification succeed
- retry bundle download failures and CRC failures through the configured hotfix retry policy
- clean stale `.tmp` bundle files before each bundle download/apply stage
- write the local PackageIndex pointer and switch paths
- call `AssetPackageManager.Instance.Initialize()` as the final resource bootstrap step

AA hotfix metadata prefers `AAManifest.bin` and falls back to `AAManifest.json`; it still downloads `catalog.json` for resource-location data. The AA full-build baseline copies `AAManifest` into `StreamingAssets` only for query-index fallback; Addressables catalog and built-in bundle placement remain owned by the existing Addressables player build flow.

AB hotfix metadata prefers `ABManifest.bin` and falls back to `ABManifest.json`. A Full AB manifest writes an empty `DeliveryBundles` list because the Full package delivers all bundles. A Hotfix AB manifest keeps `BundleEntries` as the complete runtime table and writes `DeliveryBundles` as the current-vs-Full-baseline delivery list. `ABHotfixBackend` uses `DeliveryBundles` for the remote download list when that field exists; legacy JSON manifests without the field fall back to `BundleEntries`. Runtime loading still resolves assets and dependencies from `BundleEntries`, so unchanged bundles are loaded from the StreamingAssets Full baseline through the normal hotfix-directory-first, StreamingAssets-fallback path.

Hotfix backend locations:

- AB: `Assets/FYAsset/Scripts/Hotfix/Backends/AB/ABHotfixBackend.cs`
- AA: `Assets/FYAsset/Scripts/Hotfix/Backends/Addressables/AAHotfixBackend.cs`
- Addressables catalog adapter: `Assets/FYAsset/Scripts/Hotfix/Backends/Addressables/CatalogUpdater.cs`

`BundleDownloadItem` carries `BundleName`, `FileHash`, `FileCRC`, and `FileSize` for both AA and AB hotfix backends. `FileHash` remains the content identity used for reuse/download decisions. `FileCRC` is the fast verification checksum. `FileCRC == 0` means CRC metadata is unavailable (following Unity's convention where CRC=0 signals "skip verification"); CRC verification is skipped in that case but a Warning is logged.

Backend-specific settings own runtime hotfix network configuration. When `FYAssetSettings.Instance.UseABBackend` is `false`, `HotfixManager` reads `FYAssetAASettings.Instance.HotfixUrl`, `HotfixMaxRetryCount`, and `HotfixRetryBaseDelaySeconds`. When it is `true`, it reads the same fields from `FYAssetABSettings.Instance`. The default behavior retries failed bundle downloads and CRC mismatches up to 3 times with exponential backoff from 1 second.

## Shared Runtime Support Components

### `RuntimePathManager`

- source location: `Assets/FYAsset/Scripts/Runtime/RuntimePathManager.cs`
- centralizes package root resolution
- uses build GUIDs to isolate package directories
- switches to the new build root after a hotfix is applied

### `NetworkDownloader`

- shared helper under `Assets/FYAsset/Scripts/Helpers/`
- used by both hotfix backends
- provides text/file download primitives instead of backend-specific download code
- bundle downloads use `DownloadFileOnce()` so `HotfixManager` can own one retry policy for network failure and CRC failure

### `FileHelper`

- cross-platform file I/O utility, same shared helper tier as `NetworkDownloader`; `RuntimePathManager` is runtime-root owned
- Android StreamingAssets reads go through `UnityWebRequest`; other platforms use `Task.Run(File.ReadAllBytes)` through the shared helper
- atomic writes via temp-file + rename pattern (`WriteAllBytesAtomic` / `WriteAllTextAtomic`)
- safe deletion via `TryDelete` / `TryDeleteDirectory` that return bool and never throw
- bundle download/copy commit uses `ReplaceFile` after verification
- used by `ABManifestLoader`, `AAManifestLoader`, `HotfixManager.LoadBuildIndexFromStreamingAssets`, and hotfix backend post-download paths; manifest loaders do not rely on raw file existence checks before async reads so Android StreamingAssets fallback can use the helper's UnityWebRequest branch

### `FYAssetPathUtility`

- shared helper under `Assets/FYAsset/Scripts/Helpers/`
- joins remote URL roots and path segments without depending on trailing slashes
- joins local filesystem paths for runtime hotfix roots, bundle paths, manifest paths, and StreamingAssets fallbacks
- preserves URI-like path roots such as Android StreamingAssets `jar:` paths by joining those segments with `/` instead of OS separators
- compares normalized local paths when repository publication needs to detect source and target identity

### `HashGenerator`

- shared helper under `Assets/FYAsset/Scripts/Helpers/`
- provides `GenerateFileHash` for MD5 content identity and `GenerateFileCRC` for CRC32 verification
- runtime hotfix verification and build-time metadata generation use the same CRC implementation
- `GenerateDeepHash` is Editor-only because it depends on `UnityEditor.AssetDatabase`

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

- the AA path still exists and remains a first-class runtime path
- hotfix startup still depends on `HotfixManager`
- Addressables is not fully removed from the repository

### Verified refactor direction

- AB manifest/index/backend types exist and already plug into the same public manager
- the repository is moving toward backend separation through `IAssetIndex` and `IPackageBackend`
- do not assume the fully custom AB flow is the universal default unless the flag and calling context say so
