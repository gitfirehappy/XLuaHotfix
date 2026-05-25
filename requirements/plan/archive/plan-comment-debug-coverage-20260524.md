# Comment and Debug Coverage Plan

## Goal
Make recent build/repository/hotfix task code easier to diagnose in Unity Console and easier to maintain in source, without changing behavior.

## Scope
- `TaskScanAddressableHotfixDiff`
- `TaskScanABHotfixDiff`
- `TaskWritePackageIndex`
- `RepositoryPreviewRunner`
- `BuildProjectManager`
- `AABuildBackend`
- `ABBuildBackend`
- narrowly related recent repository files if a message path needs the same wording cleanup

## Changes
1. Add short Chinese comments at non-obvious boundaries:
   - DAG stop-after preview paths
   - Full build skip vs Hotfix diff scan
   - PackageIndex as remote latest-package pointer
   - Repository commit outside DAG
   - AB preview temp output and cleanup

2. Improve Debug/log wording:
   - Use `[Component]` prefix for Unity Console filtering.
   - Use Chinese human wording with English technical terms: `Diff Preview`, `ArtifactDelta`, `PackageIndex`, `Repository HEAD`, `DAG`.
   - Include action + reason + key path/count/version where useful.
   - Keep `BuildMessage.Message` free of `[Component]` prefix.

3. Add missing diagnostic coverage for important paths:
   - AA Full build skips diff scan but still records current artifacts.
   - AA/AB no-change diff result.
   - PackageIndex write path and backend/version.
   - Preview pipeline starts/stops and cleanup.
   - Repository commit failure context.

4. Verify no behavior drift:
   - No DAG dependency/order changes.
   - No new file writes except logs/comments.
   - No new exception behavior.
   - `dotnet build XLuaHotfix.sln` passes with existing warnings only.

## Approval Checklist
- [ ] Comment style: Chinese sentences with English technical terms such as `DAG`, `Diff Preview`, `ArtifactDelta`, `PackageIndex`.
- [ ] Debug style: keep `[Component]` prefix only in direct `Debug.Log*`, not in `BuildMessage.Message`.
- [ ] Scope: limit edits to recent build/repository/hotfix task implementation files listed above.
- [ ] Behavior: comments/logs only; no build flow, artifact format, or hotfix distribution changes.
