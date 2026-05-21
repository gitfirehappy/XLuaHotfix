# Sub-Plan EUP-1: Editor UX Low-Risk Polish

> **Risk**: Low
> **Status**: Executed; awaiting sign-off
> **Date**: 2026-05-21
> **Positioning**: Editor-only usability polish for BuildGraph, Pipeline Validate diagnostics, splitter visibility, and Collector scan preview readability.

---

## Objective

Improve FYAsset editor inspection and troubleshooting without changing build orchestration, Task scheduling, runtime loading, Addressables configuration, or package output formats.

Target outcomes:

- Task nodes can open their corresponding C# source from the BuildGraph right-click menu.
- Pipeline `Validate` details are visible in a bottom bar only after validation-related actions, can be closed, and can be copied.
- Thin splitters in the Pipeline/Collector editor surface are easier to see and drag.
- Collector Scan Preview remains readable in small windows through vertical scrolling.

---

## Current Verified State

| Area | Current state | Gap |
|------|---------------|-----|
| BuildGraph task nodes | `BuildGraphView` renders `BuildTaskNode` nodes and has a blank-space optional Task creation menu | No direct source jump from a Task node |
| Pipeline validation | `PipelinePanel` writes only a short top-toolbar status string | Long validation messages are truncated and cannot be copied |
| Splitters | Collector detail and bottom panes use very thin plain `VisualElement` splitters | Handles are hard to see in compact layouts |
| Collector Scan Preview | Bottom tab already uses a `ScrollView`; scan list is rendered in a multiline `TextField` | Small windows can still obscure the full scan text without explicit sizing/scroll behavior |

---

## Planned Changes

| Area | File / Module | Change |
|------|---------------|--------|
| Task source lookup | `BuildTaskResolver` | Add a read-only `TryGetTaskType(string taskName, out Type type)` helper |
| BuildGraph context menu | `BuildGraphView` | Add node-only `Open Source` menu action that resolves `TaskName -> Type -> MonoScript` and opens it with `AssetDatabase.OpenAsset` |
| Validation details | `PipelinePanel` | Add hidden-by-default bottom detail bar with Close, Copy, and read-only multiline text; show it after Validate or validation/build exceptions |
| Shared splitter style | `BuildPipelineUI` | Add a reusable UI Toolkit splitter factory without `style.cursor`, preserving Unity 2022.3 compatibility |
| Collector readability | `CollectorPanel` | Use the shared splitter style and ensure Scan Preview content can scroll in small bottom panes |
| Requirement records | `requirements/`, README/context if verified | Record execution, status, and verified editor behavior |

---

## Invariants

1. No build artifact format changes.
2. No runtime loading behavior changes.
3. No Addressables Groups/profile/schema/label editor changes.
4. No `IBuildTask`, `BuildPipelineConfig`, or `TaskEntry` data model changes.
5. Blank-space BuildGraph right-click optional Task creation remains intact.
6. Backbone Task creation exclusion remains intact.
7. No IMGUI reintroduction for migrated UI Toolkit panels.
8. No Unity 2022.3 incompatible UI Toolkit cursor styling.

---

## Acceptance Criteria

- [x] Right-clicking a concrete Task node shows `Open Source`.
- [x] `Open Source` opens the matching `MonoScript` for registered Task types.
- [x] Missing/unresolved Task source emits a clear Unity Console warning instead of failing silently.
- [x] Right-clicking BuildGraph blank space still shows optional Task creation behavior.
- [x] Pipeline validation detail bar is hidden before validation-related actions.
- [x] Running `Validate` shows a bottom detail bar with full text, `Copy`, and `Close`.
- [x] Top toolbar validation status remains a short summary.
- [x] Pipeline/Collector splitters are more visible and remain draggable.
- [x] Collector Scan Preview remains inside the existing bottom tab and supports vertical scrolling in small panes.
- [ ] Unity Editor host workflow is verified manually for AB/AA Pipeline and Collector small-window behavior.
- [x] `dotnet build XLuaHotfix.sln` passes with 0 new errors.

---

## Out Of Scope

- Build execution semantics.
- Task dependency validation rules.
- Addressables configuration UI.
- Runtime hotfix or asset loading behavior.
- Build Repository / report UI.
- Persisting splitter sizes beyond the existing in-memory panel fields.

---

## Approval Checklist

- [x] Task right-click source jump is node-only and does not replace blank-space optional Task creation.
- [x] Pipeline Validate uses a hidden-until-needed bottom bar with close and copy actions.
- [x] Full validation text is shown in the bottom bar; top status stays concise.
- [x] Splitter polish is limited to Pipeline validation details and Collector detail/bottom panes.
- [x] Collector Scan Preview keeps the current bottom tab model and only improves small-window scrolling.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-21 | Plan created from developer-approved EUP-1 scope and executed as an Editor-only UX polish |
| 2026-05-21 | Static verification and `dotnet build XLuaHotfix.sln` passed with existing System.Net.Http warnings; Unity Editor host-workflow sign-off remains pending |
