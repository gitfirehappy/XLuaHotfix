# Migration: Constants → FYAssetConstants + BuildContextKeys Split

> **Risk**: Low (rename + relocate, zero logic change)
> **Dependencies**: None (no other sub-plan depends on this)
> **Status**: Realized
> **Type**: Migration / Code Organization

---

## Objective

Move `Assets/Global/Scripts/Constants.cs` (27 fields + 10 BuildContextKeys) into FYAsset module, rename from generic `Constants` to module-scoped `FYAssetConstants`, reorganize fields by pipeline era + purpose, and extract pipeline-specific `BuildContextKeys` into its own file.

### Background

`Constants` lives in `Assets/Global/Scripts/` (project-wide namespace) but 90%+ of its fields exclusively serve FYAsset (Addressables paths, pipeline config, collector rules). `BuildContextKeys` is defined in the same file as a separate class — it serves only the build pipeline and should live nearby.

## Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| D1 | Rename `Constants` → `FYAssetConstants` | Explicit module ownership; avoids collision with other project-level constants |
| D2 | BuildContextKeys stays independent class in separate file | Single responsibility; pipeline-specific keys shouldn't clutter module-level constants. File at `Build/BuildContextKeys.cs` (Runtime assembly — needed by both Editor and Runtime code) |
| D3 | Old-pipeline fields **kept**, reorganized with new fields | Not deleting — old pipeline still runs. Group by era then by category (paths / identifiers / naming) |
| D4 | Global switch (`USE_AB_BACKEND`) placed top-level with project name | It's a pipeline-era toggle — same "global configuration" tier as `PROJECTNAME`, `HOTFIX_URL` |
| D5 | SystemIdentifiers / RuntimeErrorCodes / BuildErrorCodes stay put | Already under FYAsset, already single-responsibility |

## Target Grouping

```
#region 项目/全局开关
  PROJECTNAME, HOTFIX_URL, USE_AB_BACKEND

#region 旧管线 — 文件路径
  AA_LABELS_CONFIG_ASSETPATH, LUA_SCRIPTS_INDEX_ASSETPATH,
  SNAPSHOT_ASSET_PATH, BUILD_INDEX_JSON_PROJECT_PATH

#region 旧管线 — 标识符
  AA_LABELS_CONFIG, LUA_SCRIPTS_INDEX,
  DEAULT_XLUA_TYPE_CONFIG_LOAD_LABEL, HOTFIX_GROUP_NAME,
  HELPER_BUILD_DATA_GROUP_NAME, BUILD_INDEX_FILENAME

#region 新管线 — 文件路径
  BINARY_SERIALIZER_GENERATE_PATH, COLLECTOR_SETTING_ASSET_PATH,
  PIPELINE_CONFIG_ASSET_PATH

#region 新管线 — 文件命名
  MANIFEST_FILE_NAME, MANIFEST_FILE_NAME_BIN

#region Collector Rules
  RULE_ADDRESS_BY_FILE_NAME, RULE_COLLECT_ALL,
  RULE_PACK_BY_COLLECT_PATH, RULE_PACK_SEPARATELY,
  RULE_PACK_BY_DIRECTORY, RULE_PACK_BY_LABEL,
  RULE_GROUP_ALL, RULE_GROUP_BY_TYPE, RULE_GROUP_BY_LABEL,
  RULE_GROUP_BY_DIRECTORY
```

## Files Changed

| Op | File | Details |
|----|------|---------|
| **Create** | `Assets/FYAsset/Scripts/FYAssetConstants.cs` | 27 fields, regrouped |
| **Create** | `Assets/FYAsset/Scripts/Build/BuildContextKeys.cs` | 10 keys, standalone file (Runtime assembly) |
| **Delete** | `Assets/Global/Scripts/Constants.cs` | Migration complete |
| **Edit** | 13 files × 37 references | `Constants.` → `FYAssetConstants.` |
| **Edit** | `Assembly-CSharp.csproj` | Remove 3 old, add 2 new Compile Includes |

### Reference File List (37 occurrences)

| File | Count | Notes |
|------|-------|-------|
| DifferentialProcessor.cs | 9 | Old pipeline — heaviest |
| ABHotfixBackend.cs | 6 | Old pipeline runtime |
| HelperBuildDataExporter.cs | 5 | Old pipeline build export |
| LocalStatusExporter.cs | 4 | Old pipeline |
| AssetPackageManager.cs | 3 | Compatibility layer |
| HotfixManager.cs | 2 | Pipeline toggle |
| ManifestLoader.cs | 2 | New pipeline |
| GameLauncher.cs | 1 | URL constant |
| XLuaLoader.cs | 1 | XLua config label |
| XluaTypeConfigLoader.cs | 1 | XLua config label |
| PathManager.cs | 1 | Path constant |
| BinarySerializerGenerator.cs | 1 | Tool path |
| BuildProjectManager.cs | 1 | Old pipeline |

## Task Breakdown

| # | Task | Content |
|---|------|---------|
| M-T1 | Create `FYAssetConstants.cs` | New file with 27 fields in 6 regions |
| M-T2 | Create `BuildContextKeys.cs` | Extract 10 keys into independent file at `Build/BuildContextKeys.cs` |
| M-T3 | Global replace `Constants.` → `FYAssetConstants.` | 13 files, 37 occurrences |
| M-T4 | Update csproj references | Assembly-CSharp.csproj: remove 3 old includes, add 2 new |
| M-T5 | Delete `Global/Scripts/Constants.cs` | Old file removal |
| M-T6 | Build verification | `dotnet build XLuaHotfix.sln` → 0 errors |

## Invariants

1. `dotnet build XLuaHotfix.sln` → 0 errors
2. All 37 `Constants.XXX` references resolve to `FYAssetConstants.XXX`
3. All 10 `BuildContextKeys.XXX` references resolve to same class (class name unchanged, file location only)
4. SystemIdentifiers / RuntimeErrorCodes / BuildErrorCodes untouched
5. Zero logic changes — pure rename + relocate + regroup

## Not In Scope

- Deleting old-pipeline constants (still used)
- Renaming individual field names (beyond adding grouping regions)
- Creating nested class `Constants.BuildContextKeys` (D2: stays independent)

---

## Change Log

| Date | Change |
|------|--------|
| 2026-04-30 | Initial draft — 6 regions, 6 tasks, 5 invariants |
| 2026-04-30 | **Realized**: All 6 tasks completed. 2 files created, 1 deleted, 13 files × 37 references replaced, build passes 0 errors. |
