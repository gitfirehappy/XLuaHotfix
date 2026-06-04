# Draft: AB Cumulative Hotfix Delivery

> **Status**: Draft
> **Date**: 2026-06-04
> **Purpose**: discuss how AB hotfix packages should match AA's cumulative hotfix semantics while avoiding full baseline bundles in hotfix output.
> **Executable**: No. This draft records direction and code surfaces; it must be promoted to `requirements/plan/` before implementation.

## Current Verified State

- AA hotfix uses official Addressables catalog generation. `TaskScanAddressableHotfixDiff` moves Added/Modified source assets into the Hotfix group, then Addressables builds the accumulated Hotfix group output.
- AA hotfix packages are cumulative relative to the full package's built-in content. They are not per-build pure delta packages.
- AB hotfix currently builds all current bundles, computes diff in `TaskScanABHotfixDiff`, then `TaskOrganizeOutput` copies every `ABManifest.BundleEntries` file into the final package.
- Runtime AB loading already supports hotfix-first and `StreamingAssets` fallback in `ABBundleLoader.ResolveBundlePath`.
- Runtime download currently treats `ABManifest.BundleEntries` as the AB download list through `ABHotfixBackend.GetBundleDownloadList()`.

## Target Direction

AB should align with AA's cumulative hotfix model:

- Full build exports the complete AB baseline into `StreamingAssets`.
- Hotfix build exports a complete `ABManifest` for runtime lookup, but only ships bundles changed relative to the current Major full baseline.
- The remote package remains self-contained for the latest full-baseline-to-hotfix state. No package chain or adjacent-version-only update rule is introduced.
- The client stays simple: download the manifest's delivery list, load hotfix bundles first, and fall back to `StreamingAssets` for unchanged baseline bundles.

## Candidate Code Changes

- `RepositoryCommit` should add a serialized `BuildType` string or enum-compatible field. `BuildRepositoryFacade.Commit()` fills it from `BuildPackageRequest.BuildType`.
- AB hotfix baseline lookup should use `FileBuildRepository.ListCommits()` / `BuildRepositoryFacade.ListCommits()` to find the latest same channel/backend/Major commit with `BuildType == Full`. Old commits may be interpreted by version fallback only while migrating.
- `ABManifest` should add a serialized `DeliveryBundles` list of `ManifestBundleEntry` or a lightweight bundle-info DTO. `BundleEntries` remains the complete runtime lookup table.
- `TaskScanABHotfixDiff` should compare current bundle artifacts against the Full baseline, not the current HEAD, for AB hotfix delivery calculation.
- `TaskOrganizeOutput` should copy all bundles for Full builds and only `DeliveryBundles` for Hotfix builds.
- `TaskWriteABPackageManifest` and `HotfixPackageSizeGuard` should size-check delivery bundles, not all complete-manifest bundles.
- `ABHotfixBackend.GetBundleDownloadList()` should prefer `DeliveryBundles`, falling back to `BundleEntries` for old manifests.

## Decisions Already Leaning Stable

- Do not filter AA package files at Push time. AA catalog correctness is more important than publish-size optimization.
- Do not introduce AB package chains or adjacent-only upgrade requirements.
- Do not make Repository Push reinterpret package internals. Push remains whole-package publication plus root `PackageIndex.json`.
- Do not remove the existing runtime fallback from `CurrentGUIDRoot/bundles` to `StreamingAssets/bundles`.

## Discussion Notes

- AA can theoretically build a correct catalog first and then filter Push files to only publish changed bundles. This is not the preferred direction: it moves duplicate-file comparison from download time to publish time, adds catalog/package mismatch risk, and does not clearly improve the current simpler client flow.
- AA hotfix output is expected to grow as more hotfix assets accumulate in the Addressables Hotfix group. This follows the official mechanism needed for correct catalog generation.
- AB can be optimized more aggressively because it owns the manifest and downloader. It can publish a complete runtime manifest while limiting the delivery bundle list to cumulative changes relative to the Full baseline.
- Under package-splitting strategies, both AA and AB still need download-time comparison/migration logic:
  - AA can keep or copy matching local bundles into the new package path and download only missing bundles.
  - AB becomes harder if the remote only contains a one-time pure diff package, because the client cannot compare against a complete remote package shape. This is why the preferred AB direction is complete manifest plus explicit delivery list, not a package chain of opaque deltas.

## Open Questions Before Promotion

- Exact `ABManifest` binary schema version bump and serializer regeneration steps.
- Whether `DeliveryBundles` should reuse `ManifestBundleEntry` or use a smaller DTO to avoid serializing runtime-only fields indirectly.
- Exact old-commit fallback rule for discovering the first Full baseline when `RepositoryCommit.BuildType` is missing.
- Whether `TaskVerifyBuildResult` should continue verifying all built bundle outputs before delivery filtering; current recommendation is yes.
- UI/CLI wording for AB Diff Preview: preview still compares current output to repository HEAD, while official AB Hotfix delivery should compare against Full baseline.

## Promotion Criteria

- Every code touchpoint above has a concrete implementation rule.
- Binary serialization compatibility is specified.
- Acceptance cases cover first hotfix, later cumulative hotfix, no-diff hotfix, and direct full-to-latest-hotfix update.
- Documentation clearly separates current behavior from the proposed AB delivery change.
