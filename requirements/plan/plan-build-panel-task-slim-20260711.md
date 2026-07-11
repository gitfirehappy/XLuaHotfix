# Build Panel and Task Simplification

## Status

Implemented and statically verified. Unity Editor build/report acceptance is pending.

## Approved Changes

1. Remove the standalone Version panel; show the current version and test reset action in Repository.
2. Persist AA catalog and remote bundle Build Paths through public Addressables Profile APIs, write directly to the final package, and keep Load Paths unchanged.
3. Open the official Addressables Report from AA Results and retain local package management.
4. Add AB bundle-to-asset expansion and reverse bundle references without inventing unavailable uncompressed-size data.
5. Remove the Pipeline Validate UI and `IBuildTask` dependency/read/write declarations while retaining minimal task resolution/backbone checks and `TaskVerifyBuildResult`.

## Acceptance

- `dotnet build XLuaHotfix.sln --no-restore` succeeds.
- No `IBuildTask.DependsOn`, `ReadKeys`, `WriteKeys`, Pipeline Validate call, Version panel, or ServerData handoff remains.
- In Unity, run AA Full and Hotfix builds and confirm the persisted Profile Build Paths, final catalog/bundle layout, and official report values.
- In Unity, inspect new and old AB reports and verify bundle expansion, dependency, reverse-reference, and detail rendering.
- Confirm Repository version display and the guarded `1.0.0` reset action.

## Boundaries

- Do not change AA Remote/Catalog Load Paths.
- Do not modify Addressables package source or use reflection.
- Do not remove AB output verification.
