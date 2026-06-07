# Staging Diff Build Failure Review

> **Date**: 2026-06-06
> **Reviewer**: Codex
> **Scope**: Repository staging diff preview for AA/AB, `RepositoryPreviewRunner`, AB bundle preview task, Unity package resolution, and the provided Unity Console error.
> **Method**: Static source review, package lock inspection, local PackageCache inspection, and official Unity 2022.3 package documentation lookup.

## Summary

No code was changed.

The copied stack trace is from the AB Repository staging preview path. The failure is not caused by repository snapshot comparison itself. AB staging preview executes `TaskBuildBundles`, which calls `BuildPipeline.BuildAssetBundles`; Unity then compiles scripts for that build context and fails inside `com.unity.collections@1.2.4`.

The direct compiler error is:

```text
Library\PackageCache\com.unity.collections@1.2.4\Unity.Collections\NativeList.cs(839,24): error CS7036:
There is no argument given that corresponds to the required formal parameter 'safety' of
'NativeArray<T>.ReadOnly.ReadOnly(void*, int, ref AtomicSafetyHandle)'
```

This indicates a package/API mismatch around `NativeArray<T>.ReadOnly` safety-handle constructor availability. The project is on Unity `2022.3.62f3`, while `packages-lock.json` resolves `com.unity.collections` to `1.2.4` as a transitive package. Unity's official 2022.3 Collections documentation mirrors currently disagree on the exact latest released version (`docs.unity3d.com` shows `2.6.6`, while `docs.unity.cn` shows `2.4.1`), but both list a newer 2.x release line for Unity 2022.3 and both list `1.2.4` only as an older available package version.

AA needs one more direct log sample before saying it is the same staging-preview code path: the current AA preview implementation stops after `TaskScanAddressableHotfixDiff` and does not call Addressables content build. However, any AA operation that actually reaches Addressables content build can be blocked by the same global script compilation error until the Collections package resolution is fixed.

## Findings

### [P0] `com.unity.collections@1.2.4` is incompatible with the current build compile context and blocks AB staging preview

**Files**

- `Packages/packages-lock.json:130`
- `Packages/manifest.json:2`
- `ProjectSettings/ProjectVersion.txt:1`
- `ProjectSettings/ProjectSettings.asset:686`
- `Library/PackageCache/com.unity.collections@1.2.4/Unity.Collections/NativeList.cs:834`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AB/TaskBuildBundles.cs:124`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:70`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:82`

**Problem**

`Packages/manifest.json` does not directly pin `com.unity.collections`, but `packages-lock.json` resolves it to `1.2.4`. The failing source line in `NativeList.AsParallelReader()` has this shape:

```csharp
#if ENABLE_UNITY_COLLECTIONS_CHECKS
return new NativeArray<T>.ReadOnly(m_ListData->Ptr, m_ListData->Length, ref m_Safety);
#else
return new NativeArray<T>.ReadOnly(m_ListData->Ptr, m_ListData->Length);
#endif
```

The Unity compiler is taking the non-checks branch, but the visible `NativeArray<T>.ReadOnly` constructor requires the `AtomicSafetyHandle` parameter. `ProjectSettings.asset` only defines `HOTFIX_ENABLE;DOTWEEN` for Standalone, so this project is not intentionally defining `ENABLE_UNITY_COLLECTIONS_CHECKS` to force the other branch.

The AB staging preview includes `TaskBuildBundles` in its whitelist and stops after `TaskScanABHotfixDiff`. `TaskBuildBundles` calls `BuildPipeline.BuildAssetBundles`, so this preview is enough to trigger Unity's script compilation failure before the repository diff task can complete.

**Impact**

AB `Refresh Staging` cannot produce `ArtifactDelta` or AB hotfix delivery preview. The UI reports a generic preview failure, and the temporary output directory is cleaned, but the underlying issue remains a global package compile failure. This can also block official AB builds and any other build operation that compiles the same player-side package assemblies.

**Recommendation**

Do not edit files under `Library/PackageCache`; that is generated package cache and will be overwritten.

Fix package resolution at the Package Manager level:

- Add an explicit `com.unity.collections` dependency to `Packages/manifest.json` using a version validated for Unity 2022.3. Official references checked during this review:
  - `https://docs.unity3d.com/2022.3/Documentation/Manual/com.unity.collections.html` currently lists `2.6.6` as released for Unity Editor 2022.3.
  - `https://docs.unity.cn/Manual/com.unity.collections.html` currently lists `2.4.1` as released for Unity Editor 2022.3.
