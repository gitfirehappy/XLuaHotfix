# Collaboration Conventions

## Core Rules
- Read `context/INDEX.md` before starting non-trivial work.
- Use Chinese for developer communication; keep code, commit messages, and technical identifiers in English.
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

## Git Rules
- Commit types: `feat`, `fix`, `refactor`, `docs`, `chore`.
- Run XLua Generate Code before commits that affect XLua-exposed APIs.
