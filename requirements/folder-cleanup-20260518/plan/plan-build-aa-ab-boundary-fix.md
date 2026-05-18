# Plan: Build AA/AB Boundary Corrective Cleanup

## Summary

Correct the Build folder cleanup where runtime-readable manifest models and Build editor panels still mix AA, AB, and shared responsibilities.

This is still a path and naming cleanup. It does not change build logic, hotfix flow, or public runtime behavior.

## Scope

- Move runtime-readable AA/AB manifest models under `Runtime/Manifests/` with AA and AB at the same directory level.
- Move `LuaScriptsIndex` to `Assets/XLuaFramework/Scripts/XLuaLoader/` because it is Lua routing data consumed by `XLuaLoader`.
- Split `Build/Release/Editor/` by shared, Addressables, and AB responsibilities.
- Split `Build/Editor/` by shared window code, settings, Legacy Addressables panels, AB pipeline panels, and management panels.
- Rename the root hotfix pointer type from `Manifest` to `PackageIndex`, while keeping the remote/local file name `manifest.json` unchanged.

## Target Layout

```text
Assets/FYAsset/Scripts/Runtime/Manifests/
  Shared/
    PackageIndex.cs
  Addressables/
    AAManifest.cs
    PackageEntry.cs
  AB/
    ABManifest.cs
    ManifestAssetEntry.cs
    ManifestBundleEntry.cs

Assets/XLuaFramework/Scripts/XLuaLoader/
  LuaScriptsIndex.cs

Assets/FYAsset/Scripts/Build/Release/Editor/
  Shared/
    BuildProjectManager.cs
    BuildCommandLine.cs
    IBuildBackend.cs
    BuildBackendResult.cs
  Addressables/
    LegacyAddressableBuildBackend.cs
    BuildPathCustomizer.cs
    AAAssetIndexBuilder.cs
    LuaScriptsIndexExporter.cs
  AB/
    ABBuildBackend.cs

Assets/FYAsset/Scripts/Build/Editor/
  Shared/
    BuildPipelineWindow.cs
    IBuildPipelinePanel.cs
    PlaceholderPanel.cs
    BuildMessage.cs
  Settings/
    SettingsPanel.cs
  LegacyAddressables/
    LegacyConfigPanel.cs
    LegacyBuildPanel.cs
    LegacyReportPanel.cs
  ABPipeline/
    CollectorSettingPanel.cs
    PipelinePanel.cs
    BuilderPanel.cs
    BuildGraph/
  Manage/
    VersionPanel.cs
```

## Invariants

- Keep `.cs` and `.cs.meta` files together.
- Do not move `Build/Collector/`, `Build/Pipeline/`, `Build/Bootstrap/`, `Build/Snapshots/`, or `Build/Versioning/`.
- Do not rename `manifest.json` in package output or local hotfix storage.
- Do not add namespaces.
- Do not change runtime loading, hotfix update, or build behavior.

## Verification

- Current code/docs/csproj must not refer to `Build/Manifests/` or root `Build/Editor/*.cs` for files moved in this plan.
- Current code must not use the ambiguous `Manifest` type for the root package pointer.
- `dotnet build XLuaHotfix.sln` must pass with 0 errors.
