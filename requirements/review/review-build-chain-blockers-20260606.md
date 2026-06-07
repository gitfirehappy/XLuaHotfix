# Build Chain Blocker Review

> **Date**: 2026-06-06
> **Reviewer**: Codex
> **Scope**: AA and AB Repository staging diff, official AA/AB Full and Hotfix build chains, package resolution, collector input, AB output verification, repository commit, and repository push.
> **Method**: Static source review, current asset/config inspection, repository state inspection, and targeted filesystem evidence collection. Unity build execution was not run because it is side-effectful and the request is for review.

## Executive Summary

No runtime or build code was changed.

The copied Console error is a real first blocker, but it is not the only blocker in the chain. The current project state has several independent failures that can stop staging diff or official builds even after the Collections compiler error is fixed.

Current confirmed blockers:

- The project resolves `com.unity.collections` to `1.2.4` under Unity `2022.3.62f3`; the supplied compiler error blocks AB staging preview at `TaskBuildBundles`.
- AA staging preview is created as a Hotfix diff and currently requires a Repository HEAD. `BuildData/Snapshots` does not exist, so AA staging diff can fail on an empty repository.
- AB staging preview is also forced through the Hotfix branch and requires a same-Major AB Full baseline. `BuildData/Snapshots` does not exist, so AB staging diff will fail after earlier AB build blockers are cleared.
- The active AB collector configuration contains multiple RawFile candidates in the same unlabeled `PackTogetherByLabel` bundle groups. `TaskBuildBundles` has an explicit fatal guard for more than one RawFile per bundle.
- AB verification counts every file under `_temp`, but Unity `BuildPipeline.BuildAssetBundles` writes extra sidecar manifest files in that directory. Any AB path that reaches verification with serialized bundles is likely to fail `COUNT_CROSS_CHECK`.

Secondary release-chain blockers and risks:

- Build version is incremented and saved before the build succeeds; failed builds can advance `VersionDataBase` without a matching package or repository commit.
- `PushTargets` is empty, so Repository Push is blocked even after staging/build issues are fixed.
- Preview error wrapping hides the task-level failure details and makes triage slower.

## Findings

### [P0] Package compile failure blocks AB staging and any build path that compiles the same package graph

**Evidence**

- `ProjectSettings/ProjectVersion.txt:1` uses Unity `2022.3.62f3`.
- `Packages/manifest.json:5` includes `com.unity.feature.2d`.
- `Packages/packages-lock.json:130` resolves `com.unity.collections`.
- `Packages/packages-lock.json:131` pins the resolved version to `1.2.4`.
- `Packages/packages-lock.json:147` shows `com.unity.feature.2d` as a root package; `packages-lock.json:152` pulls `com.unity.2d.animation`, and `packages-lock.json:10` shows that chain depends on `com.unity.collections`.
- The supplied Unity Console error is:

```text
Library\PackageCache\com.unity.collections@1.2.4\Unity.Collections\NativeList.cs(839,24): error CS7036:
There is no argument given that corresponds to the required formal parameter 'safety' of
'NativeArray<T>.ReadOnly.ReadOnly(void*, int, ref AtomicSafetyHandle)'
```

AB staging preview reaches `TaskBuildBundles` through `RepositoryPreviewRunner.RunABPreviewDetailed`:

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:76` whitelists `TaskBuildBundles`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:124` calls `BuildPipeline.BuildAssetBundles`.

**Impact**

AB `Refresh Staging` cannot get past the compilation/build-bundle phase. Official AB Full/Hotfix builds are blocked for the same reason. Official AA builds can also be blocked when they reach Addressables content build if the global script compile context includes the same package error.

This is not a Repository diff algorithm failure. It is a package graph/compiler compatibility failure surfaced by the AB staging path.

**Recommendation**

Do not patch `Library/PackageCache`. Fix package resolution through `Packages/manifest.json` and let Unity regenerate `Packages/packages-lock.json`.

Use one of these approaches:

- Add an explicit `com.unity.collections` version validated for Unity 2022.3.
- If the 2D feature package is not needed, remove the feature package that brings the old Collections dependency chain.

Verification after the package fix:

