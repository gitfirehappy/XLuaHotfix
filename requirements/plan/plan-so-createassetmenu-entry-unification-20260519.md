# Sub-Plan SOE-1: ScriptableObject Creation Entry Unification

> **Risk**: Low
> **Dependencies**: Existing Editor panels and tool-owned creation flows
> **Status**: Signed off — 2026-05-20
> **Source Draft**: `drafts/archive/draft-so-createassetmenu-entry-unification-20260519.md`
> **Positioning**: Editor workflow cleanup. No runtime behavior or data model changes.

---

## Objective

Unify ScriptableObject creation entry points so each tool-owned asset type has exactly one authoritative creation path.

If a ScriptableObject type already has a dedicated GUI creation button or an automatic tool-owned creation flow, it must not also expose a generic `CreateAssetMenu` entry. This prevents unsafe duplicate assets, inconsistent default values, and unclear ownership of canonical asset paths.

This plan also adds a developer-facing documentation page that lists where each important SO type should be created.

---

## Background

Current verified state:

| Type | Current creation paths | Problem |
|------|------------------------|---------|
| `FYAssetSettings` | `SettingsPanel` create button + `LoadOrCreate()` + `CreateAssetMenu` | Duplicate entry; canonical path is tool-owned |
| `VersionDataBase` | `VersionPanel` create button + `CreateAssetMenu` | Duplicate entry; path is controlled by `FYAssetSettings.VersionDataBasePath` |
| `ScriptObjectDataBase` | `SOAddressableTagger` create button + `CreateAssetMenu` | Duplicate entry; owned by SO tagger workflow |
| `ScriptObjectContainer` | Lua tooling creates containers + `CreateAssetMenu` | Duplicate entry; containers should be tied to tool/database workflow |

Types that still rely on manual asset creation and have no stronger dedicated creation flow should keep `CreateAssetMenu`.

---

## Design Decisions

### D1: Tool-Owned SO Types Must Not Expose Generic Create Menus

When an Editor tool owns the path, defaults, or lifecycle of an SO asset, that tool is the authoritative creation entry. A generic right-click asset menu is not allowed for that type.

### D2: `LoadOrCreate()` Implies No `CreateAssetMenu`

If a singleton/settings asset can be created automatically through `LoadOrCreate()`, exposing a generic menu entry risks creating duplicate assets outside the canonical path.

### D3: Manual Config SO Types Keep `CreateAssetMenu`

Asset types that represent user-authored configuration and have no dedicated creation panel remain menu-createable.

### D4: Documentation Is Required

Removing generic menu entries makes the correct creation path less discoverable unless there is a documentation map. This plan must add a docs page that lists every relevant SO type and its expected creation entry.

---

## Planned Changes

| Area | File | Change |
|------|------|--------|
| Settings asset | `Assets/FYAsset/Scripts/FYAssetSettings.cs` | Remove `[CreateAssetMenu(...)]`; keep `SettingsPanel` and `LoadOrCreate()` as the creation paths |
| Version asset | `Assets/FYAsset/Scripts/Build/Versioning/VersionDataBase.cs` | Remove `[CreateAssetMenu(...)]`; keep `VersionPanel.CreateVersionDB()` as the creation path |
| SO tag database | `Assets/FYAsset/Scripts/Helpers/ScriptObjectDataBase.cs` | Remove `[CreateAssetMenu(...)]`; keep `SOAddressableTagger.CreateNewDatabase()` as the creation path |
| SO tag container | `Assets/FYAsset/Scripts/Helpers/ScriptObjectContainer.cs` | Remove `[CreateAssetMenu(...)]`; keep tool/database-driven creation as the creation path |
| Developer docs | `docs/FYAsset/so-创建入口说明.md` | Add a Chinese developer guide that maps SO types to creation entries |
| Docs index | `docs/FYAsset/资源管理架构文档.md` | Add the new docs page to the FYAsset document index |

---

## Types That Must Keep `CreateAssetMenu`

