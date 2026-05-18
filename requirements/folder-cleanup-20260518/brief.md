# folder-cleanup-20260518 Brief

## Goal

Reorganize FYAsset Hotfix and Runtime folders so directory names reflect current responsibilities instead of migration history.

## Background

`ABHotfixBackend` lived under `LegacyRuntime/` even though it implements the AB hotfix path. The same area also mixed orchestration, legacy Addressables catalog support, backend contracts, package cleanup, and AB backend code. Runtime contracts and the `AssetPackageManager` facade also used broad or historical directory names.

## Scope

- Move Hotfix orchestration and backend files into `Assets/FYAsset/Scripts/Hotfix/`.
- Move runtime loading contracts into `Assets/FYAsset/Scripts/Runtime/Contracts/`.
- Move `AssetPackageManager` into `Assets/FYAsset/Scripts/Runtime/Facade/`.
- Split Hotfix data types from `IHotfixPipeline.cs` into separate files.
- Remove the empty historical `Helpers/Helper/` directory.
- Sync project files and documentation paths.

## Out Of Scope

- Initial Hotfix/Runtime cleanup did not reorganize Build directories; Build cleanup is tracked separately in `plan/plan-build-folder-cleanup.md`.
- No runtime behavior changes.
- No namespace refactor.
- No Addressables or AB flow replacement.
