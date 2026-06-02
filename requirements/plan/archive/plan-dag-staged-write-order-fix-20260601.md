# Plan: DAG Staged Write And AB Task Order Fix

> Status: Executed; awaiting developer sign-off
> Date: 2026-06-01
> Requirement ID: dag-staged-write-order-fix-20260601
> Scope: AB build pipeline DAG validation semantics, staged `CollectedAssets` writes, and AB task execution order.

## Goal

Fix AB Pipeline validation failure caused by legacy Write-Write conflict validation:

- `TaskCollectAssets`, `TaskCollectBuiltins`, and `TaskAnalyzeDependencies` all write `CollectedAssets`.
- This is the current AB pipeline's intentional staged data flow, not a concurrent write conflict.
- `TaskCollectBuiltins` must run before `TaskAnalyzeDependencies` so builtin shader/resources assets are included in dependency analysis.

AA succeeds because the AA graph does not use this staged `CollectedAssets` chain; this plan targets the AB graph and shared scheduler semantics without changing AA behavior.

## Approved Decisions

1. `WriteKeys` no longer means exclusive write ownership.
2. `WriteKeys` means a task writes or updates the named `BuildContext` key.
3. Same-key staged writes are allowed when task dependencies define an explicit order.
4. Fatal validation remains for missing dependencies and circular dependencies.
5. Read-before-write remains a non-fatal warning.
6. `TaskCollectBuiltins` must execute before `TaskAnalyzeDependencies`.
7. The existing AB pipeline config asset must be updated to match the code-level order.
8. BuildGraph data-flow edges should show staged flow using the nearest upstream writer instead of assuming a single global producer per key.
9. This plan does not add Scene/duplicate-bundle defensive validation.
10. This plan does not redesign backend selection, output root naming, or release packaging.

## Implementation Checklist

1. Update `DAGScheduler`:
   - remove fatal `CONFLICTING_WRITE_KEYS` validation;
   - adjust comments so validation no longer claims W-W conflict blocking;
   - make `ValidatePair` non-blocking for shared writes;
   - validate whitelist executions against the effective task subset used by preview.
2. Update AB task order:
   - make `TaskAnalyzeDependencies.DependsOn` include `TaskCollectBuiltins`;
   - make `TaskBuildBundles` depend on `TaskAnalyzeDependencies`;
   - keep downstream order through manifest, verification, diff, organization, package manifest, package index, and local data export.
3. Update default and current AB pipeline config:
   - reorder `BuildPipelineBackbone` AB task list to `CollectAssets -> CollectBuiltins -> AnalyzeDependencies`;
   - add default dependencies for the AB backbone;
   - update `Assets/Build/BuildPipelineConfig.asset` with the same order/dependency chain.
4. Update BuildGraph data-flow rendering:
   - derive data-flow edges from topological order;
   - for each `ReadKey`, connect to the nearest previously executed writer for that key;
   - keep edges display-only.
5. Align knowledge and tracking:
   - update `context/architecture/resource-build-and-release.md`;
   - record the mistake in `context/mistakes/implementation-pitfalls.md`;
   - update `requirements/plan.md`, `requirements/plan/INDEX.md`, and `requirements/progress.txt`.
6. Verification:
   - `dotnet build XLuaHotfix.sln --no-restore`;
   - `git diff --check` for touched files;
   - static grep confirms active code/docs no longer treat W-W as fatal exclusive write ownership;
   - AB config shows builtin collection before dependency analysis.

## Execution Result

Implemented on 2026-06-01. The scheduler now treats `WriteKeys` as BuildContext write/update declarations, AB collection is ordered as `TaskCollectAssets -> TaskCollectBuiltins -> TaskAnalyzeDependencies`, the current AB `BuildPipelineConfig` asset carries the same dependency chain, and BuildGraph data-flow edges now follow the nearest upstream writer in topological order.

Awaiting developer sign-off after Unity Editor AB Validate/Build verification.

## Non-Goals

- Do not change runtime loading behavior.
- Do not change Lua/C# bridge behavior.
- Do not add Scene/duplicate bundle validation.
- Do not change backend selection ownership.
- Do not refactor unrelated BuildGraph editing behavior.

## Approval

Approved by developer on 2026-06-01 with instruction to write this standard plan first, then execute the workflow directly.
