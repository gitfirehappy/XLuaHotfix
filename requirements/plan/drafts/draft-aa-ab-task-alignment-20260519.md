# Draft: AA/AB Task Alignment for Build Optimization

Status: Draft
Date: 2026-05-19

> Promotion note (2026-05-20): Plan-1 was extracted as `../plan-build-request-output-ownership-20260520.md`.
> Scope extracted: build request model, package identity creation, and final output path ownership.
> Remaining draft scope: AB finalization Task migration, AA build graph migration, bootstrap tail Task, and backend interface cleanup after both pipelines are task-managed.

> Promotion note (2026-05-20): Plan-2 was extracted as `../plan-ab-finalization-task-20260520.md`.
> Scope extracted: AB final package output organization and AB manifest JSON/binary emission into the Task graph.
> Remaining draft scope: AA build graph migration, bootstrap tail Task, and backend interface cleanup after both pipelines are task-managed.

> Promotion note (2026-05-20): Plan-3 was extracted as `../plan-aa-build-graph-migration-20260520.md`.
> Scope extracted: AA build graph migration, including Lua index export, Addressables build, AA output organization, and AAManifest publication into an AA Task graph.

> Open decision (2026-05-20): LuaScriptsIndexExporter pipeline-independence.
> Current state: exporter depends on Addressables API for address lookup and group registration. AB runtime also needs LuaScriptsIndex (loaded by address via ABPackageBackend). Both AA and AB share the same address namespace.
> Question: Should exporter be refactored to read addresses from a unified registry (e.g., AssetAddressGenerator) instead of Addressables settings, making it pipeline-agnostic? This would allow it to be a shared AA/AB task.
> Decision: Deferred. Does not block AAG-1 execution — current plan keeps BuildProjectManager outer call for both pipelines. Revisit after AAG-1 lands.
> Remaining draft scope: bootstrap tail Task and backend interface cleanup after both pipelines are task-managed.

## Background

AA and AB are converging on the same build orchestration model: `IBuildTask` + `BuildContext` + `DAGScheduler`.
The next optimization should not keep AA in a helper-based flow while AB is task-based.
Instead, both pipelines should be managed through the same Task system, with AA/AB differences isolated to pipeline-specific task graphs and source path resolution.

## Core Decision

The important change is structural alignment, not simple renaming.

- AA and AB both use Task graphs.
- `BuildProjectManager` stays as the outer release orchestrator.
- `IBuildBackend` stays as a thin runner that selects the proper pipeline config and executes its DAG.
- Output organization, manifest generation, Lua index export, and bootstrap export are all Task-managed.
- AA/AB differences are represented by backend mode, config path, and path redirection, not by separate output-processing architectures.

## Design Direction

### 1. Task System as the Shared Engine

Both pipelines should run under the same task infrastructure:

- `IBuildTask`
- `BuildContext`
- `DAGScheduler`

AA and AB do not need identical task lists, but they should share the same execution model, validation model, and task lifecycle.

### 2. Separate Pipeline Config Assets

Keep two `BuildPipelineConfig` assets:

- one for AA
- one for AB

This keeps the task graph clear and avoids mixing mutually different pipeline steps into one config asset.

### 3. Backend Becomes a Thin Runner

The backend layer should not own duplicated output logic.
Its responsibility becomes:

- choose the active pipeline config
- execute the DAG
- expose build orchestration entry points to `BuildProjectManager`

This keeps the outer build flow stable while moving the real work into tasks.

### 4. Output Processing Becomes Task-Based

The following AA/AB build-side helpers should be absorbed into the Task system:

- `LocalStatusExporter`
- `AAAssetIndexBuilder`
- `LuaScriptsIndexExporter` as a custom Task
- `AddressablesBuildOutputOrganizer`

This is not about merging all code into one class.
It is about moving the responsibility boundary so the output pipeline is managed consistently.

### 5. Task Boundary for AA/AB Output Stages

The AA side should be split into:

- pure AA asset index export
- AA manifest generation
- Lua script index export
- Addressables-only registration step when needed

The AB side should keep its own manifest/task flow, but the output organization stage should follow the same task-oriented model.

### 6. Bootstrap Export as a Separate Tail Task

`LocalStatusExporter` should become its own tail task, not part of the organizer.

Reason:

- organizer handles package layout
- bootstrap export handles startup-facing local data
- separating them keeps the post-build chain easier to maintain

## Rules

| Condition | Action | Order | Recovery |
|---|---|---|---|
| AA pipeline build | Execute AA task graph with AA config | Before output organization | Fail fast if config or task graph is invalid |
| AB pipeline build | Execute AB task graph with AB config | Before output organization | Fail fast if config or task graph is invalid |
| Output layout generation | Use task-managed output organization | After build tasks finish | Re-run organizer task from context data |
| Manifest emission | Keep manifest type-specific, but task-managed | After build output exists | Fail if required manifest data is missing |
| Bootstrap data export | Run as a separate final task | After package output is ready | Skip only if the build mode does not require bootstrap artifacts |
| Lua index handling | Split pure export from AA registration | Before package finalization | Keep export task usable as a general build extension point |

## Assumptions

- AA and AB keep separate task graphs, not one forced merged DAG.
- `IBuildBackend` can remain as a thin runner if it delegates all real processing to tasks.
- `AAManifest` and `ABManifest` keep their own data models for now.
- The organizer should use light backend-mode branching only for source path redirection and package layout differences.
- `LuaScriptsIndexExporter` is a natural custom task, not a special-case AA-only helper.

## Expected Outcome

After this alignment:

- AA and AB build behavior is easier to reason about.
- output rules live in tasks instead of scattered helpers
- pipeline-specific differences are localized
- future optimization work can target the task graph instead of backend glue
