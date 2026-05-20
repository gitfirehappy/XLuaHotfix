# Draft: SO CreateAssetMenu 入口统一

Status: Promoted
Date: 2026-05-19
Promoted to: `requirements/plan/plan-so-createassetmenu-entry-unification-20260519.md`

## Promotion Note

This draft was promoted into an executable plan.
The plan keeps the verified classification and adds a required developer-facing docs deliverable.

## Original Direction

Project-wide `ScriptableObject` creation paths are split across:

- `CreateAssetMenu`
- GUI/Editor window creation
- automatic creation during sync/build/export flows

The draft direction is to keep only one primary creation entry per SO type.
If an asset type already has a clear GUI creation path or is created automatically by tooling, it should not also expose a `CreateAssetMenu` menu item.

## Draft Decisions

### Remove `CreateAssetMenu`

- `FYAssetSettings`
- `VersionDataBase`
- `ScriptObjectDataBase`
- `ScriptObjectContainer`

### Keep `CreateAssetMenu`

- `LuaAutoSyncConfig`
- `TypeMemberListSO`
- `LuaBehaviourConfigSO`
- `ScriptObjectBridgeConfig`
- `StateAnimationConfigSO`
- `UIResourceConfigSO`
- `UIFormConfigSO`
- `ConfigConvertSettings`
- `ConfigConvertChannel`
- `CharacterConfig`
- `PlayerControllerSO`

### Already automatic / internal only

- `BuildSnapshots`
- `LuaScriptsIndex`
- `BuildPipelineConfig`
- `CollectorSetting`
- `LuaDataBase`
- `LuaScriptContainer`

