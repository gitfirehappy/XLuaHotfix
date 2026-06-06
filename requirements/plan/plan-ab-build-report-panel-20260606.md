# AB Build Report Panel Plan

## Summary

Implement the first concrete AB build result report while leaving AA on Unity Addressables Report. AB writes an editor-only JSON report during official AB builds and the existing AB Build Result sidebar entry reads that report.

The report is tooling data only. It is written under `BuildData/Reports/AB/`, ignored by git, and never copied into package output.

## Scope

- Add AB report data model, report builder, JSON reader/writer, and report panel UI.
- Capture AB DAG result data from the existing `BuildContext` after build execution.
- Show reports through the current `ABReportPanel` entry.
- Keep `AAReportPanel` unchanged.
- Do not change runtime manifest schema, hotfix download flow, package distribution format, AB/AA backend selection, or Lua bridge behavior.

## Implementation Checklist

1. Create editor-only report model and helpers under `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/Report/`.
2. Extend `BuildBackendResult` so AB build callers can receive the `BuildResult`, package request, and output report path without changing public build commands.
3. Update `ABBuildBackend` to build and write an AB report in success and failure paths when a `BuildPackageRequest` exists.
4. Replace `ABReportPanel` placeholder with a UI Toolkit report viewer:
   - toolbar: refresh, open reports folder, reveal package, report selector, search
   - tabs: Summary, Explore, Potential Issues
   - Explore view modes: AssetBundles, Assets, Groups, Labels
   - details pane for selected bundle or asset
5. Add `/BuildData/Reports/` to `.gitignore`.
6. Update `requirements/plan.md`, `requirements/plan/INDEX.md`, `requirements/progress.txt`, and `context/architecture/resource-build-and-release.md`.
7. Verify with `dotnet build XLuaHotfix.sln --no-restore`, scoped whitespace checks, and static searches proving reports stay outside package output.

## Data Contract

- Report header: backend, build type, build target, version, package name, package path, start time, duration, success, error.
- Summary: bundle count, asset count, group count, label count, total bundle size, delivery count/size, verification issue counts, task counts.
- Bundles: name, hash, CRC, size, type, tags, dependencies, asset count, delivery flag.
- Assets: source path, entry id, address, primary type, group, labels, bundle name.
- Groups and labels are derived from `ABManifest.AssetEntries`.
- Potential issues are limited to existing pipeline data: failed tasks, task warnings, verification issues, and report load errors.

## Acceptance Criteria

- AB Full build writes a JSON report under `BuildData/Reports/AB/` and package output remains unchanged.
- AB Hotfix report shows delivery bundle count/size and marks delivered bundles.
- Failed AB build writes a readable failure report when enough context exists.
- AB Build Result panel handles no reports, corrupted reports, and normal reports without throwing.
- AA Build Result panel remains unchanged.
- Compile passes with existing warnings only.