- Reopen Unity or force Package Manager resolution and confirm the `CS7036` error is gone.
- Re-run AB Repository `Refresh Staging`; expect the next blocker to be collector/raw-file handling or AB baseline state, not the same compiler error.
- Run an official AA content build or AA staging path separately; AA staging has a different repository-HEAD blocker described below.

### [P0] AA staging diff fails on an empty Repository HEAD

**Evidence**

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:24` creates the AA preview context as `BuildType.Hotfix`.
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:27` whitelists only `TaskScanAddressableHotfixDiff`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AA/TaskScanAddressableHotfixDiff.cs:74` diffs current Addressables source against `GetBaselineArtifacts(request)`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AA/TaskScanAddressableHotfixDiff.cs:81` calls `BuildRepositoryFacade.GetHeadCommit(...)`.
- `Assets/FYAsset/Scripts/Build/Repository/Editor/FileBuildRepository.cs:58` throws when the repository has no HEAD.
- Current filesystem state contains only `BuildData/Reports/AB`; no `BuildData/Snapshots` directory exists.

**Impact**

AA Repository `Refresh Staging` can fail before producing an `ArtifactDelta` on a fresh repository. This is independent of the Collections compiler error.

Official AA Hotfix builds have the same first-hotfix problem because they also run `TaskScanAddressableHotfixDiff` in the Hotfix branch. AA Full builds skip the hotfix diff branch and can create the initial repository commit if the rest of the build succeeds.

**Recommendation**

Decide the intended first-run behavior:

- If staging diff should work before the first commit, treat missing HEAD as an empty baseline in the AA preview path.
- If a baseline is required by design, surface a clear action message: run AA Full build first to create Repository HEAD.

Verification:

- With no `BuildData/Snapshots`, AA staging should either produce a full Added delta or show a specific "missing AA Full/HEAD baseline" error, not a generic preview failure.
- After a successful AA Full build and repository commit, AA staging should compare against that HEAD.

### [P0] AB collector settings currently create RawFile multi-asset bundle failures

**Evidence**

Active AB collector config:

- `Assets/FYAsset/CollectorData/CollectorSetting.asset:16` defines package `ProjectName1`.
- Many groups are enabled and use `BundlePackingMode: 2`, which is `PackTogetherByLabel`.
- These groups have empty `Labels: []`, so unlabeled assets in the same group collapse to the same bundle key.
- `Assets/FYAsset/CollectorData/CollectorSetting.asset:51-61` enables `Dialogue`.
- `Assets/FYAsset/CollectorData/CollectorSetting.asset:168-178` enables `Test`.
- `Assets/FYAsset/CollectorData/CollectorSetting.asset:201-211` enables `XLuaFramework`.

Scanner/classifier behavior:

- `Assets/FYAsset/Scripts/Build/Collector/Editor/Rules/CollectAll.cs:10-18` excludes `.meta`, `.cs`, `.dll`, `.asmdef`, `.asmref`, and `.gitignore`, but not `.lua`, `.csv`, `.json`, `.txt`, `.pdf`, `.ttf`, native libraries, or `.inputactions`.
- `Assets/FYAsset/Scripts/Build/Collector/Editor/AssetClassifier.cs:52-55` classifies unsupported/non-serialized extensions as `RawFile`.
- `Assets/FYAsset/Scripts/Build/Collector/Editor/CollectionScanner.cs:686` uses the group packing mode for non-scene assets.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:243-247` fails the build when a bundle contains more than one RawFile.

Static extension scan using the current classifier rules found multiple raw-like files in the same current groups:

```text
Dialogue: raw=7
Test: raw=19
XLuaFramework: raw=16
Plugins: raw=16
TextMeshPro: raw=12
```

Examples include `.lua` files under `Assets/Dialogue/LuaScripts/...`, `.lua/.csv/.json` files under `Assets/Test/...`, and `.lua` files under `Assets/XLuaFramework/LuaScripts/...`.

**Impact**

After the Collections compiler blocker is removed, AB staging/AB builds can fail inside `TaskBuildBundles` with `RawfileMultiAsset` before manifest generation and diffing.

There is also a related correctness risk: when a group contains both serialized output and RawFile entries with the same bundle name, `TaskBuildBundles.cs:240-242` skips RawFile copies if `processedOutputs` already contains that bundle name. That can silently drop non-serialized files instead of failing.

**Recommendation**

Do not try to fix this by broadening `CollectAll` blindly. The collector needs explicit packaging intent for non-serialized assets:

- Exclude tool/config/editor-only folders from AB collection.
- Put Lua/text/csv/json files into a RawFile-safe packing mode, normally one raw file per bundle, or add a dedicated raw-file bundle naming rule.
- Do not collect native plugin binaries and plugin metadata into resource ABs unless there is a verified runtime need.
- Add validation before `TaskBuildBundles` that reports exact raw-file conflicts by group, bundle name, and source asset paths.

Verification:

- Run the collector scan in Unity and confirm there are no multi-file RawFile bundle groups.
- Run AB staging again and confirm it reaches manifest generation or the next expected baseline check.

### [P0] AB staging/hotfix requires a same-Major Full baseline, but current repository snapshots are absent

**Evidence**

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:65` creates the AB preview context as `BuildType.Hotfix`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs:38` enters the Hotfix branch when `BuildType` is Hotfix.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs:57` calls `FindFullBaseline`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs:61` fails when the same-Major Full baseline is missing.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskScanABHotfixDiff.cs:117-120` only accepts commits with `BuildType == Full` and the same major version.
- Current filesystem state contains no `BuildData/Snapshots`.

