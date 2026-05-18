# Plan: FYAsset Hotfix + Runtime Folder Boundary Cleanup

## Summary

Move FYAsset Hotfix and Runtime files into responsibility-based directories without changing runtime behavior or public APIs.

## Implementation

- Move Hotfix orchestration and shared types from `LegacyRuntime/` to `Hotfix/`.
- Move AB hotfix backend to `Hotfix/Backends/AB/`.
- Move Legacy Addressables hotfix backend and catalog adapter to `Hotfix/Backends/Addressables/`.
- Move `AssetPackageManager` to `Runtime/Facade/`.
- Move `IAssetIndex` and `IPackageBackend` to `Runtime/Contracts/`.
- Split `HotfixContext`, `HotfixVersionInfo`, and `BundleDownloadItem` into independent source files.
- Remove empty `Helpers/Helper/` and its `.meta` after verifying it contains no files.
- Preserve `.cs.meta` files when moving Unity assets.

## Documentation

- Update README, human docs, AI context, and requirement indexes to reflect new paths.
- Keep historical archive/review path references unless they are current architecture summaries.

## Verification

- Run a targeted old-path grep.
- Run `dotnet build XLuaHotfix.sln`.
- Inspect `git status --short` for expected moves and no unrelated cleanup.

## Approval Checklist

- [x] Scope is limited to Hotfix + Runtime + Helpers empty directory cleanup.
- [x] `ABHotfixBackend` goes to `Hotfix/Backends/AB/`.
- [x] Legacy Addressables hotfix files go to `Hotfix/Backends/Addressables/`.
- [x] `AssetPackageManager` goes to `Runtime/Facade/`.
- [x] `IAssetIndex` / `IPackageBackend` go to `Runtime/Contracts/`.
- [x] Hotfix data types are split into independent files.
