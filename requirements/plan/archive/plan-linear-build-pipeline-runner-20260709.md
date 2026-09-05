# Linear Build Pipeline Runner Plan 2026-07-09

> **Status**: Executed / Verified / Archived 2026-07-14
> **Archive Note**: Later task-contract cleanup superseded the retained validation declarations without restoring DAG execution.
> **Requirement ID**: linear-build-pipeline-runner-20260709
> **Origin**: A2 from `requirements/plan/drafts/draft-fyasset-architecture-review-20260707.md`
> **Scope**: Replace DAG scheduling with deterministic linear task execution.

## Goal

Simplify the build pipeline executor to match the current real pipeline shape: a fixed, deterministic, linear task list
with optional preview filtering.

## Locked Decisions

1. Replace `DAGScheduler` with `BuildPipelineRunner`.
2. Execute tasks strictly in the provided list order.
3. Preserve lightweight `Validate`, `stopAfterTaskName`, and `taskWhitelist` behavior.
4. Validation may confirm that declared dependencies appear earlier in the list, but must not topologically sort.
5. Remove Kahn topology sorting, indegree/successor graph state, batch/deadlock/cycle execution logic, and DAG wording
   from active code paths.
6. Keep `IBuildTask`, `BuildContext`, `BuildTaskResult`, task `DependsOn`, and task read/write declarations intact.
7. Do not change task implementation order, build artifact format, or repository/hotfix semantics.

## Implementation Checklist

1. Introduce `BuildPipelineRunner` in the existing build pipeline editor area.
2. Port only the needed runner surface:
   - `Validate(IReadOnlyList<IBuildTask> tasks)`
   - `Execute(IReadOnlyList<IBuildTask> tasks, BuildContext context, string stopAfterTaskName = null,
     ISet<string> taskWhitelist = null)`
3. Update AA, AB, repository preview, and editor validation callers to use `BuildPipelineRunner`.
4. Remove the active `DAGScheduler` implementation and stale DAG-specific comments.
5. Keep task execution events/status propagation compatible with current pipeline UI.

## Acceptance Criteria

- Build tasks run in declared list order.
- `stopAfterTaskName` still stops after the named executed task.
- `taskWhitelist` still skips tasks outside the preview set.
- Dependency validation fails when a task depends on a later/missing task.
- No active code references `DAGScheduler`.
- No active code describes the runner as topological, DAG-batched, or parallel-capable.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static checks:
  - no active `DAGScheduler` references
  - `BuildPipelineRunner` is used by AA build, AB build, repository preview, and pipeline panel validation

## Non-Goals

- No task graph editor redesign.
- No incremental build cache.
- No preview cache change.
- No task contract rewrite.
> 2026-07-11 follow-up: `plan-build-panel-task-slim-20260711.md` removes the public Validate surface and Task dependency/read/write declarations while retaining task resolution, backbone checks, stop-after, and whitelist execution.
