# YooAsset Gap Analysis

> **Date**: 2026-04-27
> **Source**: Systematic comparison of YooAsset full feature set vs our implemented + planned features
> **Status**: Discussion input for subsequent planning sessions

---

## High Priority (required for main pipeline closure)

| # | YooAsset Feature | Our Status | Recommendation |
|---|-----------------|:---:|------|
| 1 | **Shader auto-collection** — TaskGetBuildMap automatically bundles shaders into `unityshaders.bundle` | ❌ Missing | Add shader auto-collection to E4 dependency analysis, or as a standalone E5-2 Task. Real projects always have shaders |
| 2 | **Build verification** — TaskVerifyBuildResult validates output integrity post-build | ❌ Missing | E5-2 currently only has TaskBuildBundles + TaskOrganizeOutput. Add verification Task for CRC/file integrity |
| 3 | **Asset→Bundle tag propagation** — Asset Labels propagate up to Bundle Tags | 🟡 Incomplete | E6 TaskGenerateManifest needs propagation logic. Bundle Tags are the basis for download filtering ("DLC-1" / "mandatory") |
| 4 | **Cache cleanup** — ClearUnusedBundleFiles removes old bundles no longer referenced by current manifest | ❌ Missing | B4B9 only handles download, not cleanup. Disk grows unbounded |

## Medium Priority (UX / maintainability)

| # | YooAsset Feature | Our Status | Recommendation |
|---|-----------------|:---:|------|
| 5 | **Build report** — TaskCreateReport: bundle count/size/type statistics | ❌ Missing | E6 can add optional report output for build debugging |
| 6 | **File naming mode** — FileNameStyle (HashName / BundleName / BundleName_HashName) | 🟡 Incomplete | E2 uses fixed 3-segment format. Add naming mode switch to BuildParameters |
| 7 | **Group toggle** — IActiveRule / EnableGroup | ❌ Missing | Add `bool Enabled` field to CollectorGroup (simpler than YooAsset's IActiveRule interface) |
| 8 | **Download resume + retry** — ResumeDownload / FailedTryAgain | ❌ Missing | B4B9 added concurrency control. Mobile hotfix needs resume |

## Low Priority (deferrable)

| # | YooAsset Feature | Recommendation |
|---|-----------------|------|
| 9 | Encryption (IEncryptionServices) | Not needed for current phase |
| 10 | Profile variable substitution system | Constants.cs is sufficient |
| 11 | Multi-package + multi-filesystem composition | Single package is enough |

## Decision Items (Resolved 2026-04-28)

- [x] #1 Shader auto-collection → **E5-2 new Task (TaskCollectBuiltins)**: independent backbone node, runs after E1-3 + before E4. General "builtin assets" pattern, first case = Shaders.
- [x] #2 Build verification → **E5-2 new Task (TaskVerifyBuildResult)**: 6 validation checks after E6 + before TaskOrganizeOutput. Error→abort, Warning→continue.
- [x] #3 Tag propagation → **E6 TaskGenerateManifest**: union of CollectedAssetInfo.Tags (which already merge Collector.Tags ∪ Group.Tags from E1-3) across all assets in the bundle. Simple set union.
- [x] #4 Cache cleanup → **Already covered** by `PackageCleaner.CleanOldBuildPackages()` (directory-level cleanup by build version). Architecture-agnostic, no Addressables dependency. No changes needed.
- [x] #5 Build report → **Absorbed into TaskOrganizeOutput** (build_summary.txt already listed in plan-E5-2). No separate plan needed.
- [x] #6 File naming mode → **E5-1 reserved**: `BundleFileNameStyle` enum in BuildPipelineConfig SO. Default = BundleName_HashName (current behavior). Switch for future use.
- [x] #7 Group toggle → **E1-4 + CollectorGroup**: `CollectorGroup.Enabled` bool (default true). E1-3 CollectionScanner skips when false. E1-4 PropertyPanel adds toggle.
- [x] #8 Download resume + retry → **Deferred**: B4B9 already has concurrency control (SemaphoreSlim). Resume/retry is a NetworkDownloader concern — evaluate when mobile platform testing begins.
- [x] #9-#11 Low priority → **Deferred indefinitely**.
