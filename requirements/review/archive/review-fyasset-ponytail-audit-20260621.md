# FYAsset Ponytail Audit Review

> **Date**: 2026-06-21
> **Reviewer**: Codex / ponytail-audit
> **Scope**: FYAsset code under `Assets/FYAsset/`, focused on removable complexity, unused flexibility, speculative abstractions, and shrink opportunities.
> **Method**: Static source inspection, targeted `rg` searches, context review, and over-engineering audit. No code was changed.

## Status

Processed / Archived 2026-07-14. Findings 3, 6, 8, and 9 were resolved or invalidated by later work. Still-valid
deletion candidates were consolidated into
`requirements/plan/drafts/draft-legacy-plan-review-followups-20260714.md` and remain unapproved.

## Summary

This audit intentionally excludes correctness, security, and performance review. It only lists code that can likely be deleted, simplified, or postponed until a real second use case exists.

The highest-value cuts are inactive placeholder panels, speculative rule/reflection extension points, and interfaces with only one current implementation. Runtime AA/AB loading abstractions were inspected and are not listed here because they currently carry real backend split behavior.

## Findings

1. `delete:` Uninstantiated `PlaceholderPanel`; replace with nothing. `Assets/FYAsset/Scripts/Build/Editor/Shared/PlaceholderPanel.cs`
2. `delete:` Uninstantiated `BuilderPanel` placeholder page; add it back only when a real Builder surface exists. `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/BuilderPanel.cs`
3. `delete:` `AAReportPanel` is only a future-report placeholder but still occupies a sidebar slot; remove the panel and sidebar entry until AA report data exists. `Assets/FYAsset/Scripts/Build/Editor/Addressables/AAReportPanel.cs`, `Assets/FYAsset/Scripts/Build/Editor/Shared/BuildPipelineWindow.cs`
4. `yagni:` `RuleResolver` and `RuleDropdownHelper` provide reflection-driven rule discovery while the active rule set only has `CollectAll` and `GroupAll`; call the default rules directly until a second real rule exists. `Assets/FYAsset/Scripts/Build/Collector/Editor/RuleResolver.cs`, `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/RuleDropdownHelper.cs`
5. `delete:` `RULE_GROUP_BY_TYPE`, `RULE_GROUP_BY_LABEL`, and `RULE_GROUP_BY_DIRECTORY` are constants without implementations; delete until the matching rule classes exist. `Assets/FYAsset/Scripts/FYAssetSettings.cs`
6. `yagni:` `IPushTarget` and `PushTargetType` currently support only `LocalDirectory`; publish directly from `PushTargetConfig` until a CDN or second target is implemented. `Assets/FYAsset/Scripts/Build/Repository/PushModels.cs`
7. `yagni:` `IBuildRepository` has only `FileBuildRepository`, and `BuildRepositoryFacade` downcasts back to the concrete type for `PushHead`; hold `FileBuildRepository` directly. `Assets/FYAsset/Scripts/Build/Repository/IBuildRepository.cs`, `Assets/FYAsset/Scripts/Build/Repository/Editor/BuildRepositoryFacade.cs`
8. `yagni:` `IBuildBackend` is only used by `BuildProjectManager.CreateBackend()` for a local AA/AB branch; replace with direct branch calls or static backend methods. `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/IBuildBackend.cs`, `Assets/FYAsset/Scripts/Build/Release/Editor/Shared/BuildProjectManager.cs`
9. `shrink:` `FYAssetSettings`, `FYAssetAASettings`, and `FYAssetABSettings` duplicate `LoadOrCreate()` logic; extract a minimal shared helper while keeping existing asset paths. `Assets/FYAsset/Scripts/FYAssetSettings.cs`, `Assets/FYAsset/Scripts/FYAssetAASettings.cs`, `Assets/FYAsset/Scripts/FYAssetABSettings.cs`
10. `shrink:` `CollectAll.ContainsEditorDirectory()` manually scans path segments; normalize once and use a single `IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase)`. `Assets/FYAsset/Scripts/Build/Collector/Editor/Rules/CollectAll.cs`
11. `delete:` Unused small APIs: `FYAssetBuildSettingsProvider.CurrentBackend`, `RuleDropdownHelper.ClearCache()`, and `ABBuildReportStore.GetLatestReportPath()`; replace with nothing. `Assets/FYAsset/Scripts/Build/Editor/Settings/FYAssetBuildSettingsProvider.cs`, `Assets/FYAsset/Scripts/Build/Collector/Editor/UI/RuleDropdownHelper.cs`, `Assets/FYAsset/Scripts/Build/Editor/ABPipeline/Report/ABBuildReportStore.cs`

## Net Estimate

Potential reduction: about 430 lines and no dependency changes.

## Notes

- Keep runtime `IPackageBackend`, `IAssetIndex`, and `IHotfixPipeline` for now; they represent the current AA/AB split.
- Each candidate still needs normal approval before implementation because FYAsset resource loading, hot-update flow, and build tooling are guarded areas.