- Let Unity regenerate `Packages/packages-lock.json`, then commit both manifest and lockfile if the package graph resolves cleanly.
- If `com.unity.feature.2d` is not needed, consider removing that feature package instead of carrying the 2D package dependency chain that brings Collections into the project.
- Do not use `ENABLE_UNITY_COLLECTIONS_CHECKS` as the primary fix. It may hide this exact branch on one target, but it does not correct the package/API mismatch and can leave other build targets broken.

Verification after the package change should be:

- Reopen Unity or force a package refresh and confirm the `CS7036` error is gone.
- Run AB Repository `Refresh Staging`; it should pass the compile phase and either produce a staging diff or fail on a later domain-specific build error.
- Run AA Repository `Refresh Staging` with a log that starts `AA Diff Preview start`; if it still fails with `CS7036`, the same package fix should be considered incomplete.

### [P1] Repository preview throws away task-level failure details

**Files**

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:31`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:82`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:84`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildResult.cs:22`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/BuildTaskResult.cs:13`

**Problem**

`DAGScheduler.Execute()` returns `BuildResult.TaskResults`, and each failed task can carry `ErrorCode` and `ErrorMessage`. `RepositoryPreviewRunner` currently reduces any failed AA/AB preview to:

```csharp
throw new InvalidOperationException("AA diff preview pipeline failed.");
throw new InvalidOperationException("AB diff preview pipeline failed.");
```

This discards task-level evidence before it reaches `RepositoryStatusPanel`, so the panel message only shows the wrapper failure. Developers must manually search the full Unity Console to find the real compiler or task failure.

**Impact**

Preview failures are slower to triage and can be misattributed to repository diff logic. In this incident, the actionable failure was in `Library/PackageCache/com.unity.collections@1.2.4`, but the panel surfaces only `AB diff preview pipeline failed.`

**Recommendation**

When `result.Success == false`, format the first failed `BuildTaskResult` into the thrown message, including the backend label, task name if available, error code, and error message. If task names are not currently stored in `BuildTaskResult`, add a small formatter in `RepositoryPreviewRunner` that walks `result.TaskResults` and prints the first failed entry with its index. Keep the full Unity Console compiler log as the detailed source for package compile failures.

### [P2] The provided evidence shows AB preview only; AA staging failure still needs a direct AA log

**Files**

- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:14`
- `Assets/FYAsset/Scripts/Build/Repository/Editor/RepositoryPreviewRunner.cs:25`
- `Assets/FYAsset/Scripts/Build/Pipeline/Editor/Tasks/AA/TaskScanAddressableHotfixDiff.cs:38`

**Problem**

The copied logs all start with:

```text
[RepositoryPreviewRunner] AB Diff Preview start
```

The current AA preview path only whitelists `TaskScanAddressableHotfixDiff` and stops after that task. That task scans Addressables source entries and repository HEAD; it does not call `AddressableAssetSettings.BuildPlayerContent`.

**Impact**

The Collections compiler error is real and can affect both backend build operations, but the supplied stack trace only proves the AB staging preview path. If the AA Repository panel also fails, it should produce an `AA Diff Preview start` log or a different Addressables content-build stack.

**Recommendation**

Collect one AA-specific Console log after clicking the AA Repository `Refresh Staging`. If the log starts with `AA Diff Preview start` and still reaches `CS7036`, then the AA preview path is unexpectedly triggering compilation and should be reviewed separately. If AA preview passes after the package fix, keep this as an AB-triggered compile failure with a shared package root cause.

## Review Notes

- This review intentionally avoids code and package changes because project guidance requires explicit approval before modifying build flow, package distribution, or dependency behavior.
- The fastest durable fix is package graph correction, not Repository code changes.
- The most useful Repository-side follow-up is better failure surfacing in `RepositoryPreviewRunner`, after the package compile blocker is removed.
