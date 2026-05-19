# Plan-E12: Pipeline BuildGraph and Build Execution Editor

> **Status**: Archived — 2026-05-19; E12-1 Reworked Complete; E12-2 Executed, awaiting developer sign-off (2026-05-14)
> **Risk**: Medium (Editor integration + build trigger wiring; build backend path already exists)
> **Dependencies**: E5-1 (IBuildTask/BuildPipelineConfig/DAGScheduler realized), E10 (BuildProjectManager dual backend realized), E11 (FYAssetSettings + AB PIPELINE gating realized)
> **Supersedes**: `../drafts/archive/draft-buildgraph-visualization.md`
> **Scope**: AB Pipeline editor only. Builder/report browsing is deferred to a separate post-E7 plan.

---

## Current Baseline

E12-1 was originally planned as a BuilderPanel DAG visualization, then reworked after Unity Editor verification.
The current accepted baseline is:

- `PipelinePanel` owns the BuildGraph DAG, `Reload`, `Validate`, `Build Mode`, `Build`, and build options (`FileNameStyle`, `BundleCompression`, `SequentialMode`).
- `PipelinePanel` implements `IBuildPipelinePanelVisibility`; `BuildPipelineWindow` explicitly toggles its UI Toolkit graph root visibility.
- `PipelinePanel` loads `BuildPipelineConfig` from `FYAssetSettings.Instance.PipelineConfigPath`.
- `BuildPipelineConfigRepair.EnsureBackboneTasks()` guarantees the 8 backbone tasks exist before rendering the graph.
- BuildGraph right-click creation is limited to optional tasks; backbone tasks are excluded from creation candidates.
- BuildGraph edges and ports are display-only. Nodes may be moved visually, but execution/data edges cannot be selected, deleted, or reconnected.
- Code dependency, SO dependency, and data-flow edges are visually distinguishable.
- `BuilderPanel` no longer owns or embeds the DAG and stays empty/reserved in E12.
- `BuildGraphToolbar` was an unused artifact from the first E12-1 implementation and has been deleted after reference search confirmed no remaining code or project-file usage.
- Pipeline-triggered builds route through `BuildProjectManager` and the active backend; `PipelinePanel` does not call `DAGScheduler.Execute()` directly.
- `DAGScheduler` emits task lifecycle events through `BuildExecutionOptions.TaskStatusChanged`, and `BuildTaskNode` renders `Pending`, `Running`, `Success`, `Failed`, and `Skipped`.

---

## Confirmed Decisions

| # | Decision | Result |
|---|----------|--------|
| D1 | BuildGraph ownership | `PipelinePanel` owns DAG visualization, build options, Validate, and build trigger controls. |
| D2 | Builder/report scope | Build result/report querying is removed from E12 and will be planned separately after E7 stabilizes diff/snapshot/report output formats. |
| D3 | Rendering technology | Keep Unity `UnityEditor.Experimental.GraphView`. |
| D4 | SO dependency policy | `TaskEntry.DependsOn` edges are displayed and validated, but not editable in E12-2. |
| D5 | Full build behavior | Pipeline-triggered Full build preserves current `BuildProjectManager.BuildFullPackage()` behavior, including opening Unity Build Settings after resource build. |
| D6 | Backend gating | Existing `UseABBackend=false` AB PIPELINE grey-out behavior remains the single gating policy. |
| D7 | Toolbar cleanup | E12-2 verified no code/csproj references to `BuildGraphToolbar`, then deleted the file, `.meta`, and project entry. |

---

## Objective

Upgrade the AB Pipeline editor in staged slices:

1. **E12-1**: Pipeline-owned read-only DAG visualization + Validate. **Complete after rework.**
2. **E12-2**: Pipeline top-bar build trigger + full task execution status visualization. **Executed; awaiting sign-off.**

Build result/report browsing is intentionally out of E12. It should be designed after E7 because E7 introduces `BundleDigestList` and per-version snapshot data that overlap with report inputs.

---

## PRS Design

### Paradigm

| Mechanism | Data | Invariant |
|-----------|------|-----------|
| Task graph model | `BuildPipelineConfig.Tasks` + `IBuildTask` instances from `BuildTaskResolver` | `TaskName` is the graph node identity. |
| Backbone task repair | `BuildPipelineConfigRepair` | Backbone tasks are always present and are not normal user-created optional tasks. |
| Visibility lifecycle | `IBuildPipelinePanelVisibility` | UI Toolkit graph root visibility is controlled by `BuildPipelineWindow`, not by polling or update-loop guessing. |
| Execution dependency edges | `IBuildTask.DependsOn` and `TaskEntry.DependsOn` | Code-level dependencies are fixed; SO-level dependencies remain read-only until a separately approved editing plan. |
| Data-flow edges | Producer `WriteKeys` to consumer `ReadKeys` | Data-flow edges are derived only; they do not change scheduler order by themselves. |
| Validation surface | `DAGScheduler.Validate(config)` | Validation displays diagnostics and blocks build trigger on fatal errors. |
| Build execution surface | Pipeline top bar | Build trigger must reuse existing build semantics, not bypass `BuildProjectManager` / backend flow. |
| Task status surface | DAGScheduler execution observer/callback | Node status must come from scheduler execution events, not inferred from logs or post-build guesses. |

