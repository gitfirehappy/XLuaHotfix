# Plan: Build Pipeline Sequence List Editor Simplification

**Date**: 2026-07-09  
**Status**: Implemented / Verified / Pending developer sign-off  
**Origin**: A2 follow-up after `plan-linear-build-pipeline-runner-20260709.md`  
**Scope**: Editor/build-pipeline simplification

## Summary

Replace the Pipeline panel GraphView with a direct vertical Task sequence list. The list order is the execution order. Keep Refresh, Validate, Build Mode, Build, build status lights, and copyable validation details.

## Approved Changes

1. Draw the Task sequence list inside `PipelinePanel`; do not add a separate complex control file.
2. Each row shows order, status light, TaskName, and dynamic Resolved/Unresolved state.
3. Keep `BuildExecutionOptions.TaskStatusChanged` and map it to row status lights: Idle, Pending, Running, Success, Failed, and Skipped.
4. Remove `BuildGraphView`, `BuildTaskNode`, `BuildGraphLayoutEngine`, `EdgeStyle`, their `.meta` files, `.csproj` compile items, and unused `UnityEditor.GraphViewModule` references.
5. Remove `TaskEntry.DependsOn` and serialized `DependsOn` blocks from tracked pipeline config assets.
6. Keep `IBuildTask.DependsOn` as a validation-only ordering guardrail.
7. Make `BuildPipelineRunner` validate only `IBuildTask.DependsOn` against earlier tasks in the configured list.
8. Make `BuildPipelineBackbone` create only `TaskName` entries.
9. Do not keep Graph right-click task creation or node source-opening behavior.
10. Align requirements, context, and docs from Task graph wording to Task sequence list wording.
11. Remove `TaskEntry.Enabled` as leftover graph-era optional-task state; keep `Resolved` only as dynamic editor diagnostics.

## Non-Goals

- No Task add/delete/reorder UI in this plan.
- No runtime loading, hot-update, package format, or Lua/C# bridge changes.
- No replacement DAG/graph visualization.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore` exited 0. Existing `System.Net.Http` conflict warnings remain.
- `git diff --check` exited 0. Git reported LF/CRLF working-copy warnings only.
- Static checks:
  - no active `UnityEditor.Experimental.GraphView`
  - no active `BuildGraphView`, `BuildTaskNode`, `BuildGraphLayoutEngine`, `EdgeStyle`, or `EdgeStyles`
  - `TaskEntry` has no `DependsOn`
  - tracked `BuildPipelineConfig` assets have no `DependsOn:`
  - `TaskEntry` has no `Enabled`
  - tracked `BuildPipelineConfig` assets have no Task `Enabled:` entries
  - `BuildPipelineRunner` still validates `IBuildTask.DependsOn` ordering
