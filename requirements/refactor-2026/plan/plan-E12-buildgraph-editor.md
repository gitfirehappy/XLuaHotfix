# Plan-E12: BuilderPanel BuildGraph Editor

> **Status**: Approved — awaiting execution (2026-05-13)
> **Risk**: Medium (Editor-only in E12-1, but later phases can edit BuildPipelineConfig and trigger build flows)
> **Dependencies**: E5-1 (IBuildTask/BuildPipelineConfig/DAGScheduler realized), E10 (BuildProjectManager dual backend realized), E11 (FYAssetSettings + AB PIPELINE gating realized)
> **Supersedes**: `drafts/draft-buildgraph-visualization.md`
> **Scope**: AB pipeline only. `UseABBackend=false` keeps the existing AB PIPELINE greyed-out behavior.

---

## Approved Decisions

| # | Decision | Result |
|---|----------|--------|
| D1 | First executable slice | **E12-1 only**: read-only DAG visualization + Validate. No drag editing, no build trigger. |
| D2 | Rendering technology | Unity `UnityEditor.Experimental.GraphView`, embedded in BuilderPanel. |
| D3 | Data-flow edges | Show data-flow edges in E12-1, not deferred. |
| D4 | Plan filename | `plan-E12-buildgraph-editor.md`. |
| D5 | Backend gating | BuilderPanel follows the current `UseABBackend=true` interactive / false greyed-out policy. |

---

## Objective

Upgrade `BuilderPanel` from a placeholder into a BuildGraph editor in staged slices:

1. **E12-1**: read-only DAG visualization and validation entry.
2. **E12-2**: interaction editing for `TaskEntry.Enabled` and SO-level dependencies.
3. **E12-3**: build triggering and real-time task status visualization.

Only E12-1 is approved for execution by this plan. E12-2 and E12-3 are documented as boundaries and require separate approval before implementation.

---

## PRS Design

### Paradigm

| Mechanism | Data | Invariant |
|-----------|------|-----------|
| Task graph model | `BuildPipelineConfig.Tasks` + `IBuildTask` instances from `BuildTaskResolver` | `TaskName` is the graph node identity. |
| Execution dependency edges | `IBuildTask.DependsOn` and `TaskEntry.DependsOn` | Code-level dependencies are read-only; SO-level dependencies are user-owned but not editable in E12-1. |
| Data-flow edges | `WriteKeys` to matching `ReadKeys` | Data-flow edges are derived only; they do not change scheduler order by themselves. |
| Validation surface | `DAGScheduler.Validate(config)` | Validation displays diagnostics but never executes tasks in E12-1. |
| AB backend gating | `FYAssetSettings.Instance.UseABBackend` through existing BuildPipelineWindow grey-out behavior | BuilderPanel must not bypass sidebar/content gating. |

### Rules

| Condition | Action | Order | Recovery |
|-----------|--------|-------|----------|
| BuilderPanel opens | Load BuildPipelineConfig and rebuild graph | OnEnable / Reload before draw | If config is missing, show create/open guidance and no graph. |
| Task exists in config but resolver cannot create it | Render the node as invalid and include validation output | During graph rebuild | Do not throw from GUI. |
| Code-level dependency exists | Render read-only execution edge | Before SO-level edge styling | If dependency target is missing, let Validate report it. |
| SO-level dependency exists | Render read-only execution edge with distinct style in E12-1 | After code-level edges | Editing deferred to E12-2. |
| WriteKey matches another task ReadKey | Render read-only data-flow edge | After task nodes are created | Missing producers are shown by Validate warning, not by synthetic nodes. |
| `UseABBackend=false` | Keep current greyed-out panel behavior | Controlled by BuildPipelineWindow | Do not add duplicate gating inside graph widgets unless required for UI Toolkit safety. |

### System

| Component | Responsibility |
|-----------|----------------|
| `BuilderPanel` | Owns the panel lifecycle and embeds/reloads the GraphView root. |
| `BuildGraphView` | GraphView surface: zoom, pan, grid, node/edge creation, reload. |
| `BuildTaskNode` | Visual representation of one task, including task name, enabled state, read keys, and write keys. |
| `BuildGraphLayoutEngine` | Deterministic layered layout from scheduler dependencies. |
| `BuildGraphToolbar` | E12-1 toolbar actions: Reload and Validate only. |

---

## E12-1: Read-Only DAG Visualization + Validate

### Task Breakdown

#### E12-1-T1: BuildGraphView surface

**New file**: `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphView.cs`

- Create a `GraphView` subclass.
- Add zoom, drag, selection, and grid background manipulators.
- Provide `Reload(BuildPipelineConfig config)` to clear and rebuild graph.
- Keep graph read-only in E12-1: no user edge creation, no node deletion, no SO mutation.

**Est.**: ~90 lines

#### E12-1-T2: BuildTaskNode rendering

**New file**: `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildTaskNode.cs`

- Render `TaskName`.
- Render enabled/disabled state.
- Render `ReadKeys` and `WriteKeys`.
- Provide execution input/output ports and data input/output ports.
- Keep ports visually present but not connectable by user in E12-1.

**Est.**: ~120 lines

#### E12-1-T3: BuildGraphLayoutEngine

**New file**: `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphLayoutEngine.cs`

- Compute layered positions from code-level + SO-level execution dependencies.
- Use deterministic ordering by `TaskName`.
- Place disconnected/invalid nodes in a stable fallback column.

**Est.**: ~90 lines

#### E12-1-T4: BuildGraphToolbar

**New file**: `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphToolbar.cs`

- Add `Reload` button.
- Add `Validate` button.
- Display validation summary from `DAGScheduler.Validate(config)`.
- Do not include `Build Full` / `Build Hotfix` in E12-1.