### Rules

| Condition | Action | Order | Recovery |
|-----------|--------|-------|----------|
| PipelinePanel opens | Load BuildPipelineConfig, repair backbone tasks, rebuild graph | OnEnable / Reload before draw | If config is missing, show create/open guidance and no graph. |
| Task exists in config but resolver cannot create it | Render invalid node and include validation output | During graph rebuild | Do not throw from GUI. |
| Code-level dependency exists | Render read-only execution edge | Before SO-level edge styling | If target is missing, let Validate report it. |
| SO-level dependency exists | Render read-only execution edge with distinct style | After code-level edges | Editing remains out of scope. |
| WriteKey matches another task ReadKey | Render read-only data-flow edge | After task nodes are created | Missing producers are shown by Validate warnings, not synthetic nodes. |
| User clicks Build in Pipeline | Validate first, then trigger selected Full/Hotfix flow | Validate -> build trigger -> scheduler status events -> final result | Fatal validation failure blocks execution and highlights graph/top-bar summary. |
| Full build selected | Preserve current full-package behavior | Existing BuildProjectManager semantics | Build Settings still opens after resource build in non-batch mode. |
| BuildGraphToolbar cleanup | Reference search confirmed unused; file, `.meta`, and project entry removed | Completed in E12-2 | No remaining toolbar cleanup action. |

### System

| Component | Responsibility |
|-----------|----------------|
| `PipelinePanel` | Owns build options, Reload, Validate, DAG host, Build Mode selector, and Build button. |
| `IBuildPipelinePanelVisibility` | Lets `BuildPipelineWindow` explicitly show/hide PipelinePanel's UI Toolkit graph root. |
| `BuildGraphView` | GraphView surface: zoom, pan, grid, read-only node/edge rendering, optional task creation menu, build-running lockout, and task status refresh entry. |
| `BuildTaskNode` | Visual representation of one task, including enabled state, read/write keys, dependency labels, and execution state. |
| `BuildGraphLayoutEngine` | Deterministic layered layout from scheduler dependencies. |
| `DAGScheduler` | Exposes `BuildExecutionOptions` observer/callback integration for per-task status updates. |
| `BuildExecutionOptions` / `BuildTaskExecutionEvent` / `BuildTaskExecutionStatus` | Editor-facing execution observation contract used by PipelinePanel and DAGScheduler. |
| `BuildGraphToolbar` | Removed legacy E12-1 artifact. |
| `BuilderPanel` | Out of E12 execution scope; remains reserved until a separate post-E7 report plan. |

---

## E12-1: Pipeline-Owned Read-Only DAG + Validate

**Status: Complete after rework.**

Implemented behavior:

- `PipelinePanel` hosts the BuildGraph under a build-options top bar.
- Reload and Validate are available from the Pipeline top bar.
- `BuildPipelineConfig.asset` is repaired with the 8 backbone task entries when the panel loads.
- Optional task creation is available from the graph context menu; backbone tasks are not offered as creation candidates.
- Execution and data-flow edges are display-only.
- `BuilderPanel` is empty/reserved and does not host the graph.

Acceptance already verified:

1. Pipeline panel shows the DAG with all backbone tasks.
2. `Reload` rebuilds the graph without reopening the window.
3. `Validate` displays `DAGScheduler.Validate(config)` results without executing tasks.
4. Code-level execution edges, SO execution edges, and data-flow edges are visually distinguishable.
5. Graph edges cannot be selected, deleted, or reconnected.
6. `dotnet build XLuaHotfix.sln` passed with 0 errors during E12-1 rework.

---

## E12-2: Pipeline Build Trigger + Task Status

**Status: Executed; awaiting developer sign-off.**

Implemented behavior:

- `PipelinePanel` top bar now includes `Build Mode` (`Full` / `Hotfix`) and a single `Build` button.
- `Build` runs `DAGScheduler.Validate(config)` first; fatal validation failures block execution.
- Full and Hotfix builds preserve existing semantics through `BuildProjectManager` and the active backend.
- `IBuildBackend.BuildAsync(...)`, `ABBuildBackend`, `LegacyAddressableBuildBackend`, and `BuildProjectManager` now accept optional `BuildExecutionOptions`.
- `ABBuildBackend` passes `BuildExecutionOptions` into `DAGScheduler.Execute(...)`; Legacy Addressables accepts the parameter but has no scheduler status events.
- `DAGScheduler` reports per-task lifecycle events:
  - `Pending`
  - `Running`
  - `Success`
  - `Failed`
  - `Skipped`
