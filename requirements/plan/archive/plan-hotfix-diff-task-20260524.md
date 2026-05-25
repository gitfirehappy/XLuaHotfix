# Hotfix Diff DAG Unification Plan

## Summary
Unify AA and AB diff handling under the build DAG. Remove direct diff helper orchestration and make both build-time diff checks and standalone diff viewing run through the same task graph with early stop.

## Key Changes
- AA: keep `TaskScanAddressableHotfixDiff` at the front of the AA pipeline. It scans current Addressables source vs repository HEAD, writes `ArtifactDelta`, and standalone diff stops after this task.
- AA hotfix build continues past the task when diff is empty; group move remains task-owned.
- AB: add one diff task after bundle construction and before downstream packaging/finalization. It scans the built bundle outputs vs repository HEAD, writes `ArtifactDelta`, and standalone diff stops after this task.
- Remove direct diff helper orchestration from `DifferentialProcessor`, `BuildRepositoryFacade`, and any CLI/UI code paths that previously called a diff wrapper directly.
- Keep `ArtifactDiffer` and the source/output scanners as primitives only; the workflow owns execution order.
- CLI and editor diff buttons become DAG runners that prepare `BuildContext`, select `stopAfterTaskName`, and read `ArtifactDelta` from context.

## Test Plan
- AA hotfix build still runs end to end, including empty-diff cases.
- AA standalone diff stops at `TaskScanAddressableHotfixDiff`.
- AB build still runs end to end.
- AB standalone diff stops at the new AB diff task after bundle build.
- No code path calls removed diff helper methods.
- `dotnet build XLuaHotfix.sln` passes with existing warnings only.

## Assumptions
- “Diff” here means current source/output vs repository HEAD, not commit-to-commit comparison.
- The AB diff task consumes bundle build output from the DAG, not a separate post-build helper.
- Manual reset of AA hotfix groups remains available.

---

# PackageIndex DAG Task Plan

## Summary
Move `PackageIndex.json` writing from `BuildProjectManager` into the AA/AB build DAG. Keep official Full and Hotfix builds writing `PackageIndex`, because runtime hotfix uses it as the remote latest-package pointer, not as Full-only baseline data.

## Key Changes
- Add shared `TaskWritePackageIndex`; it reads `BuildPackageRequest` and writes `PackageIndex.json` with `LatestPackage`, `LatestVersion`, and `BackendMode`.
- Add `TaskWritePackageIndex` after AA/AB package manifest tasks and before `TaskExportLocalBuildData` in both backbone arrays and existing `BuildPipelineConfig` assets.
- Remove `BuildProjectManager.UpdatePackageIndexFile()` and its direct call so PackageIndex writes only happen through DAG execution.
- Keep AA/AB diff preview as DAG stop-after flows; they stop before `TaskWritePackageIndex`, so preview cannot write PackageIndex, repository HEAD, or objects.
- Document skip and early-stop behavior in `docs/FYAsset/build-pipeline-构建管线.md`.

## Test Plan
- `dotnet build XLuaHotfix.sln` passes with existing warnings only.
- AA/AB official build graphs include `TaskWritePackageIndex` before `TaskExportLocalBuildData`.
- AA/AB diff preview still stop at their diff tasks and do not reach PackageIndex writing.
- Repository push continues to publish `BuildPathManager.PackageIndexPath`.

## Assumptions
- `PackageIndex` remains the remote hotfix entry pointer and must be updated by official Hotfix builds.
- Repository commit remains outside the DAG in this step.
