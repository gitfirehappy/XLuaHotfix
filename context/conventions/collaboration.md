# Collaboration Conventions

## Core Rules
- Read `context/INDEX.md` before starting non-trivial work.
- Use Chinese for developer communication; keep code, commit messages, and technical identifiers in English.
- Ask before implementing major runtime-loading, hotfix-pipeline, or Addressables replacement changes.
- New Lua-callable C# types must sync `TypeMemberListSO` configuration.

## Requirement Workflow
1. Create `requirements/{id}/brief.md` for substantial work.
2. Track execution in `requirements/{id}/progress.txt`.
3. Keep sub-plans in `requirements/{id}/plan*.md`.
4. After sign-off, migrate verified lessons into `context/`.

## Resource-Management Rules
- Prefer `AssetPackageManager` over direct Addressables usage in new runtime code.
- Current runtime abstractions include `IAssetIndex` and `IPackageBackend`.
- Hotfix core flow (`HotfixManager`, `NetworkDownloader`, `CatalogUpdater`) remains high-risk and must be explicitly approved before modification.
- LINQ is allowed in editor/build code and in low-frequency runtime flows such as startup, hotfix checks, catalog switching, and maintenance operations.
- LINQ must not be used in gameplay-sensitive hot paths, repeated runtime query loops, asset resolve/filter core paths, or public runtime APIs whose call frequency is uncertain.
- If a method may be used both during startup and gameplay, default to loop-based implementations (`for`, `foreach`, `Dictionary`, `HashSet`, cached lookups) instead of LINQ.

## Git Rules
- Commit types: `feat`, `fix`, `refactor`, `docs`, `chore`.
- Run XLua Generate Code before commits that affect XLua-exposed APIs.