**Est.**: ~80 lines

#### E12-1-T5: BuilderPanel integration

**Modify**: `Assets/FYAsset/Scripts/Build/Editor/BuilderPanel.cs`

- Replace placeholder IMGUI card with a UI Toolkit host for toolbar + graph.
- Load `BuildPipelineConfig` from `FYAssetSettings.Instance.PipelineConfigPath`.
- Keep `IBuildPipelinePanel` lifecycle compatible with BuildPipelineWindow.
- Avoid triggering build operations.

**Est.**: ~80 lines changed

#### E12-1-T6: csproj sync + verification

- Add new editor files to `Assembly-CSharp-Editor.csproj` if project files are manually maintained.
- Run `dotnet build XLuaHotfix.sln`.
- Confirm no `Assets/` runtime behavior changed.

---

## E12-1 Edge Semantics

| Edge Type | Source | Editable in E12-1 | Visual Intent |
|-----------|--------|-------------------|---------------|
| Code execution dependency | `IBuildTask.DependsOn` | No | Read-only backbone dependency. |
| SO execution dependency | `TaskEntry.DependsOn` | No | User-owned dependency, displayed but not editable until E12-2. |
| Data flow | Producer `WriteKeys` to consumer `ReadKeys` | No | Derived data relationship, not scheduler order. |

---

## E12-2 Boundary: Interaction Editing

Requires separate approval before implementation.

Planned scope:
- Enable/Disable task from node menu.
- Add/remove SO-level dependencies by GraphView edge operations.
- Prevent illegal connections with cycle detection.
- Highlight Write-Write conflicts and Read-before-Write warnings.
- Persist node positions only if default auto-layout is insufficient.

Out of E12-2:
- Build execution.
- `DAGScheduler.ExecuteAsync()`.

---

## E12-3 Boundary: Build Trigger + Runtime Status

Requires separate approval before implementation because it touches build entry behavior.

Planned scope:
- Add `Build Full` and `Build Hotfix` buttons.
- Connect to `BuildProjectManager` or a backend-safe editor facade.
- Show Pending/Ready/Running/Success/Failed/Skipped node states.
- Decide whether `DAGScheduler` needs `ExecuteAsync()` or a lower-risk status callback model.

Out of E12-3:
- Legacy Addressables graph visualization.
- Changing output artifact format.
- Changing hotfix distribution behavior.

---

## Execution Order

```
E12-1-T1 (BuildGraphView)
  -> E12-1-T2 (BuildTaskNode)
    -> E12-1-T3 (Layout engine)
      -> E12-1-T4 (Toolbar)
        -> E12-1-T5 (BuilderPanel integration)
          -> E12-1-T6 (csproj + verification)
```

Sequential execution is preferred. The UI Toolkit / GraphView lifecycle and the existing IMGUI panel lifecycle should be integrated in one pass to reduce editor-window regressions.

---

## File List

| Action | File | Purpose |
|--------|------|---------|
| New | `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphView.cs` | GraphView surface and graph rebuild. |
| New | `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildTaskNode.cs` | Task node rendering. |
| New | `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphLayoutEngine.cs` | Deterministic layered layout. |
| New | `Assets/FYAsset/Scripts/Build/Editor/BuildGraph/BuildGraphToolbar.cs` | Reload / Validate toolbar. |
| Modify | `Assets/FYAsset/Scripts/Build/Editor/BuilderPanel.cs` | Replace placeholder with graph host. |
| Modify if needed | `Assembly-CSharp-Editor.csproj` | Include new editor files. |

---

## Invariants

1. `UseABBackend=false` keeps the current AB PIPELINE greyed-out behavior.
2. E12-1 does not modify `BuildPipelineConfig.asset`.
3. E12-1 does not call `BuildProjectManager`, `IBuildBackend.BuildAsync`, or any build task `Execute()`.
4. Code-level dependencies remain read-only.
5. SO-level dependencies are visible but not editable until E12-2.
6. Data-flow edges are derived from `ReadKeys` / `WriteKeys` only.
7. `DAGScheduler.Execute()` behavior remains unchanged.
8. `dotnet build XLuaHotfix.sln` has 0 errors.

---

## Acceptance Criteria

1. Opening `XLua/Build Pipeline` and selecting Builder shows a graph instead of the placeholder card.
2. The graph shows all configured tasks from `BuildPipelineConfig.Tasks`.
3. Disabled tasks are visually distinct but still shown.
4. Code-level execution edges, SO-level execution edges, and data-flow edges are visually distinguishable.
5. `Reload` rebuilds the graph without reopening the window.
6. `Validate` displays `DAGScheduler.Validate(config)` success/warning/error summary without executing tasks.
7. `UseABBackend=false` greys out Builder through the existing BuildPipelineWindow behavior.
8. Compiles with 0 errors.

---

## Out of Scope

- Drag-line dependency editing.
- Enable/Disable task mutation.
- Node position persistence.
- Build Full / Build Hotfix buttons.
- Real-time build status.
- `DAGScheduler.ExecuteAsync()`.
- Legacy Addressables graph visualization.
- Custom IBuildTask code generation or templates.

---

## Approval Checklist

Approved on 2026-05-13:

- [x] E12-1 only does read-only DAG visualization + Validate; no drag editing and no build trigger.
- [x] GraphView uses Unity `UnityEditor.Experimental.GraphView`.
- [x] Data-flow edges are shown in E12-1.
- [x] Formal plan filename is `plan-E12-buildgraph-editor.md`.
- [x] BuilderPanel keeps the current `UseABBackend` gating behavior.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-13 | Promoted from `draft-buildgraph-visualization.md`; split scope into E12-1/E12-2/E12-3 and approved E12-1 as the only executable slice. |