- `BuildGraphView` forwards execution events to matching `BuildTaskNode` instances.
- `BuildTaskNode` shows `Status: Idle/Pending/Running/Success/Failed/Skipped` and colors node headers by status.
- Pipeline top-bar controls, build options, and graph mutation are disabled while a build is running.
- Reference search confirmed no `BuildGraphToolbar` code/csproj references; `BuildGraphToolbar.cs`, `.meta`, and csproj compile entry were removed.

Acceptance verified:

1. `PipelinePanel` calls `BuildProjectManager.BuildFullPackage(options)` / `BuildProjectManager.BuildHotfix(options)`, not `DAGScheduler.Execute()` directly.
2. `DAGScheduler` has an explicit observer path through `BuildExecutionOptions.TaskStatusChanged`.
3. `BuildGraphToolbar` has no remaining code/csproj references and is removed from `BuildGraph/`.
4. `dotnet build XLuaHotfix.sln` passed with 0 errors after final documentation/project sync; remaining warnings are pre-existing `System.Net.Http` conflicts.

Out of E12-2:

- BuilderPanel build report/query UI.
- Parsing `build_summary.txt`, `ABManifest.json`, `BundleDigestList`, or E7 snapshot outputs.
- Editing SO-level dependencies.
- Changing build artifact format.
- Replacing `BuildProjectManager` semantics with a direct `DAGScheduler.Execute` call.
- Legacy Addressables graph visualization.

---

## Deferred: Post-E7 Build Report Plan

Build result/report browsing is valuable, but it should not be implemented in E12.

Reason:

- E7 is still pending and owns diff snapshot adaptation.
- E7 introduces `BundleDigestList` `.bin/.json` and per-version snapshot/history data.
- A report browser designed before E7 may accidentally depend on incomplete report inputs or duplicate E7 parsing logic.

Future report plan should be opened after E7 and should decide the canonical report inputs across:

- `build_summary.txt`
- `ABManifest.json`
- `BundleDigestList`
- per-version snapshot/history files
- verification outputs from build tasks

---

## Edge Semantics

| Edge Type | Source | Editable | Visual Intent |
|-----------|--------|----------|---------------|
| Code execution dependency | `IBuildTask.DependsOn` | No | Read-only backbone/code dependency. |
| SO execution dependency | `TaskEntry.DependsOn` | No | User-owned config dependency, visible and validated but not editable in E12. |
| Data flow | Producer `WriteKeys` to consumer `ReadKeys` | No | Derived data relationship, not scheduler order. |

---

## Invariants

1. `UseABBackend=false` keeps the current AB PIPELINE greyed-out behavior.
2. Code-level dependencies remain read-only.
3. SO-level dependencies remain visible and validated, but not editable until a separately approved editing plan.
4. Data-flow edges are derived from `ReadKeys` / `WriteKeys` only.
5. Pipeline-triggered builds must not bypass current version increment, backend selection, output organization, or manifest update semantics.
6. Full build must preserve the existing Unity Build Settings opening behavior.
7. Task node execution states must be driven by explicit scheduler observer/callback events.
8. Build report/query UI is deferred until after E7.
9. `BuildGraphToolbar` cleanup used an explicit reference-search-and-delete completion criterion.
10. `dotnet build XLuaHotfix.sln` must have 0 errors after any code execution slice.

---

## Approval Checklist

Confirmed on 2026-05-14:

- [x] E12 plan baseline should be updated before further implementation.
- [x] PipelinePanel is the final build execution location.
- [x] Pipeline top bar uses `Build Mode` dropdown + single `Build` button.
- [x] Full build preserves current Build Settings opening behavior.
- [x] SO-level dependencies are shown and validated, but not editable.
- [x] Builder/report querying is deferred until after E7 and will be planned separately.
- [x] E12-2 must include a DAGScheduler observer/callback integration point for per-task status.
- [x] E12-2 must explicitly verify and remove unused `BuildGraphToolbar`.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-13 | Promoted from `draft-buildgraph-visualization.md`; split scope into E12-1/E12-2/E12-3 and approved E12-1 as the only executable slice. |
| 2026-05-14 | E12-1 executed initially with BuilderPanel-owned DAG visualization. |
| 2026-05-14 | E12-1 reworked: DAG ownership moved to PipelinePanel; build options moved to Pipeline top bar; BuilderPanel no longer hosts the DAG. |
| 2026-05-14 | E12 baseline redirected: PipelinePanel is the final build execution surface, and BuilderPanel/report querying is deferred to a separate post-E7 plan. |
| 2026-05-14 | E12-2 executed: Pipeline Build Mode + Build button added; build execution stays on `BuildProjectManager`; `DAGScheduler` observer events drive node status; unused `BuildGraphToolbar` removed. |
