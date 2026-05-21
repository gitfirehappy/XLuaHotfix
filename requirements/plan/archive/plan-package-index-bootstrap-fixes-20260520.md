# Sub-Plan PIB-1: PackageIndex And Bootstrap Baseline Fixes

> **Risk**: Medium
> **Dependencies**: `BuildPackageRequest`, `BuildPathManager`, `BuildProjectManager`, `TaskExportLocalBuildData`, `LocalStatusExporter`, `HotfixManager`, `ManifestLoader`, `TaskBuildBundles`
> **Status**: Executed — 2026-05-20
> **Positioning**: Post-review corrective slice after AA/AB Task alignment. It fixes root package pointer naming, full-build AB bootstrap baseline export, Android StreamingAssets manifest fallback, RawFile hash metadata, and same-day package-name collision.

---

## Objective

Fix the confirmed post-review issues without changing manifest schemas or widening the AA/AB task graph design.

After this plan:

- `PackageIndex` uses `PackageIndex.json`, never `manifest.json` or ABManifest constants.
- full AB builds copy the real final package baseline into `StreamingAssets`.
- `BuildIndex.BuildGUID` equals the created package name.
- AB manifest fallback can use `FileHelper` platform handling for Android StreamingAssets.
- RawFile bundles carry real file hashes.
- package names include seconds to prevent same-day collisions.

---

## Findings

| ID | Finding | Root Cause | Fix |
|----|---------|------------|-----|
| PIB-F1 | `BuildPathManager.PackageIndexPath` wrote `ABManifest.json`, while runtime downloaded `manifest.json` | PackageIndex pointer reused ABManifest naming and had a separate runtime literal | Add `FYAssetSettings.PACKAGE_INDEX_FILE_NAME = "PackageIndex.json"` and use it on build/runtime paths |
| PIB-F2 | AB full-build baseline wrote an empty ABManifest to `StreamingAssets` | `LocalStatusExporter` generated placeholder state instead of consuming the completed package output | Pass `BuildPackageRequest` to `LocalStatusExporter` and copy real package files for AB full builds |
| PIB-F3 | `ManifestLoader.TryLoadFromFile` checked `File.Exists` before `FileHelper.ReadAllBytesAsync` | Platform-specific I/O helper was bypassed before Android StreamingAssets fallback | Remove the precheck and let `FileHelper` attempt the read |
| PIB-F4 | RawFile bundles had empty hash metadata | Direct copy path did not mirror the serialized bundle hash path | Compute `HashGenerator.GenerateFileHash(destPath)` after copy |
| PIB-F5 | `Build_yyyyMMdd_version` collided on repeated same-day builds | Package identity timestamp resolution was day-level | Change package name to `Build_yyyyMMddHHmmss_version` |

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Settings | `FYAssetSettings` | Add `PACKAGE_INDEX_FILE_NAME = "PackageIndex.json"` |
| Build paths | `BuildPathManager`, `BuildPackageRequest` | Use the PackageIndex constant and second-level package timestamp |
| Build orchestrator | `BuildProjectManager` | Rename `UpdateManifestFile` to `UpdatePackageIndexFile` |
| Runtime hotfix | `HotfixManager`, `PackageIndex` docs | Download and persist `PackageIndex.json`; rename PackageIndex-related members |
| Bootstrap export | `TaskExportLocalBuildData`, `LocalStatusExporter` | Export with `BuildPackageRequest`; for AB full builds copy real AB package manifest and bundles to `StreamingAssets`; clean stale opposite-backend baseline files |
| AB loader | `ManifestLoader` | Remove raw `File.Exists` precheck before helper read |
| RawFile build | `TaskBuildBundles` | Set copied RawFile hash to the actual file hash |
| Documentation | `README.md`, `docs/FYAsset/*`, `context/architecture/*` | Replace PackageIndex `manifest.json` references with `PackageIndex.json`; keep ABManifest/AAManifest names unchanged |
| Mistakes | `context/mistakes/*` | Add concise prevention notes for pointer-name split, placeholder bootstrap baseline, and platform I/O prechecks |

---

## Acceptance Criteria

- [x] `PackageIndex` build output path uses `FYAssetSettings.PACKAGE_INDEX_FILE_NAME`.
- [x] Runtime remote URL and local hotfix pointer use `PackageIndex.json`.
- [x] PackageIndex-related method/member names no longer use generic `Manifest` wording.
- [x] Full AB build bootstrap export copies real `ABManifest.json` / `ABManifest.bin` according to the produced package and copies `bundles/`.
- [x] Full AA build bootstrap behavior remains aligned with existing AA package output and does not add unrelated new AA logic.
- [x] `BuildIndex.BuildGUID` is `BuildPackageRequest.PackageName`.
- [x] Package names use `yyyyMMddHHmmss`.
- [x] `ManifestLoader` no longer calls raw `File.Exists(path)` before `FileHelper.ReadAllBytesAsync(path)`.
- [x] RawFile `BundleBuildInfo.Hash` is real hash, not an empty string.
- [x] README/docs/context reflect `PackageIndex.json` for the package pointer.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out of Scope

- Changing `ABManifest` or `AAManifest` schemas.
- Adding `manifest.json` compatibility aliases.
- Changing CDN upload or deployment tooling.
- Moving snapshot rebuild, Lua index export, or PackageIndex update into a DAG task.
- Refactoring all Addressables runtime loading paths.

---

## Approval Checklist

- [x] Rename the root package pointer file to `PackageIndex.json` with no `manifest.json` compatibility alias.
- [x] Keep ABManifest/AAManifest filenames unchanged.
- [x] Use `Build_yyyyMMddHHmmss_version` package names.
- [x] Make `BuildIndex.BuildGUID` equal the package name.
- [x] AB full-build bootstrap baseline copies the real final package to `StreamingAssets`.
- [x] Keep AA bootstrap handling aligned with existing AA package output and avoid unrelated AA expansion.
- [x] Remove only `ManifestLoader`'s raw existence precheck for StreamingAssets fallback.
- [x] Compute real RawFile hashes.
- [x] Sync README/docs/context/mistakes and archive the review/plan after verification.
