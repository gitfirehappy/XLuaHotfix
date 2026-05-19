# Plan: Complete FYAsset Editor UI Toolkit Replacement

> Status: Archived — 2026-05-19; DONE
> Date: 2026-05-19
> Source draft: `../drafts/archive/draft-uitoolkit-migration-analysis-20260518.md`
> Execution mode: developer confirmed previous step and requested continuing directly

## Goal

Replace the remaining FYAsset build editor IMGUI surfaces with UI Toolkit while matching the current layout and workflows. Implementation may differ from the old IMGUI internals, but the user-facing panel structure must stay aligned with the current BuildPipelineWindow design.

## Scope

Included:

- `BuildPipelineWindow` shell: sidebar, collapsible groups, Legacy/AB mutual disable hint, resizable sidebar.
- Build pipeline panels: Settings, Legacy Config, Legacy Build, Legacy Report, Collect Config, Collector, Pipeline, Builder, Version.
- Collector UI helpers required by those panels: result rendering, rule dropdowns, picker popup replacement.
- `CollectorSettingInspector` shortcut inspector.
- `SOAddressableTagger` standalone helper window, promoted from the same draft after the main build editor replacement was verified.

Excluded unless needed for compile:

- Runtime loading, hotfix, build artifact format, AB/AA backend behavior.
- XLua bridge config.
- GraphView internals except hosting/lifecycle changes needed by `PipelinePanel`.
- `CollectorAssetInspectorGUI` header injection drawing remains on Unity's `Editor.finishedDefaultHeaderGUI` IMGUI callback because Unity does not expose an equivalent UI Toolkit hook for the default Inspector header.

## Layout Contract

- Sidebar keeps four groups: `SETTINGS`, `LEGACY PIPELINE`, `AB PIPELINE`, `MANAGE`.
- Panel order remains: Settings, Legacy Config, Legacy Build, Legacy Report, Collect Config, Collector, Pipeline, Builder, Version.
- Legacy and AB groups remain mutually disabled based on `FYAssetSettings.Instance.UseABBackend`.
- Collector Config keeps left package/group navigation and right detail editor.
- Collector keeps top toolbar, main table/detail split, and bottom Validation / Scan Preview tabs.
- Pipeline keeps top toolbar, build options row, and BuildGraph below.

## Tasks

| ID | Status | Task |
|----|--------|------|
| UTC-0 | DONE | Record UTM sign-off and promote this plan. |
| UTC-1 | DONE | Replace BuildPipelineWindow IMGUI shell with UI Toolkit CreateGUI shell. |
| UTC-2 | DONE | Update panel contract and shared UI Toolkit helpers/styles. |
| UTC-3 | DONE | Migrate Settings, Version, Legacy Config, Pipeline, and reserved panels. |
| UTC-4 | DONE | Migrate CollectorSettingPanel and CollectorPanel with matching layout and behavior. |
| UTC-5 | DONE | Replace CollectorSettingInspector and popup flow with UI Toolkit equivalents. |
| UTC-6 | DONE | Remove or isolate obsolete IMGUI-only helper code from active editor paths. |
| UTC-7 | DONE | Sync csproj, README, context, draft marker, and progress. |
| UTC-8 | DONE | Verify compile and static scope audit. |

## Verification

- `dotnet build XLuaHotfix.sln` must pass with 0 errors.
- `rg` audit must show no active FYAsset build editor panel depending on `OnGUI`, `EditorGUILayout`, `GUILayout`, `EditorGUI`, or `UnityEditor.IMGUI.Controls.TreeView`, except historical/unused files explicitly isolated from active paths.
- Any remaining IMGUI use must be documented with a reason.

## Result

- `BuildPipelineWindow` and active panels now host UI Toolkit content through `CreateGUI()` / `CreateContent()`.
- The old Collector IMGUI `TreeView`, property panel, result panel, and popup files were removed from active compile paths.
- `SOAddressableTagger` was also migrated to `CreateGUI()` because it was explicitly listed in the source draft IMGUI inventory.
- Remaining IMGUI audit hits are limited to `CollectorAssetInspectorGUI`, which is bound to Unity's IMGUI-only default Inspector header extension point.
