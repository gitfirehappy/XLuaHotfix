# Plan: UI Toolkit Mechanical Shell Migration After Unity 2022.3.62 Upgrade

> Status: Archived — 2026-05-19; DONE
> Date: 2026-05-19
> Source draft: `../drafts/archive/draft-uitoolkit-migration-analysis-20260518.md`
> Execution mode: developer pre-approved direct execution; no approval checklist required for this plan

## Goal

Promote only the low-risk, mechanical part of the UI Toolkit migration draft after the project was upgraded to Unity `2022.3.62f3`.

This plan does not perform a full IMGUI to UI Toolkit migration. It only creates a reusable UI Toolkit host for simple build editor panels and converts simple placeholder/reserved panels where behavior is static and does not require interaction redesign.

## Verified Baseline

- `ProjectSettings/ProjectVersion.txt` reports `2022.3.62f3 (96770f904ca7)`.
- `Assembly-CSharp-Editor.csproj` already points Unity references and define constants at `2022.3.62f3`.
- `Packages/manifest.json` keeps `com.unity.modules.uielements` and `com.unity.modules.imgui`; no package addition is required for Editor UI Toolkit.
- The package lock resolves `com.unity.scriptablebuildpipeline` to `1.21.25` while Addressables declares a lower compatible dependency. This is not treated as a bug without a Unity Package Manager error.

## Extracted From Draft

Promoted and executed:

- Use Unity `2022.3.x` Editor UI Toolkit API as the baseline.
- Prefer UI Toolkit for new/simple editor panel shells.
- Keep existing mature IMGUI panels untouched when migration would require redesign.
- Treat `BuildGraphView` as already UI Toolkit / Experimental GraphView and leave it unchanged.

Kept in draft, not executable in this plan:

- `CollectorTreeView` migration to `MultiColumnTreeView`.
- `CollectorTargetPickerPopup` replacement.
- `CollectorAssetInspectorGUI` replacement for `Editor.finishedDefaultHeaderGUI`.
- `CollectorPanel` drag-and-drop / splitter / dense table redesign.
- `CollectorSettingPanel` package/group navigation redesign.
- `PipelinePanel` IMGUI plus GraphView mixed host redesign.

## Tasks

| ID | Status | Task | Files |
|----|--------|------|-------|
| UTM-1 | DONE | Add a reusable UI Toolkit panel host that maps an IMGUI content rect to an absolute `VisualElement` overlay. | `Assets/FYAsset/Scripts/Build/Editor/Shared/BuildPipelineUIToolkitPanel.cs` |
| UTM-2 | DONE | Convert simple reserved/placeholder panels to the host without changing their panel order or sidebar behavior. | `PlaceholderPanel.cs`, `LegacyBuildPanel.cs`, `LegacyReportPanel.cs`, `BuilderPanel.cs` |
| UTM-3 | DONE | Sync project file for external build verification. | `Assembly-CSharp-Editor.csproj` |
| UTM-4 | DONE | Audit Unity upgrade configuration and compile. | `ProjectVersion.txt`, packages, generated project references |
| UTM-5 | DONE | Align README, context, draft marker, plan index, and progress log. | `README.md`, `context/architecture/system-overview.md`, `../drafts/archive/draft-uitoolkit-migration-analysis-20260518.md`, `requirements/plan/INDEX.md`, `requirements/progress.txt` |

## Acceptance Criteria

- Simple reserved panels render through UI Toolkit and keep current user-facing semantics.
- Sidebar selection, disabled Legacy/AB group behavior, and `IBuildPipelinePanelVisibility` lifecycle still work.
- No Collector, Popup, HeaderGUI, or GraphView redesign is introduced.
- `dotnet build XLuaHotfix.sln` passes with 0 errors.
- Version knowledge in `context/` is aligned to Unity `2022.3.62f3`.

## Verification

- `dotnet build XLuaHotfix.sln` passed with 0 errors.
- Remaining warnings are pre-existing framework/package warnings, not introduced by this plan.