**Impact**

Even after fixing Collections and RawFile conflicts, AB Repository `Refresh Staging` will not be able to produce hotfix delivery information without an AB Full baseline commit for the current major version.

Official AB Hotfix builds are blocked by the same baseline requirement. AB Full builds are the correct way to create the initial baseline, but they still have to pass the package and collector/build verification blockers first.

**Recommendation**

Keep the baseline requirement for official AB Hotfix builds, but make staging UX explicit:

- For AB staging, either allow current-vs-empty/HEAD diff while showing "delivery unavailable until AB Full baseline exists", or fail with a specific missing-baseline message before running expensive bundle build work.
- Ensure the first successful AB Full build commits a `BuildType.Full` snapshot before AB hotfix/staging delivery is expected to work.

Verification:

- With no snapshots, AB staging should show a direct baseline error instead of a generic `AB diff preview pipeline failed`.
- After a successful same-major AB Full commit, AB staging should calculate both HEAD delta and delivery bundles.

### [P0] AB verification counts Unity-generated sidecar manifest files as build output

**Evidence**

- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:124` uses `BuildPipeline.BuildAssetBundles`.
- Unity AssetBundle builds normally emit the bundle files plus sidecar `.manifest` files and a root manifest in the output directory.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:175` reads only `unityManifest.GetAllAssetBundles()` into `BundleBuildInfo`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:48-54` records only business manifest bundle names in `knownFiles`.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:124-129` treats every file in `_temp` that is not in `knownFiles` as an orphan warning.
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskVerifyBuildResult.cs:136-144` compares `Directory.GetFiles(tempDir).Length` against `manifest.BundleEntries.Count` and makes mismatches fatal.

**Impact**

Any AB build that reaches verification with at least one serialized/scene AssetBundle can fail `COUNT_CROSS_CHECK` because Unity's sidecar files increase the actual file count. This affects AB staging preview and official AB Full/Hotfix builds.

This issue is hidden today by earlier blockers, but it is in the same AB build chain and should be fixed before declaring AB staging/build healthy.

**Recommendation**

Make verification compare only expected deployable bundle files:

- Ignore Unity sidecar `.manifest` files and the root manifest file in orphan/count checks.
- Prefer checking the set of `ABManifest.BundleEntries.BundleName` against file existence/hash rather than raw directory file count.
- If sidecar files are intentionally kept for diagnostics, classify them separately and never count them as bundle payload.

Verification:

- Run an AB build with one known serialized bundle.
- Confirm `_temp` can contain Unity sidecar files while `TaskVerifyBuildResult` still passes when all deployable bundle files are correct.

### [P1] Version state is saved before build success and can drift from repository/package state

**Evidence**

- `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildProjectManager.cs:30-34` increments and saves the Full version before `RunBuild`.
- `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildProjectManager.cs:57-61` increments and saves the Hotfix version before `RunBuild`.
- There is no rollback path when `RunBuild` fails.
- `Assets/Build/VersionDataBase.asset:15-20` currently stores `1.0.0`.
- `Assets/StreamingAssets/BuildIndex.json` currently records a `4.0.0` build identity, so local startup data and version database are already out of sync.

**Impact**

Repeated failed builds consume product versions without producing matching package output or repository commits. For AB this can also make same-Major Full baseline checks harder to reason about. For Repository UI, the current version can diverge from HEAD and package history.

**Recommendation**

Make version advancement transactional:

- Stage the next version in memory.
- Build package output.
- Commit repository and export required local data.
- Save `VersionDataBase` only after the build chain succeeds, or record an explicit failed-build state.

Verification:

- Force a controlled build failure and confirm `VersionDataBase` does not advance unless the package and repository commit are produced.

### [P1] Repository preview hides the failing task and error code

**Evidence**

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:33` throws only `AA diff preview pipeline failed`.
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:84` throws only `AB diff preview pipeline failed`.
- `DAGScheduler.Execute` returns `BuildResult.TaskResults`, and each failed task contains `ErrorCode` and `ErrorMessage`.

**Impact**

Developers see generic Repository panel failures and must search the full Unity Console for the actual root cause. This caused the current package compiler issue to appear as a repository staging problem.

**Recommendation**

When preview fails, include:

- backend label
- first failed task index/name if available
- `ErrorCode`
- `ErrorMessage`
- whether the failure was fatal

For compiler failures outside a task result, keep the generic wrapper but add a message telling developers to check the compiler error immediately above the preview failure.

### [P2] Repository Push is blocked because no push target is configured

**Evidence**

- `Assets/Resources/FYAssetSettings.asset:21` has `PushTargets: []`.
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryStatusPanel.cs:369` displays "No Push Target configured."
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryStatusPanel.cs:928` throws `No push target configured.` when no target can be created.

**Impact**

This does not block staging diff or package build, but it blocks the release publication step after a valid Repository HEAD exists.

**Recommendation**

Configure at least one `LocalDirectory` push target before testing end-to-end release publication. If an empty path is intended to publish to the default output root, add an explicit target with an ID and empty path.

## Reviewed Non-Blockers

These areas were reviewed and are not current first-order build blockers:

- AA `AABuildPipelineConfig.asset` does not contain `TaskPrepareContext`, but `BuildPipelineBackbone` defines a separate AA backbone and `AABuildBackend` seeds `BuildPackageRequest` and `BuildType` directly.
- Addressables active builder is index `3`, which maps to `BuildScriptPackedMode` in the current `m_DataBuilders` order.
- `Assets/FYAsset/Editor/Generated/HotfixGroupUndoLog.json` is absent, so `TaskMoveAddressableHotfixGroups.HasPendingMoves` is not currently blocking AA Hotfix. The existing `HotfixGroup` entries may still be a content correctness concern for Full builds, but they are not an immediate scheduler failure.
- The fixed AB Repository panel can run while global `UseABBackend` is `0`. Current AB preview uses the request backend for repository channel/diff and stops before package organization; `TaskPrepareContext` still reading global backend is a metadata consistency risk, not the first current staging blocker.

## Suggested Remediation Order

1. Fix the Unity package graph so the project compiles without the Collections `CS7036` error.
2. Decide and implement first-run Repository staging semantics for AA and AB.
3. Fix AB RawFile collection/packing conflicts in the current collector setting.
4. Fix AB verification to ignore Unity sidecar manifest files.
5. Run AB Full to create a same-Major baseline, then run AB staging.
6. Run AA Full to create Repository HEAD, then run AA staging.
7. Make version advancement transactional.
8. Configure a push target and test Repository Push.

## Verification Gaps

This review did not run Unity builds or staging previews. The code/config evidence is enough to identify the blockers above, but final closure requires fresh Unity verification:

- Unity editor compile after package resolution.
- AA Repository `Refresh Staging` on empty repository and after AA Full commit.
- AB Repository `Refresh Staging` after package fix, collector fix, and AB Full baseline.
- Official AA Full/Hotfix package builds.
- Official AB Full/Hotfix package builds.
- Repository Push to a configured local directory target.
