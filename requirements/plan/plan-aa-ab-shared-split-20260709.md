# FYAsset Strict AA/AB/Shared Split Plan 2026-07-09

> **Status**: Implemented / Static Verified / Editor acceptance pending
> **Requirement ID**: aa-ab-shared-split-20260709
> **Origin**: A1, A3, A5, and A9 from `requirements/plan/drafts/draft-fyasset-architecture-review-20260707.md`
> **Scope**: Split AA/AB runtime, hotfix, build, and settings ownership into strict `AA/`, `AB/`, and `Shared/` script roots while keeping thin compatibility facades.

## Goal

Make AA and AB independent framework paths instead of one runtime/build mainline routed by `UseABBackend`, while keeping a
small compatibility layer for existing call sites and tests during migration.

## Locked Decisions

1. Root code layout:
   - `Assets/FYAsset/Scripts/AA`
   - `Assets/FYAsset/Scripts/AB`
   - `Assets/FYAsset/Scripts/Shared`
   No other top-level script directories remain after this cleanup.
2. Settings ownership:
   - `FYAssetSettings` belongs to `Shared`
   - `FYAssetAASettings` belongs to `AA`
   - `FYAssetABSettings` belongs to `AB`
3. Keep the three settings assets independent; do not merge them into one ScriptableObject.
4. Deduplicate settings `LoadOrCreate` through a shared base/helper during this split.
5. Runtime managers split into `AAPackageManager` and `ABPackageManager`.
6. Hotfix flows split into AA and AB concrete managers/flows.
7. Build managers split into `AABuildProjectManager` and `ABBuildProjectManager`.
8. Thin compatibility facades remain under `Shared/Compatibility`:
   - `AssetPackageManager`
   - `HotfixManager`
   - `BuildProjectManager`
9. `UseABBackend` remains only for compatibility/test-glue routing, not for the build/runtime mainline.
10. Shared infrastructure remains single-copy:
    - pipeline runner
    - repository
    - versioning
    - path/serialization utilities
    - common data contracts that are genuinely backend-neutral
11. Do not change Addressables/AssetBundle package formats, hot-update protocol, or repository object format as part of
    this split.
12. Build editor ownership follows the same split: AA and AB have separate windows composed over one shared shell; shared Settings and Version panels appear in both.
13. Repository uses two native horizontal splitters for its three panes and persists AA/AB pane widths independently in EditorPrefs.
14. AA Repository resolves persisted GUID identities into Address plus asset path for presentation only; repository diff identity and JSON remain GUID-based.
15. AA-only Repository maintenance is injected by the AA window. It owns HotfixGroup recovery and never shares Test Reset, package, or Repository mutation behavior with AB.

## PRS Design Boundary

### Paradigm

- Backend-specific execution: AA and AB each own runtime load, hotfix, and build entrypoints.
- Shared infrastructure: backend-neutral repository/versioning/pipeline/path/serialization code remains one copy.
- Compatibility routing: old public facade names remain as migration adapters only.

### Rules

| Condition | Action | Order | Recovery |
|-----------|--------|-------|----------|
| New AA runtime/build/hotfix code is added | Place it under `AA` and call AA concrete entrypoints | AA/AB split first, facade after | Use compatibility facade only for old callers |
| New AB runtime/build/hotfix code is added | Place it under `AB` and call AB concrete entrypoints | AB concrete first, facade after | Keep shared utility only if backend-neutral |
| Old caller still uses shared facade | Route through compatibility using `UseABBackend` | After concrete managers exist | Keep behavior compatible until caller migration |
| Settings asset is loaded/created | Use shared `LoadOrCreate` helper/base | Before moving settings callers | Fail fast/log if asset cannot be created |

### System

#### Public Entrypoints

- `AAPackageManager`: AA runtime package loading entrypoint.
- `ABPackageManager`: AB runtime package loading entrypoint.
- `AAHotfixManager` or equivalent AA concrete hotfix flow.
- `ABHotfixManager` or equivalent AB concrete hotfix flow.
- `AABuildProjectManager`: AA build entrypoint.
- `ABBuildProjectManager`: AB build entrypoint.
- Compatibility facades keep the old public names and route only where needed.
- `AABuildPipelineWindow` and `ABBuildPipelineWindow` are independent editor entrypoints; the old menu routes to one for compatibility.
- `AAHotfixGroupMaintenancePanel` is an AA editor adapter hosted by `RepositoryStatusPanel`; AB does not register it.

#### Integration Points

- Depends on shared settings/load helper, repository, versioning, serialization, path utilities, and pipeline runner.
- Depended on by editor panels, command-line build/push flows, runtime callers, and tests.

## Implementation Checklist

1. Survey current FYAsset script layout and `.meta` files before moving anything.
2. Create the AA/AB/Shared directory layout with Unity `.meta` preservation handled carefully.
3. Move settings classes into their owned areas and extract `LoadOrCreate` dedup into Shared.
4. Split runtime package managers into AA and AB concrete managers.
5. Split hotfix orchestration into AA and AB concrete flows.
6. Split build project managers into AA and AB concrete entrypoints.
7. Add/trim compatibility facades under `Shared/Compatibility`.
8. Replace mainline callers with concrete AA/AB entrypoints where the backend is already known.
9. Leave `UseABBackend` only in compatibility/test glue and editor places that intentionally route between old APIs.
10. Update project files if Unity-generated `.csproj` files are tracked.
11. Remove old empty script folders and their `.meta` files after moving `.cs`/`.meta` pairs.
12. Update context/docs after code verification, not before.
13. Add AA Repository presentation/recovery follow-up: show Address plus path while preserving GUID identity, expose pending HotfixGroup recovery, preserve unresolved undo records, and allow explicit record-only discard.

## Acceptance Criteria

- AA and AB concrete runtime managers exist and are callable independently.
- AA and AB concrete hotfix flows exist and are callable independently.
- AA and AB concrete build managers exist and are callable independently.
- Compatibility facades are thin and live under `Shared/Compatibility`.
- `UseABBackend` is no longer part of build/runtime mainline implementation.
- Three settings assets remain independent, with shared `LoadOrCreate` logic.
- Shared infrastructure is not duplicated into AA and AB folders.
- Existing external call sites continue to compile through compatibility facades where not yet migrated.
- AA Repository exposes readable Address/path names without changing ArtifactDigest or stored repository objects.
- AA Repository recovery retains unresolved undo records and can discard only those records after confirmation.
- `Assets/FYAsset/Scripts/` has exactly `AA`, `AB`, and `Shared` as top-level code directories.
- Active docs, context, requirements, and tracked `.csproj` entries describe current FYAsset script code only under those three roots.

## Verification

- `dotnet build XLuaHotfix.sln --no-restore`
- `git diff --check`
- Static checks:
  - `Assets/FYAsset/Scripts/` top-level directory listing is exactly `AA`, `AB`, `Shared`
  - concrete AA/AB manager entrypoints exist
  - compatibility facades contain routing only
  - `UseABBackend` references are limited to compatibility/test/editor glue
  - no duplicated repository/versioning/pipeline/serialization implementation under both AA and AB

## Non-Goals

- No AA deprecation.
- No Addressables removal.
- No package format change.
- No repository object format change.
- No HandleRegistry generation simplification.
- No preview cache or incremental build implementation.
