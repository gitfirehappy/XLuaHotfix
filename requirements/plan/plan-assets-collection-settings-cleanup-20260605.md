# Plan: Assets Collection And Settings Cleanup

> Status: Executed / Awaiting sign-off
> Date: 2026-06-05
> Requirement ID: assets-collection-settings-cleanup-20260605
> Scope: AssetsCollection editor workflow, Collector exclusion semantics, Scene collector shape, BuildPipeline Sequential removal, and FYAsset settings ownership.

> 2026-06-05 follow-up correction: `assets-collection-followup-20260605` superseded the exclusion owner from `FYAssetABSettings.ExcludedAssetGUIDs` to `AssetCollectionSetting.ExcludedAssets`. The AB field is now hidden legacy migration input only.

## Goal

Fix the current AssetsCollection usability and ownership issues without weakening the Collector model:

- Collector remains a rule-based collection entry, not an asset-row list.
- Folder-owned asset deletion becomes GUID-based exclusion owned by AB settings.
- Curate, Inspector, and Project context menu use one add/remove/exclude behavior.
- Curate sidebar and Scan Preview are rebuilt from current collection state after mutations.
- Scene Project Scan uses explicit scene file collectors only.
- BuildPipeline `SequentialMode` is removed because execution is already Unity-main-thread serial.
- FYAsset configuration is reduced to three active settings: Global, AA, and AB.

## Decisions

1. Use three active settings assets under `Assets/Resources/`:
   - `FYAssetSettings`: global runtime/build settings and constants.
   - `FYAssetAASettings`: AA runtime/build settings.
   - `FYAssetABSettings`: AB runtime/build settings plus AB Collector configuration.
2. Keep old build settings classes/assets only as compatibility/migration leftovers; active code reads the three settings above.
3. Store asset-level Collector exclusions in `FYAssetABSettings.ExcludedAssetGUIDs`, not in `AssetCollectionSetting` and not on `Collector`.
4. Use GUID exclusions because path-based exclusions break when assets move or rename.
5. Direct File Collector removal deletes the File Collector. Folder-owned asset removal adds the asset GUID to AB exclusions.
6. Re-adding a covered excluded asset removes the exclusion instead of adding a duplicate File Collector.
7. Project Scan creates file-level collectors for `.unity` scenes and does not add a Scene root folder collector.
8. Remove `BuildPipelineConfig.SequentialMode`; the scheduler keeps deterministic topological execution.

## Implementation Checklist

1. Settings
   - Add `FYAssetAASettings` and `FYAssetABSettings` runtime-safe ScriptableObjects.
   - Move active Global fields into `FYAssetSettings`: build output, version path, build index path, push targets, backend switch, package folder name.
   - Update runtime and editor call sites to use `FYAssetSettings.Instance`, `FYAssetAASettings.Instance`, and `FYAssetABSettings.Instance`.
   - Update settings panels to edit Global, AA, and AB settings.
   - Add three Resources assets and keep existing values from current assets.
2. Collector exclusion
   - Add scan options that carry excluded GUIDs into `CollectionScanner`.
   - Skip excluded GUIDs before metadata creation and bundle grouping.
   - Update build tasks and editor previews to pass AB exclusions.
3. Editor workflow
   - Centralize Collector add/remove/exclude operations in a shared editor utility.
   - Make Curate mutations rebuild scan state immediately and preserve sidebar expansion.
   - Make Inspector and context menu changes notify open AssetsCollection panels.
   - Show parent-folder coverage and exclusion states clearly in Inspector.
4. Scene scan
   - Update Project Scan so Scene groups contain `.unity` File collectors only.
   - Preserve `PackSeparately` for Scene groups.
5. Sequential removal
   - Remove field, UI, scheduler branch, docs/context wording, and serialized config residue.
6. Records
   - Update `requirements/progress.txt`.
   - Update context/docs if behavior changed.
   - Record a new mistake after verification.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static searches:
  - no active `SequentialMode` reads;
  - no active `SharedBuildSettings` / `BuildRepositorySettings` provider reads;
  - Collector exclusions are read from `FYAssetABSettings`.
- Unity manual smoke:
  - folder-owned asset exclude/restore;
  - direct File Collector removal;
  - deleted Collector no longer leaves stale Curate rows;
  - nested folder Inspector shows parent coverage;
  - Scene Project Scan produces file-only Scene collectors;
  - AA/AB runtime hotfix settings read from backend settings.

## Non-Goals

- Do not change runtime loading APIs.
- Do not change Lua-C# bridge behavior.
- Do not convert Project Scan to all-file collectors.
- Do not add per-Collector exclude lists.
- Do not implement AB cumulative hotfix package shape in this plan.