| Type | Reason |
|------|--------|
| `LuaAutoSyncConfig` | Manual sync configuration asset; no stronger dedicated create flow |
| `TypeMemberListSO` | Manual XLua type configuration asset |
| `LuaBehaviourConfigSO` | Manual bridge configuration asset |
| `ScriptObjectBridgeConfig` | Manual bridge configuration asset |
| `StateAnimationConfigSO` | Manual bridge configuration asset |
| `UIResourceConfigSO` | Manual UI resource configuration asset |
| `UIFormConfigSO` | Manual UI form configuration asset |
| `ConfigConvertSettings` | Manual config conversion settings asset |
| `ConfigConvertChannel` | Manual config conversion channel asset |
| `CharacterConfig` | Manual dialogue/gameplay configuration asset |
| `PlayerControllerSO` | Manual gameplay configuration asset |

---

## Already Internal Or Auto-Created Types

These types do not require `CreateAssetMenu` changes in this plan:

| Type | Entry |
|------|-------|
| `BuildSnapshots` | Created by `DifferentialProcessor` |
| `LuaScriptsIndex` | Created by `LuaScriptsIndexExporter` |
| `BuildPipelineConfig` | Created by `PipelinePanel` |
| `CollectorSetting` | Created by `CollectorSettingPanel` |
| `LuaDataBase` | Created by Lua editor tooling |
| `LuaScriptContainer` | Created by Lua editor tooling / directory scanner |

---

## Task Breakdown

| Task | Content | Depends On |
|------|---------|------------|
| SOE1-T1 | Remove `CreateAssetMenu` from `FYAssetSettings` | — |
| SOE1-T2 | Remove `CreateAssetMenu` from `VersionDataBase` | — |
| SOE1-T3 | Remove `CreateAssetMenu` from `ScriptObjectDataBase` and `ScriptObjectContainer` | — |
| SOE1-T4 | Add `docs/FYAsset/so-创建入口说明.md` with the SO entry map | T1-T3 classification |
| SOE1-T5 | Add the docs page to `docs/FYAsset/资源管理架构文档.md` | T4 |
| SOE1-T6 | Verification: search `CreateAssetMenu`, inspect key GUI paths, and compile | T1-T5 |

---

## Invariants

1. No ScriptableObject data schema changes.
2. No runtime behavior changes.
3. Canonical asset paths must remain unchanged.
4. `FYAssetSettings.Instance` must still create/load `Assets/Resources/FYAssetSettings.asset`.
5. `VersionPanel` must remain able to create `VersionDataBase`.
6. `SOAddressableTagger` must remain able to create `ScriptObjectDataBase`.
7. Manual config assets without dedicated creation tooling must keep `CreateAssetMenu`.

---

## Acceptance Criteria

- [ ] `rg "\[CreateAssetMenu" Assets` no longer reports the four removed entries.
- [ ] `FYAssetSettings` can still be created through `SettingsPanel` or `LoadOrCreate()`.
- [ ] `VersionDataBase` can still be created through `VersionPanel`.
- [ ] `ScriptObjectDataBase` can still be created through `SOAddressableTagger`.
- [ ] Lua container/database tool workflows still create or reference `LuaDataBase` / `LuaScriptContainer` as before.
- [ ] `docs/FYAsset/so-创建入口说明.md` lists the recommended entry for each relevant SO type.
- [ ] `dotnet build XLuaHotfix.sln` or Unity Editor compilation passes with 0 new errors.

---

## Out of Scope

- Reworking the SO tagger UI.
- Reworking Lua tooling.
- Adding new creation buttons for all remaining menu-created SO types.
- Moving existing `.asset` files.
- Changing Addressables group membership.
- Changing XLua code generation configuration.

---

## Approval Checklist

- [x] Remove generic `CreateAssetMenu` from the four tool-owned SO types only.
- [x] Keep `CreateAssetMenu` for manual config SO types that do not have a stronger creation entry.
- [x] Add the developer-facing SO creation entry documentation page.
- [x] Do not change SO schemas, asset paths, Addressables labels, or runtime behavior.

---

## Change Log

| Date | Change |
|------|--------|
| 2026-05-19 | Promoted from draft into executable plan with docs deliverable |
| 2026-05-20 | Approved by developer; all checklist items confirmed |
| 2026-05-20 | Executed; removed four duplicate `CreateAssetMenu` entries and verified SO docs map |
| 2026-05-20 | Signed off by developer; developer said continue |
