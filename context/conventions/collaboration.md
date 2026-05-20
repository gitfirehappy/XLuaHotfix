# Collaboration Conventions

## Core Rules
- Read `context/INDEX.md` before starting non-trivial work.
- Use Chinese for developer communication; keep code, commit messages, and technical identifiers in English.
- For user-facing editor/UI code comments, prefer Chinese descriptions while keeping technical terms and proper nouns in English.
- New Lua-callable C# types must sync `TypeMemberListSO` configuration.
- Keep `context/` in English and aligned with the latest verified project reality.
- Keep plans, approvals, sequencing, and progress tracking inside `requirements/`.

## Requirement Workflow
- Create `requirements/{id}/brief.md` for substantial work.
- Track execution in `requirements/{id}/progress.txt`.
- Keep sub-plans in `requirements/{id}/plan*.md`.
- Use `requirements/drafts/` for discussion-only drafts before a formal plan exists.

## Resource-Management Rules
- Prefer `AssetPackageManager` over direct Addressables usage in new runtime code.
- Current runtime abstractions include `IAssetIndex` and `IPackageBackend`.
- Hotfix core flow (`HotfixManager`, `NetworkDownloader`, `CatalogUpdater`) remains high-risk.
- LINQ is allowed in editor/build code and in low-frequency runtime flows such as startup, hotfix checks, catalog switching, and maintenance operations.
- LINQ must not be used in gameplay-sensitive hot paths, repeated runtime query loops, asset resolve/filter core paths, or public runtime APIs whose call frequency is uncertain.
- If a method may be used both during startup and gameplay, default to loop-based implementations (`for`, `foreach`, `Dictionary`, `HashSet`, cached lookups) instead of LINQ.

## Error Handling Rules
- Construct `RuntimeMessage` / `BuildMessage` exclusively through static factory methods; never use bare `new`.
- Public load APIs return `(T, RuntimeMessage)` tuples — errors are values, not exceptions.
- Use `RuntimeErrorCodes` / `BuildErrorCodes` constants for the `Code` field; do not hardcode error code strings.
- `RuntimeSeverity.Error` for operation failures; `RuntimeSeverity.Warning` is reserved for degraded-but-usable paths (currently zero consumers — infrastructure only).
- Build-time diagnostics use `BuildMessage` with a `Source` field identifying the file or collector path.
- `FileHelper.TryDelete` / `TryDeleteDirectory` return `bool` and never throw — callers decide whether to care.

## Logging Rules
- Direct `Debug.Log*` diagnostics in FYAsset framework, tool, backend, manager, editor, and cross-module code may use a `[Component]` prefix for Unity Console filtering.
- `RuntimeMessage.Message`, `BuildMessage.Message`, and `BuildTaskResult.ErrorMessage` human-readable descriptions must not start with a `[Component]` prefix; component ownership belongs in direct logs, source fields, or call-site context.
- UI-facing and upper-layer runtime error display should prefer `RuntimeMessage.ToString()` / `BuildMessage.ToString()` style output: `[Code] Message`.
- Avoid combining component prefixes and error-code prefixes in the same user-facing message, for example avoid `[LOAD_FAILED] [AAHotfixBackend] ...`.

## ScriptableObject Rules
- Add `[CreateAssetMenu]` only when the SO type must be created manually by a developer via the Project window right-click menu.
- Do NOT add `[CreateAssetMenu]` when the SO is already auto-created by code (singleton, build pipeline, directory scanner, etc.) or when a dedicated EditorWindow/GUI panel provides a unified creation flow.

  **Known auto/GUI-created SOs (must not have `[CreateAssetMenu]`):**
  | SO Type | Creation Mechanism |
  |---|---|
  | `FYAssetSettings` | Auto via singleton `LoadOrCreate()` |
  | `ScriptObjectDataBase` | GUI via `SOAddressableTagger` EditorWindow |
  | `VersionDataBase` | GUI via `VersionPanel` |
  | `ScriptObjectContainer` | Tool/database-driven SO container workflow |
  | `LuaScriptContainer` | GUI via `LuaFileCreatorWindow` + auto via `LuaDirectoryScanner` |
  | `LuaDataBase` | GUI via 3 EditorWindows |

  **Known manual-only SOs (correctly have `[CreateAssetMenu]`):**
  | SO Type | menuName |
  |---|---|
  | `LuaAutoSyncConfig` | `XLua/Lua Auto Sync Config` |
  | `TypeMemberListSO` | `XLua/Type List` |
  | `ScriptObjectBridgeConfig` | `XLua/Bridge/SOBridgeConfig` |
  | `LuaBehaviourConfigSO` | `XLua/Bridge/Behaviour Config SO` |
  | `StateAnimationConfigSO` | `XLua/Bridge/State Animation Config` |
  | `CharacterConfig` | `Dialogue/Character Config` |
  | `PlayerControllerSO` | `Player/PlayerControllerSO` |
  | `UIResourceConfigSO` | `UI/Resource Config` |
  | `UIFormConfigSO` | `UI/UI Form Config` |
  | `ConfigConvertSettings` | `Config/ConfigConvertSetting` |
  | `ConfigConvertChannel` | `Config/ConfigConvertChannel` |

## Git Rules
- Commit types: `feat`, `fix`, `refactor`, `docs`, `chore`.
- Run XLua Generate Code before commits that affect XLua-exposed APIs.
