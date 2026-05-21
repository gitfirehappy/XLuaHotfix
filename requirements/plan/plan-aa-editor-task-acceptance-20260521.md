# Sub-Plan AAE-1: AA Task Editor Acceptance

> **Risk**: Low-Medium
> **Status**: Executed; awaiting sign-off
> **Date**: 2026-05-21
> **Positioning**: Editor acceptance slice after AA Task migration. This plan validates the AA Task graph in the Unity Editor by matching the existing AB Pipeline workflow, while avoiding extra UI where Addressables already provides the correct editor surface.

---

## Objective

Make the completed AA Task pipeline visible and verifiable in the FYAsset Build Pipeline editor.

The target is parity with AB's editor acceptance workflow:

- load the correct `BuildPipelineConfig`
- show the Task DAG in `BuildGraphView`
- support `Reload`
- support `DAGScheduler.Validate()`
- show Task execution status when a build runs through `BuildProjectManager`

AA-specific Addressables configuration remains owned by Unity Addressables' own Groups window when that is already sufficient. Do not duplicate Addressables configuration UI inside FYAsset.

Approved follow-up correction: AA Build must align with AB Pipeline's Build Mode and Build button. AA must not expose AB Build Options because AA configuration is owned by Addressables unless a later plan explicitly designs that integration. The legacy `Tools/Build` production menu entries remain available but are marked as legacy.

---

## Current Verified State

| Area | Current state | Gap |
|------|---------------|-----|
| AB Pipeline editor | `PipelinePanel` loads `FYAssetSettings.Instance.PipelineConfigPath`, shows BuildGraph, validates, and triggers builds | Already has the acceptance workflow |
| AA Task graph | `AABuildBackend` loads `FYAssetSettings.Instance.AAPipelineConfigPath` and runs AA tasks through `DAGScheduler` | Runtime/build path is taskized, but editor acceptance entry is not equivalent to AB |
| AA Build panel | `AABuildPanel` delegates to the parameterized `PipelinePanel` with `AAPipelineConfigPath` and AA backbone tasks | Unity Editor host-workflow sign-off remains pending |
| AA Config panel | `AAConfigPanel` shows Addressables summary and opens Addressables Groups | This is sufficient for Addressables-native configuration; no duplicate UI should be added |
| AA Report panel | Reserved until E7 / Build Repository report work | Keep deferred |

---

## Design Decisions

### D1: Reuse AB Editor Workflow Instead Of Inventing A Separate AA Tool

AA Build should reuse the same BuildGraph / Validate / Build Mode / Build control model already used by AB.

Reason:

- The acceptance target is the shared Task engine, not Addressables group editing.
- AB already established the editor workflow and PP-15 requires host-workflow verification.

### D2: Parameterize The Pipeline Panel Behavior

Extract or parameterize the reusable part of `PipelinePanel` so AA can supply:

- config path: `FYAssetSettings.Instance.AAPipelineConfigPath`
- default tasks: `BuildPipelineBackbone.CreateAATasks()`
- panel label / diagnostics prefix: AA

AB continues to use:

- config path: `FYAssetSettings.Instance.PipelineConfigPath`
- default tasks: `BuildPipelineBackbone.CreateABTasks()`
- panel label / diagnostics prefix: AB

Reason:

- Avoid duplicating GraphView and build-control logic.
- Preserve the existing AB workflow.

### D3: Do Not Recreate Addressables UI

If AA configuration is already covered by `AAConfigPanel` + Unity Addressables Groups, this plan should not add another UI for groups, schemas, profiles, labels, or catalog settings.

Reason:

- The developer explicitly requested: if AA already has handling, do not add extra UI.
- Addressables-native settings are safer to inspect/edit in the official Addressables Groups window.

### D4: Keep AA Report Deferred

Do not build AA report/diff/artifact browsing UI in this plan.

Reason:

- The existing `AAReportPanel` is intentionally deferred until E7 / Build Repository.
- Adding a report now would create a second acceptance surface before the data model is settled.

### D5: AA Build Aligns With AB Build Trigger Controls

AA Task editor acceptance exposes the same Build Mode and Build button as AB Pipeline. Build execution stays on `BuildProjectManager` with `BuildExecutionOptions.TaskStatusChanged`. AA does not expose Build Options in this slice because Addressables has its own configuration surface.

Reason:

- The developer clarified that AA should be aligned with AB Pipeline for `BuildMode` and `Build`.
- Reusing AB Build Options for AA would be a false configuration surface unless it is explicitly integrated with Addressables.
- The editor Build entry becomes the preferred path; old `Tools/Build` menu entries are marked legacy but kept for compatibility.

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Shared editor panel | `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/PipelinePanel.cs` or a new shared helper under `Build/Editor/Shared/` | Make BuildGraph/Validate/Build UI reusable for AA and AB without changing GraphView behavior; keep Build Options visibility configurable |
| AA editor entry | `Assets/FYAsset/Scripts/Build/Editor/Addressables/AABuildPanel.cs` | Replace placeholder with the reusable Task graph acceptance UI backed by `AAPipelineConfigPath` |
| AB editor entry | `PipelinePanel` | Keep existing AB behavior intact while consuming the reusable path if extracted |
| Legacy menu | `BuildProjectManager` MenuItem entries | Mark production `Tools/Build` menu items as legacy while keeping them available |
| Config creation | AA and AB pipeline panels | Create missing configs with the correct backbone task list: AA uses `CreateAATasks()`, AB uses `CreateABTasks()` |
| Shell behavior | `BuildPipelineWindow` | Preserve AA/AB mutual enablement based on `FYAssetSettings.Instance.UseABBackend` |
| Documentation/progress | `requirements/`, README/context only if implementation changes verified behavior | Record editor acceptance workflow and verification results after execution |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| AAE1-T1 | Audit AB `PipelinePanel` behavior and AA panels to list exactly which UI is already covered by Addressables and which gap belongs to FYAsset | Existing editor panels |
| AAE1-T2 | Extract/parameterize shared BuildGraph + Validate + Build controls with config-path/default-task injection; keep AA Build Options hidden | T1 |
| AAE1-T3 | Wire `AABuildPanel` to the shared Task graph UI using `AAPipelineConfigPath` and `BuildPipelineBackbone.CreateAATasks()` | T2 |
| AAE1-T4 | Keep `AAConfigPanel` as Addressables summary/open-Groups only; do not add duplicate Addressables configuration controls | T1 |
| AAE1-T5 | Verify AB `PipelinePanel` behavior is unchanged after reuse/extraction | T2-T3 |
| AAE1-T6 | Host-workflow verification in Unity Editor: open Build Pipeline, switch AA/AB groups, Reload, Validate, inspect BuildGraph content and status rendering | T3-T5 |
| AAE1-T7 | Sync requirement progress, plan status, README/context only for verified behavior changes | T6 |

---

## Invariants

1. No build artifact format changes.
2. No runtime loading behavior changes.
3. No Addressables group/schema/profile mutation logic is added to FYAsset UI.
4. No duplicate AA report/diff UI is added before E7.
5. AB `PipelinePanel` remains functionally equivalent to its current workflow.
6. AA editor build execution uses the same `BuildProjectManager` path as AB.
7. `UseABBackend` remains the single source of truth for which pipeline group is active in the window shell.
8. `AAPipelineConfigPath` and `PipelineConfigPath` remain separate.
9. Backbone tasks remain non-creatable from the BuildGraph right-click optional-task menu.
10. Editor UI verification must use the actual Unity Editor window workflow, not only `dotnet build`.

---

## Acceptance Criteria

- [x] AA Build panel is no longer only a placeholder if no equivalent existing UI covers Task graph acceptance.
- [x] AA Build panel loads `FYAssetSettings.Instance.AAPipelineConfigPath`.
- [x] Missing AA config creation uses `BuildPipelineBackbone.CreateAATasks()`.
- [x] AA BuildGraph displays the AA backbone tasks, including `TaskBuildAddressablesContent`, `TaskOrganizeAAOutput`, `TaskWriteAAPackageManifest`, and `TaskExportLocalBuildData`.
- [x] AA `Reload` and `Validate` are wired through the same `PipelinePanel` path as AB.
- [x] AA Build panel exposes Build Mode and Build trigger aligned with AB Pipeline.
- [x] AA Build panel does not expose AB Build Options; AA configuration remains in Addressables.
- [x] AB Pipeline still loads `FYAssetSettings.Instance.PipelineConfigPath` and preserves existing Reload/Validate/Build controls.
- [x] No new Addressables Groups/profile/schema/label editor is added when the existing Addressables Groups window already covers it.
- [x] AA Report remains deferred unless E7 data/report scope is explicitly approved.
- [ ] Unity Editor host workflow is verified: Build Pipeline window opens, AA group behavior matches `UseABBackend=false`, AB group behavior matches `UseABBackend=true`, graph nodes render, validation runs, and status text is visible.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors after implementation.

---

## Out Of Scope

- Changing AA/AB manifest schemas.
- Changing package output layout or `PackageIndex.json` behavior.
- Changing Addressables build semantics, group movement, catalog settings, or hotfix distribution flow.
- Removing legacy `Tools/Build` menu items; they are only marked legacy in this slice.
- E7 Build Repository report/diff/artifact browsing UI.
- Replacing Unity Addressables Groups UI.

---

## Approval Checklist

- [x] AA Task graph acceptance should be added to `AABuildPanel` by reusing/parameterizing the AB `PipelinePanel` workflow.
- [x] AA Addressables configuration should remain in `AAConfigPanel` + Unity Addressables Groups; do not add duplicate Groups/profile/schema UI.
- [x] AA Report should remain deferred until E7 / Build Repository; do not add report UI in this plan.
- [x] AB Pipeline behavior must be treated as regression-sensitive and verified after the shared panel extraction.
- [x] Editor verification for this plan should include Unity Editor `Reload` + `Validate` workflow for AA and AB.
- [x] AA Full/Hotfix build execution is exposed through the editor panel to align with AB Pipeline BuildMode/Build; legacy `Tools/Build` entries are marked but retained.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-21 | Created pending-approval plan for AA Task editor acceptance, with AB parity and no duplicate Addressables UI as explicit constraints |
| 2026-05-21 | Approved with constraints: use `AABuildPanel`, do not add duplicate Addressables UI, keep AA Report deferred, and do not run or expose actual AA Full/Hotfix build execution in this slice |
| 2026-05-21 | Executed code/docs sync. Static and dotnet verification passed; Unity Editor host-workflow sign-off remains pending |
| 2026-05-21 | Follow-up correction: AA Build now aligns with AB Build Mode / Build controls only; AA Build Options stay hidden because Addressables owns AA configuration; legacy `Tools/Build` production entries are marked but retained |
