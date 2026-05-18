# Plan: Build Folder Path Cleanup

## Summary

Reorganize the Build-side historical `BuildManage` layout into responsibility-based directories. This is a path-only cleanup: no namespace changes, no type splitting, and no build behavior changes.

## Target Layout

- `Build/Release/Editor/`: release orchestration, CLI entry, and Legacy/AB build backends.
- `Build/Manifests/`: runtime-readable manifest data models.
- `Build/Manifests/Editor/`: manifest and Lua index export helpers.
- `Build/Bootstrap/`: startup build metadata model.
- `Build/Bootstrap/Editor/`: startup metadata export helper.
- `Build/Snapshots/`: snapshot data model.
- `Build/Snapshots/Editor/`: differential snapshot processor.
- `Build/Versioning/`: version database and `VersionNumber`.

## Invariants

- Preserve every moved `.cs.meta` file with its `.cs` file.
- Keep runtime-readable metadata outside `Editor/`.
- Do not move `Build/Collector/`, `Build/Pipeline/`, `Build/Editor/`, or root Build utility files.
- Keep historical archive/review documents unchanged except current active tracking records.

## Verification

- Current code/docs/csproj must not reference `Build/BuildManage`, `HelperBuildData_Remote`, or script-side `LocalStaticData`.
- `Assets/FYAsset/Scripts/Build/BuildManage/` must not exist after migration.
- `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj` must contain all moved files at their new paths.
- `dotnet build XLuaHotfix.sln` must pass with 0 errors.
