# Collaboration Conventions

## Core Rules
- Read `context/INDEX.md` before starting non-trivial work.
- Use Chinese for developer communication; keep code, commit messages, and technical identifiers in English.
- For user-facing editor/UI code comments, prefer Chinese descriptions while keeping technical terms and proper nouns in English.
- New Lua-callable C# types must sync `TypeMemberListSO` configuration.
- Keep `context/` in English and aligned with the latest verified project reality.
- Keep plans, approvals, sequencing, and progress tracking inside `requirements/`.

## Requirement Workflow
- Keep executable shared plans in `requirements/plan/` and the authoritative status table in `requirements/plan.md`.
- Track detailed execution history in `requirements/progress.txt`.
- Keep planning aligned to `requirements/plan.md`.
- Do not create `requirements/{id}/plan.md` or `requirements/{id}/plan/` unless the developer explicitly asks for an isolated planning structure.
- Use the shared `requirements/plan/`, `requirements/plan/drafts/`, and `requirements/review/` queues for active plans, drafts, and reviews.
- Use `requirements/README.md` as the requirements workspace guide.
- Do not keep scattered requirement-local progress logs long term; merge detailed entries into `requirements/progress.txt` before deleting standalone requirement folders.
- Never replace detailed progress history with summary-only consolidation.

## Resource-Management Rules
- Use `AAPackageManager` in known AA runtime paths and `ABPackageManager` in known AB runtime paths. `AssetPackageManager` is a compatibility facade for legacy callers only.
- `IAssetIndex` and `IPackageBackend` remain AB runtime contracts; do not route new AA mainline code through them merely for symmetry.
- The shared hotfix state machine (`HotfixFlowBase`), concrete `AAHotfixManager` / `ABHotfixManager` flows, `NetworkDownloader`, and AA `CatalogUpdater` remain high-risk.
- Windows Hotfix package and Bundle names are single safe filename segments: reject invalid filename characters, reserved device basenames, trailing dots/spaces, and case-insensitive duplicates.
- Full baseline staging and runtime package inspection require the manifest's exact Bundle filename set with matching non-zero CRC and declared size.
- A failed target may be deleted only before PackageManager initialization succeeds. After initialization, pointer persistence failure must retain live content, remain fatal, and must not signal completion.
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
- The `[Component]` log prefix is a load-bearing contract: CLI batch log parsers (`CommandLine/*.py`) and Unity Console filtering match on it. Keep it; do not drop it during cleanups.
- Produce the prefix from a per-class `private const string Tag = "[ClassName]";` and emit `$"{Tag} message"`. Prefer `Tag` in any class whose prefix appears more than once.
- Do not add new inline `$"[{nameof(X)}] ..."` occurrences; migrate touched call sites to `Tag`. Existing literal `[Component]` prefixes remain valid — no mass rewrite.
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
  | `VersionRecord` | Existing project asset at `FYAssetSettings.VersionRecordPath`; Repository displays and resets it, but does not auto-create it |
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
