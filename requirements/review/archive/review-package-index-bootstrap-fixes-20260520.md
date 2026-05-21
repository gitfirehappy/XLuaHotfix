# PackageIndex And Bootstrap Baseline Review

> **Date**: 2026-05-20
> **Reviewer**: Codex
> **Scope**: Recent AA/AB Task alignment batches, package pointer naming, bootstrap local data export, AB runtime manifest loading, RawFile bundle metadata
> **Method**: Static source review, cross-file data-flow review, targeted grep audit, solution build verification

## Findings

| ID | Severity | Finding | Resolution |
|----|----------|---------|------------|
| PIB-F1 | High | `PackageIndex` pointer had split naming: build output reused `ABManifest.json`, runtime downloaded `manifest.json`. | Added `FYAssetSettings.PACKAGE_INDEX_FILE_NAME = "PackageIndex.json"` and routed build/runtime pointer paths through it. |
| PIB-F2 | High | Full AB bootstrap wrote an empty ABManifest to `StreamingAssets` instead of the real final package baseline. | `TaskExportLocalBuildData` now passes `BuildPackageRequest`; AB full builds copy real final `ABManifest` files and `bundles/` into `StreamingAssets`. |
| PIB-F3 | Medium | `BuildIndex.BuildGUID` used an exporter timestamp instead of the package directory identity. | `LocalStatusExporter` writes `BuildGUID = request.PackageName`. |
| PIB-F4 | Medium | Same-day package names collided with `Build_yyyyMMdd_version`. | Package name now uses `Build_yyyyMMddHHmmss_version`. |
| PIB-F5 | Medium | `ManifestLoader` prechecked raw `File.Exists`, blocking Android StreamingAssets helper fallback. | Removed the precheck and relies on `FileHelper.ReadAllBytesAsync`. |
| PIB-F6 | Medium | RawFile bundle metadata had an empty hash, causing verification mismatch risk. | RawFile copy path computes `HashGenerator.GenerateFileHash(destPath)`. |

## Verification

- `rg` audit confirmed no PackageIndex `manifest.json` references remain in active code/docs except historical mistake notes; remaining `remoteManifest*` variables are AAManifest-specific.
- `rg` audit confirmed no `ExportData(VersionNumber)`, `CreateEmptyManifest`, `Hash = ""`, or `ManifestLoader` raw `File.Exists(path)` target remains.
- `dotnet build XLuaHotfix.sln` passed with 0 errors and existing `System.Net.Http` conflict warnings.

## Archive Note

All findings in this review were addressed by `plan-package-index-bootstrap-fixes-20260520.md` and verified on 2026-05-20.
