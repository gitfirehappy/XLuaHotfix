# Review Build Chain Blockers Plan 2026-06-07

> **Status**: Signed off and archived
> **Origin**: `requirements/review/archive/review-build-chain-blockers-20260606.md` and `requirements/review/archive/review-staging-diff-collections-20260606.md`
> **Approval**: Developer requested direct execution after precise plan capture.
> **Archived**: 2026-06-07

## Summary

Fix the confirmed build-chain blockers without widening package publication, runtime hotfix loading, or Push semantics.

This plan corrects Unity package resolution, separates Repository Changes from AB Hotfix Delivery preview, makes Collector payload Auto classification follow Unity importer output, ignores Unity AssetBundle sidecar files during AB verification, makes version increments transactional, and improves preview failure diagnostics without introducing a new error system.

## Code-Level Changes

### Package Resolution

- Add direct `com.unity.collections` dependency in `Packages/manifest.json` using version `2.6.6`.
- Let Unity regenerate `Packages/packages-lock.json` during package resolution; do not edit `Library/PackageCache`.

### Repository Changes vs AB Delivery

- Add explicit BuildContext flags in `BuildContextKeys`:
  - `RepositoryPreviewMode`
  - `ABDeliveryPreviewMode`
- `RepositoryPreviewRunner.RunAAPreview` and `RunABPreviewDetailed` set `RepositoryPreviewMode=true`.
- Add `RepositoryPreviewRunner.RunABDeliveryPreview` for the separate AB delivery button. It sets both `RepositoryPreviewMode=true` and `ABDeliveryPreviewMode=true`.
- `TaskScanAddressableHotfixDiff` treats missing repository HEAD as empty baseline only when `RepositoryPreviewMode=true`; malformed HEAD remains fatal.
- `TaskScanABHotfixDiff` always writes current-vs-HEAD `ArtifactDelta`; missing HEAD becomes empty baseline in repository preview mode.
- `TaskScanABHotfixDiff` computes `ABDeliveryBundles` only for official AB Hotfix builds or `ABDeliveryPreviewMode=true`. Missing same-Major Full baseline:
  - official Hotfix: fatal failure
  - Changes preview: no fatal; delivery unavailable message
  - Delivery preview: non-success result with explicit missing baseline message
- Extend `ABRepositoryPreviewResult` with delivery availability/message fields for UI and CLI display.
- `RepositoryStatusPanel` top toolbar becomes `Refresh Changes`, optional `Preview Delivery` for AB, and `Push`. `Refresh Changes` only refreshes git-style current-vs-HEAD changes.

### Collector Payload Classification

- Remove extension whitelist logic from `AssetClassifier`.
- `EForcePayloadKind` remains authoritative.
- Auto classification rules:
  - `.unity` -> `EPayloadKind.Scene`
  - Unity importer/main asset exists and is a usable non-folder/non-default object -> `EPayloadKind.Serialized`
  - otherwise -> `EPayloadKind.RawFile`
- Use `AssetDatabase.GetMainAssetTypeAtPath` / `AssetDatabase.LoadMainAssetAtPath` so `.csv`, `.json`, `.txt`, and project `.lua` ScriptedImporter `TextAsset` imports classify as Serialized.
- Do not change collector configuration or add unapproved RawFile packing policy in this slice.

### AB Verification

- In `TaskVerifyBuildResult`, exclude Unity-generated sidecar files from orphan and count checks:
  - any `.manifest` file
  - the root manifest file emitted by `BuildPipeline.BuildAssetBundles`
- Count only deployable bundle files when comparing actual files with `ABManifest.BundleEntries` and `BundleBuildInfo`.

### Version Transaction

- `BuildProjectManager` calculates the next `VersionNumber` in memory before build.
- `BuildPackageRequest` uses the staged next version.
- `VersionDataBase` is saved only after backend build and repository commit both succeed.
- `TaskPrepareContext` uses `BuildPackageRequest.Version` when present, so official build DAG metadata matches the staged request version.

### Preview Failure Diagnostics

- Add optional `TaskName` to `BuildTaskResult`.
- `DAGScheduler` fills `TaskName` for validation and execution results.
- `RepositoryPreviewRunner` formats first failed task into preview exceptions: backend, task name, error code, message, fatal flag, and skipped count.
- Keep existing `BuildTaskResult`/`BuildErrorCodes`; no new error framework.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static checks:
  - no serialized extension whitelist remains in `AssetClassifier`
  - Repository Changes button does not call AB delivery preview
  - AB Delivery preview has its own code path/UI button
  - `.manifest` sidecars are excluded from AB verification count/orphan checks
  - `VersionDataBase.IncrementVersion` is not called before `RunBuild`
  - preview failures include task/error details
- Self-simulated acceptance:
  - empty AA/AB repository Changes shows full Added delta
  - missing AB Full baseline does not block Changes but makes Delivery unavailable
  - official AB Hotfix still fails without same-Major Full baseline
  - Lua ScriptedImporter `TextAsset` is Serialized under Auto payload
  - failed build does not advance `VersionDataBase`

## Non-Goals

- Do not change runtime hotfix loading.
- Do not change package artifact format or Push publication semantics.
- Do not auto-edit existing Collector groups, labels, or packing modes.
- Do not configure a default PushTarget; keep the existing UI notice for empty target configuration.

## Implementation Summary

- `Packages/manifest.json` now pins `com.unity.collections` to `2.6.6`; Unity Package Manager regenerated `Packages/packages-lock.json` with `com.unity.collections@2.6.6`.
- Repository Changes preview is current-vs-HEAD and treats missing HEAD as an empty baseline; malformed HEAD remains fatal.
- AB Delivery preview is a separate action and is the only preview path that computes current-vs-Full-baseline delivery.
- `AssetClassifier.Auto` is importer-first and no longer uses a serialized extension whitelist.
- AB build verification excludes Unity sidecar manifest files from deployable bundle orphan/count checks.
- `BuildProjectManager` stages the next version and applies `VersionDataBase` only after backend build and repository commit both succeed.
- Preview failure messages now include first failed task name, error code, message, fatal flag, and skipped count when available.
