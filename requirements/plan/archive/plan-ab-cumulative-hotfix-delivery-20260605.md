# Plan: AB Cumulative Hotfix Delivery

> Status: Signed off / Archived
> Date: 2026-06-05
> Requirement ID: ab-cumulative-hotfix-delivery-20260605
> Scope: AB hotfix package delivery shape, Full-baseline delivery calculation, manifest delivery list, repository preview display, and docs/context alignment.

## Goal

Make AB Hotfix delivery cumulative relative to the current Major Full baseline while keeping runtime lookup simple:

- `ABManifest.BundleEntries` remains the complete runtime bundle table.
- `ABManifest.DeliveryBundles` records only the physical bundles that the remote Hotfix package actually delivers.
- Hotfix delivery is calculated against the same channel/backend/Major Full baseline, not against HEAD.
- The runtime downloads `DeliveryBundles`, loads hotfix bundles first, and falls back to `StreamingAssets` for unchanged baseline bundles.

## Decisions

1. `DeliveryBundles` reuses `ManifestBundleEntry`; no lightweight DTO is introduced.
2. Full builds write an empty `DeliveryBundles` list.
3. Hotfix builds write Added/Modified physical bundles from current output vs same channel/backend/Major Full baseline.
4. `RepositoryCommit.BuildType` is a serialized string with values `Full` and `Hotfix`.
5. Old repository commits without `BuildType` are not supported by fallback inference; clean old snapshots and rebuild a Full baseline.
6. AB Hotfix fails when no same-Major Full baseline is found.
7. Delivery does not include dependency closure. Unchanged dependencies are loaded from `StreamingAssets`; changed dependencies enter delivery through their own hash diff.
8. Hotfix fallback validation is required: every non-delivered bundle must exist in the Full baseline with the same physical name and hash.
9. Removed bundles are not delivered or deleted by this plan; the complete manifest stops referencing them.
10. `ABManifest` binary schema becomes version 2. Old schema-1 AB `.bin` is not compatible; rebuild the Full baseline after this change.
11. AB Diff Preview keeps current-vs-HEAD semantics and separately displays Full-baseline delivery count/size/list.
12. AA package output and Push filtering remain unchanged.

## Implementation Checklist

1. Requirements
   - Promote this plan to the active queue.
   - Archive the original draft under `requirements/plan/drafts/archive/`.
2. Manifest and serialization
   - Add `ABManifest.DeliveryBundles`.
   - Bump `ABManifest` schema version to 2.
   - Regenerate binary serializers and update `BinarySerializerInitializer` registration.
   - Extend AB manifest round-trip test coverage for `DeliveryBundles`.
3. Repository metadata
   - Add `RepositoryCommit.BuildType`.
   - Fill it from `BuildPackageRequest.BuildType` during repository commit.
   - Display it in repository list UI/CLI.
4. Build pipeline
   - Add `BuildContextKeys.ABDeliveryBundles`.
   - In `TaskScanABHotfixDiff`, compute:
     - current complete repository artifacts;
     - current-vs-HEAD `ArtifactDelta`;
     - current-vs-Full-baseline `DeliveryBundles`.
   - Fail AB Hotfix when the same-Major Full baseline is missing.
   - Validate non-delivered bundles against the Full baseline by physical bundle name and hash.
   - Make `TaskOrganizeOutput` copy all bundles for Full and `ABDeliveryBundles` for Hotfix.
   - Make `TaskWriteABPackageManifest` size-check delivery bundles for Hotfix and all bundles for Full.
5. Runtime
   - Make AB hotfix version info prefer `DeliveryBundles` when present.
   - Preserve fallback to `BundleEntries` for old/legacy JSON manifests with no delivery list.
6. Repository preview and CLI
   - Return AB preview data containing both HEAD diff and Full-baseline delivery summary/list.
   - Show both in repository UI and CLI output with explicit labels.
7. Docs and records
   - Update `context/architecture/resource-build-and-release.md`.
   - Update `context/architecture/runtime-resource-loading.md`.
   - Update FYAsset Chinese docs that describe repository/build pipeline/hotfix package shape.
   - Update `requirements/progress.txt` and master plan status.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- AB manifest binary round-trip test covers `DeliveryBundles`.
- Static checks:
  - AB hotfix runtime downloads `DeliveryBundles` first.
  - Hotfix organize no longer copies all `BundleEntries`.
  - Hotfix size guard uses delivery size.
  - Repository commits write/display `BuildType`.
  - AB preview output distinguishes HEAD diff from Full-baseline delivery.
- Self-simulated flow:
  - Full build produces complete baseline and empty delivery list.
  - First Hotfix delivers Added/Modified vs Full.
  - Later Hotfix still delivers cumulative Added/Modified vs Full.
  - No-diff Hotfix emits manifest-only package.
  - Missing Full baseline fails before package finalization.
  - Non-delivered bundle missing or hash-mismatched in Full baseline fails validation.

## Non-Goals

- Do not change AA package filtering or Addressables catalog semantics.
- Do not change Repository Push whole-package publication semantics.
- Do not implement runtime deletion of removed bundles.
- Do not fix or implement `BundleFileNameStyle`; delivery uses current physical output bundle names.

## Execution Notes

- Implemented `ABManifest.DeliveryBundles` and bumped AB binary schema to 2.
- Repository commits now record `BuildType`; AB Hotfix baseline lookup requires same Channel/Backend/Major `Full`.
- AB Hotfix delivery is computed against the Full baseline while AB Diff Preview still shows current-vs-HEAD.
- Full builds copy all manifest bundles and write empty delivery; Hotfix builds copy only `ABDeliveryBundles`.
- AB runtime hotfix downloads `DeliveryBundles` when the remote manifest has that field, with JSON legacy fallback to `BundleEntries`.
- Docs/context/progress were aligned with the current package shape and baseline requirement.
- Verification evidence: `dotnet build XLuaHotfix.sln --no-restore` exited 0 with existing `System.Net.Http` warnings only; scoped `git diff --check` passed for the AB cumulative hotfix change set; full `git diff --check` is still blocked by unrelated pre-existing trailing whitespace in `Assets/Resources/FYAssetAASettings.asset`, `Assets/Resources/FYAssetAASettings.asset.meta`, `Assets/Resources/FYAssetABSettings.asset`, and `Assets/Resources/FYAssetABSettings.asset.meta`.
- 2026-06-06 AI acceptance: compile, scoped whitespace, and static path checks were rerun during basic pipeline closure. The plan is accepted and archived; no additional Unity real build was required for this acceptance pass.
